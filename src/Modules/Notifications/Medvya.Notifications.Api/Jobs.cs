using Medvya.BuildingBlocks.Infrastructure;
using Npgsql;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Hosting;
using Medvya.Notifications.Application;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Microsoft.AspNetCore.Authorization;

namespace Medvya.Notifications.Api;

/// <summary>
/// Hangfire on PostgreSQL (ADR-001, MDV-P1-032): one recurring job publishes each tenant's outbox and then sends what is due, so a
/// confirmation leaves within about a minute of booking and reminders at their time. No Redis, no Service Bus.
/// shortcut: the server runs inside Host.Api; move it to the Worker.Jobs host (docs/03-architecture/repository-structure.md) when load or deploys need it.
/// </summary>
public sealed partial class NotificationJobs(IServiceProvider services, ILogger<NotificationJobs> log)
{
    public const string JobId = "notifications-run";
    public const string DashboardPolicy = "jobs-operations";
    const int Batch = 100;

    /// <summary>One run over every tenant that has written an outbox event. A tenant that fails does not stop the others; the run then fails so Hangfire shows it.</summary>
    [DisableConcurrentExecution(300), AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> withEvents, indexed;
        await using (var scope = services.CreateAsyncScope())
        {
            withEvents = await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().TenantsAsync(cancellationToken);
            indexed = await scope.ServiceProvider.GetRequiredService<IComplianceFacts>().TenantsAsync(cancellationToken);
        }
        var failed = 0;
        foreach (var tenant in withEvents.Union(indexed))
        {
            try
            {
                await using var scope = services.EnterLinkTenant(tenant);
                if (withEvents.Contains(tenant))
                {
                    var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
                    while (await dispatcher.DispatchAsync(Batch, cancellationToken) == Batch) { }
                    // The sender holds back all but cancellations until the owner accepts the terms (MDV-P1-019).
                    await scope.ServiceProvider.GetRequiredService<INotificationSender>().SendDueAsync(Batch, cancellationToken);
                }
                // Licence expiry and access review reminders for the owners (MDV-P1-010); idempotent, so every minute is fine.
                await scope.ServiceProvider.GetRequiredService<IComplianceReminders>().RunAsync(cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed++;
                TenantFailed(log, tenant, e.GetType().Name); // the type only: messages can carry addresses
            }
        }
        if (failed > 0) throw new InvalidOperationException($"{failed} tenant run(s) failed.");
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification run failed for tenant {TenantId}: {ErrorType}.")]
    static partial void TenantFailed(ILogger logger, Guid tenantId, string errorType);
}

public static class JobsSetup
{
    /// <summary>
    /// Jobs are off unless Jobs:Enabled (Development turns them on; deployments set it): tests and tools never start a server.
    /// Storage is the Medvya database in its own hangfire schema; the tables are created by db-migrate (as the owner role) in every environment, never by the app (roles.sql).
    /// </summary>
    public static WebApplicationBuilder AddJobs(this WebApplicationBuilder builder, string? connectionString)
    {
        builder.Services.AddAuthorizationBuilder().AddPolicy(NotificationJobs.DashboardPolicy, p => p.RequireAuthenticatedUser().RequireAssertion(c =>
            Guid.TryParse(c.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            && (builder.Configuration.GetSection("Jobs:DashboardUsers").Get<Guid[]>() ?? []).Contains(id)));
        builder.Services.AddRateLimiter(o => o.AddPolicy("receipts", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) })));
        if (connectionString is null || !builder.Configuration.GetValue("Jobs:Enabled", false)) return builder;
        builder.Services.AddScoped<NotificationJobs>();
        builder.Services.AddHangfire(c => c.SetDataCompatibilityLevel(CompatibilityLevel.Version_180).UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(o => o.UseConnectionFactory(new DataSourceConnectionFactory(PostgresDataSources.For(connectionString))), new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = false }));
        builder.Services.AddHangfireServer(o => o.WorkerCount = 2);
        return builder;
    }

    public static WebApplication UseJobs(this WebApplication app)
    {
        if (app.Services.GetService<IRecurringJobManager>() is not { } jobs) return app;
        // Medvya operations only: the policy needs a signed-in user listed in Jobs:DashboardUsers (nobody by default); read-only.
        app.MapHangfireDashboard("/admin/jobs", new DashboardOptions { Authorization = [], IsReadOnlyFunc = _ => true, DashboardTitle = "Medvya jobs" })
            .RequireAuthorization(NotificationJobs.DashboardPolicy);
        jobs.AddOrUpdate<NotificationJobs>(NotificationJobs.JobId, j => j.RunAsync(CancellationToken.None), Cron.Minutely());
        return app;
    }
}

/// <summary>Development only, and only with Notifications:DevAllowAllConsent: lets a local run send without any consent receipt.</summary>
public sealed class DevAllowAllConsent : IReminderConsent
{
    public Task<bool> AllowsAsync(Guid patientId, Medvya.Notifications.Domain.NotificationChannel channel, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

/// <summary>Hangfire's connections come from the shared data source, so they sign in the same way as the modules (Entra token in Azure).</summary>
sealed class DataSourceConnectionFactory(NpgsqlDataSource source) : IConnectionFactory
{
    public NpgsqlConnection GetOrCreateConnection() => source.CreateConnection();
}

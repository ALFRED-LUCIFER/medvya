using System.Security.Claims;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.SqlServer;
using Host.Api.Context;
using Medvya.Notifications.Application;
using Medvya.Scheduling.Application;
using Microsoft.AspNetCore.Authorization;

namespace Host.Api.Notifications;

/// <summary>
/// Hangfire on Azure SQL (ADR-001, MDV-P1-032): one recurring job publishes each tenant's outbox and then sends what is due, so a
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
        IReadOnlyList<Guid> tenants;
        await using (var scope = services.CreateAsyncScope())
            tenants = await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().TenantsAsync(cancellationToken);
        var failed = 0;
        foreach (var tenant in tenants)
        {
            try
            {
                await using var scope = services.EnterLinkTenant(tenant);
                var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
                while (await dispatcher.DispatchAsync(Batch, cancellationToken) == Batch) { }
                await scope.ServiceProvider.GetRequiredService<INotificationSender>().SendDueAsync(Batch, cancellationToken);
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
    /// Storage is the Medvya database in its own hangfire schema; only Development may create the tables (roles.sql explains the deploy step).
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
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = builder.Environment.IsDevelopment() }));
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

/// <summary>Development only, and only with Notifications:DevAllowAllConsent: lets a local run send without the consent module.</summary>
internal sealed class DevAllowAllConsent : IReminderConsent
{
    public Task<bool> AllowsAsync(Guid patientId, Medvya.Notifications.Domain.NotificationChannel channel, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

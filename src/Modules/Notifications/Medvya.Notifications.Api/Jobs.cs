using Medvya.BuildingBlocks.Infrastructure;
using Npgsql;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Hosting;
using Medvya.Notifications.Application;
using Medvya.Scheduling.Application;
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
                // Push what is new in the notification centre (MDV-P5-038) before the reminders below, which throw when an email fails:
                // an email outage must not hold pushes back. A reminder raised below is pushed on the next run, a minute later.
                await scope.ServiceProvider.GetRequiredService<IStaffPush>().SendDueAsync(Batch, cancellationToken);
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
        builder.Services.AddSingleton<RecurringJobRegistration>().AddHostedService(sp => sp.GetRequiredService<RecurringJobRegistration>());
        return builder;
    }

    public static WebApplication UseJobs(this WebApplication app)
    {
        if (app.Services.GetService<RecurringJobRegistration>() is not { } jobs) return app;
        // Medvya operations only: the policy needs a signed-in user listed in Jobs:DashboardUsers (nobody by default); read-only.
        app.MapHangfireDashboard("/admin/jobs", new DashboardOptions { Authorization = [], IsReadOnlyFunc = _ => true, DashboardTitle = "Medvya jobs" })
            .RequireAuthorization(NotificationJobs.DashboardPolicy);
        jobs.Add(NotificationJobs.JobId, m => m.AddOrUpdate<NotificationJobs>(NotificationJobs.JobId, j => j.RunAsync(CancellationToken.None), Cron.Minutely()));
        return app;
    }
}

/// <summary>
/// Registers the recurring jobs after the web app has started, never during start-up (#1365): registering needs a database lock per job, and
/// when a restarting copy still held it the wait timed out, the exception left Program.cs and the clinic app stayed down. A job already
/// stored by an earlier start keeps running meanwhile; registering only creates it or updates its schedule. It keeps trying for as long as
/// the app runs: a job left unregistered until the next deploy would silently send nothing.
/// </summary>
public sealed partial class RecurringJobRegistration(IRecurringJobManager manager, ILogger<RecurringJobRegistration> log) : BackgroundService
{
    readonly List<(string Id, Action<IRecurringJobManager> Register)> pending = [];

    /// <summary>The wait before try n (from 2): 5, 10, 20, 40 s, then a minute. Tests shorten it.</summary>
    public Func<int, TimeSpan> Delay { get; init; } = attempt => TimeSpan.FromSeconds(Math.Min(60, 5 << Math.Min(attempt - 2, 4)));

    /// <summary>Call before the app runs.</summary>
    public void Add(string jobId, Action<IRecurringJobManager> register) => pending.Add((jobId, register));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; pending.Count > 0; attempt++)
        {
            if (attempt > 1) await Task.Delay(Delay(attempt), stoppingToken);
            foreach (var job in pending.ToArray())
            {
                try
                {
                    job.Register(manager);
                    pending.Remove(job);
                }
                // The type only: exception text can carry connection details. The first five tries, then every tenth (about ten minutes apart), so a lock held for hours does not flood the log.
                catch (Exception e) { if (attempt <= 5 || attempt % 10 == 0) NotRegistered(log, job.Id, attempt, e.GetType().Name); }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recurring job {JobId} was not registered (attempt {Attempt}): {ErrorType}. The app keeps serving; it is tried again at most a minute later.")]
    static partial void NotRegistered(ILogger logger, string jobId, int attempt, string errorType);
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

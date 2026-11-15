using System.Net;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Medvya.Notifications.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>#1365: a recurring job's database lock held by another copy of the app must not stop or delay the web app's start.</summary>
public sealed class JobRegistrationTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    const string Lock = "hangfire:lock:recurring-job:" + NotificationJobs.JobId;
    const string Registered = $"SELECT count(*) FROM hangfire.set WHERE key = 'recurring-jobs' AND value = '{NotificationJobs.JobId}'";

    async Task<object?> Admin(string statement)
    {
        await using var conn = new NpgsqlConnection(sql.AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(statement, conn);
        return await cmd.ExecuteScalarAsync();
    }

    [Fact]
    public async Task TheAppStartsWhileTheJobLockIsHeldAndRegistersTheJobOnceItIsFree()
    {
        await Admin($"INSERT INTO hangfire.lock (resource, updatecount, acquired) VALUES ('{Lock}', 0, now() AT TIME ZONE 'utc')"); // another copy holds it
        var log = new CapturingLogger<RecurringJobRegistration>();
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Jobs:Enabled", "true").ConfigureServices(s => s.AddSingleton<ILogger<RecurringJobRegistration>>(log)));

        Assert.Equal(HttpStatusCode.OK, (await host.CreateClient().GetAsync(new Uri("/health", UriKind.Relative))).StatusCode);
        var registration = host.Services.GetRequiredService<RecurringJobRegistration>();
        while (log.Entries.Count == 0) await Task.Delay(200); // the first try waits for the lock, then gives way
        Assert.Equal(LogLevel.Warning, log.Entries[0].Level);
        Assert.StartsWith($"Recurring job {NotificationJobs.JobId} was not registered (attempt 1): ", log.Entries[0].Message, StringComparison.Ordinal);
        Assert.Equal(0L, await Admin(Registered));

        await Admin($"DELETE FROM hangfire.lock WHERE resource = '{Lock}'");
        await registration.ExecuteTask!;

        Assert.Equal(1L, await Admin(Registered));
        Assert.DoesNotContain(log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task RegistrationKeepsTryingPastTheOldLimitOfTwentyAndWarnsAtABoundedRate()
    {
        const string job = "registration-retry-check";
        var manager = new RecordingJobManager(); // Hangfire's own manager reads a log provider an earlier test's host has disposed
        var log = new CapturingLogger<RecurringJobRegistration>();
        var tries = 0;
        using var registration = new RecurringJobRegistration(manager, log) { Delay = _ => TimeSpan.Zero };
        registration.Add(job, m =>
        {
            if (++tries <= 25) throw new DistributedLockTimeoutException(Lock); // another copy holds the lock for 25 tries
            m.AddOrUpdate<NotificationJobs>(job, j => j.RunAsync(CancellationToken.None), Cron.Never());
        });

        await registration.StartAsync(CancellationToken.None);
        await registration.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(26, tries);
        Assert.Equal([job], manager.Registered);
        Assert.Equal([1, 2, 3, 4, 5, 10, 20], log.Entries.Select(e => int.Parse(e.Message.Split("(attempt ")[1].Split(')')[0], System.Globalization.CultureInfo.InvariantCulture)));
        Assert.All(log.Entries, e => Assert.Equal(LogLevel.Warning, e.Level));
    }
}

sealed class RecordingJobManager : IRecurringJobManager
{
    public List<string> Registered { get; } = [];
    public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options) => Registered.Add(recurringJobId);
    public void Trigger(string recurringJobId) => throw new NotSupportedException();
    public void RemoveIfExists(string recurringJobId) => throw new NotSupportedException();
}

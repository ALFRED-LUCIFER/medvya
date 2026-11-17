using Host.Api.Middleware;
using Medvya.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>#912: the startup warm-up compiles every module's queries but reads and writes nothing, and a database outage only logs.</summary>
public sealed class DatabaseWarmupTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    async Task<long> Rows(string table)
    {
        await using var conn = new NpgsqlConnection(sql.AdminConnectionString); // the superuser: row-level security would hide the rows
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table}", conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task TheWarmupRunsEveryModuleContextInTheBackgroundAndWritesNoAuditOrOutboxRows()
    {
        var (audit, outbox) = (await Rows("audit.\"AuditEvents\""), await Rows("scheduling.\"OutboxEvents\""));
        var log = new CapturingLogger<DatabaseWarmup>();
        var unordered = new UnorderedRowLimits();
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureServices(s => s.AddSingleton<ILogger<DatabaseWarmup>>(log).AddSingleton<ILoggerProvider>(unordered)));

        var warmup = host.Services.GetServices<IHostedService>().OfType<DatabaseWarmup>().Single();
        await warmup.Running!;

        var (level, message) = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.StartsWith("Database warm-up finished: 10 contexts", message, StringComparison.Ordinal); // Billing joined the host (MDV-P4-002)
        Assert.Equal(0, unordered.Count); // #1365: start-up logged one such warning per table
        Assert.Equal((audit, outbox), (await Rows("audit.\"AuditEvents\""), await Rows("scheduling.\"OutboxEvents\"")));
    }

    /// <summary>Counts EF Core's "row limiting operator without OrderBy" warnings from every logger of the host.</summary>
    sealed class UnorderedRowLimits : ILoggerProvider, ILogger
    {
        int count;
        public int Count => count;
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId == Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.RowLimitingOperationWithoutOrderByWarning) Interlocked.Increment(ref count);
        }
        public void Dispose() { }
    }

    [Fact]
    public async Task ADatabaseThatIsDownIsLoggedByErrorTypeAndNeverThrown()
    {
        const string down = "Host=127.0.0.1;Port=1;Timeout=2;Username=medvya_app;Password=Not_a_secret_1";
        await using var services = new ServiceCollection().AddDbContext<IdentityDbContext>(o => o.UseNpgsql(down)).BuildServiceProvider();
        var log = new CapturingLogger<DatabaseWarmup>();
        var warmup = new DatabaseWarmup(services, [typeof(IdentityDbContext)], log);

        await warmup.StartAsync(CancellationToken.None); // returns before the warm-up finishes, so startup never waits for the database
        await warmup.Running!;

        var (level, message) = Assert.Single(log.Entries);
        Assert.Equal((LogLevel.Warning, "Database warm-up failed: NpgsqlException. The first requests after this start will be slow."), (level, message));
    }
}

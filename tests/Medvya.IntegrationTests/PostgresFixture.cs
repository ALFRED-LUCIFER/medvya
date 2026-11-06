using Medvya.Audit.Infrastructure.Persistence;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Patients.Infrastructure.Persistence;
using Medvya.Notifications.Infrastructure.Persistence;
using Medvya.Scheduling.Infrastructure.Persistence;
using Medvya.Identity.Infrastructure.Persistence;
using Medvya.Tenancy.Infrastructure.Persistence;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace Medvya.IntegrationTests;

/// <summary>
/// One real PostgreSQL 16 per test class, migrated once with the baseline migrations. Every context connects as the container
/// superuser until the per-module roles arrive (MDV-P0-034). Synthetic data only.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16").WithDatabase("medvya")
        .WithCommand("-c", "max_connections=600") // the pooled app connections plus unpooled OpenAs ones; the load tests raise the pool to 400
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public sealed class FixedTenant(Guid? id) : ICurrentTenant { public Guid? TenantId => id; }

    DbContextOptionsBuilder<T> Options<T>(Guid? tenantId) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseNpgsql(ConnectionString).AddInterceptors(new TenantSessionInterceptor(new FixedTenant(tenantId)));

    public TenancyDbContext Context(Guid? tenantId) => new(Options<TenancyDbContext>(tenantId).Options, new FixedTenant(tenantId));

    public AuditDbContext Audit(Guid? tenantId) => new(Options<AuditDbContext>(tenantId).Options, new FixedTenant(tenantId));

    public PatientsDbContext Patients(Guid? tenantId, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] more) =>
        new(Options<PatientsDbContext>(tenantId).AddInterceptors(more).Options, new FixedTenant(tenantId));

    /// <summary>shortcut: no app role yet (MDV-P0-034), so this is the plain tenant context.</summary>
    public Task<PatientsDbContext> PatientsAs(string user, Guid tenantId)
    {
        ArgumentException.ThrowIfNullOrEmpty(user);
        return Task.FromResult(Patients(tenantId));
    }

    public BillingDbContext Billing(Guid? tenantId) => new(Options<BillingDbContext>(tenantId).Options, new FixedTenant(tenantId));

    public SchedulingDbContext Scheduling(Guid? tenantId) => new(Options<SchedulingDbContext>(tenantId).Options, new FixedTenant(tenantId));

    public NotificationsDbContext Notifications(Guid? tenantId) => new(Options<NotificationsDbContext>(tenantId).Options, new FixedTenant(tenantId));

    public IdentityDbContext Identity() => new(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>
    /// Opens an unpooled connection (its session settings die with it) in a tenant and/or for a signed-in Medvya user (medvya.user_id,
    /// as MembershipDirectory sets it).
    /// shortcut: <paramref name="user"/> names a role of the old SQL Server role set and is not impersonated; MDV-P0-034 adds the
    /// Postgres roles and switches to SET ROLE here.
    /// </summary>
    public async Task<NpgsqlConnection> OpenAs(string user, Guid? tenantId = null, Guid? userId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(user);
        var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString);
        await conn.OpenAsync();
        if (tenantId is { } t) await Set(conn, "medvya.tenant_id", t);
        if (userId is { } u) await Set(conn, "medvya.user_id", u);
        return conn;
    }

    /// <summary>Bulk-loads <paramref name="table"/> (columns named as in the database) into "schema.Table" with binary COPY, then refreshes planner statistics.</summary>
    public static async Task BulkCopyAsync(NpgsqlConnection conn, string destination, DataTable table)
    {
        var (schema, name) = (destination.Split('.')[0], destination.Split('.')[1]);
        var columns = string.Join(", ", table.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName}\""));
        await using (var writer = await conn.BeginBinaryImportAsync($"COPY {schema}.\"{name}\" ({columns}) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (DataRow row in table.Rows)
            {
                await writer.StartRowAsync();
                foreach (var value in row.ItemArray)
                    switch (value)
                    {
                        case DateTime d: await writer.WriteAsync(DateOnly.FromDateTime(d), NpgsqlDbType.Date); break; // only date-of-birth columns are DateTime
                        case DateTimeOffset o: await writer.WriteAsync(o.ToUniversalTime(), NpgsqlDbType.TimestampTz); break;
                        default: await writer.WriteAsync(value); break;
                    }
            }
            await writer.CompleteAsync();
        }
        await using var analyze = new NpgsqlCommand($"ANALYZE {schema}.\"{name}\"", conn);
        await analyze.ExecuteNonQueryAsync();
    }

    static async Task Set(NpgsqlConnection conn, string name, Guid value)
    {
        await using var cmd = new NpgsqlCommand("SELECT set_config(@name, @value, false)", conn);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("value", value.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await Medvya.DbMigrate.DatabaseMigrator.ApplyAsync(conn);
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

using System.Data.Common;
using System.Reflection;
using Hangfire.SqlServer;
using Medvya.Audit.Infrastructure.Persistence;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Infrastructure.Persistence;
using Medvya.Leads.Infrastructure.Persistence;
using Medvya.Notifications.Infrastructure.Persistence;
using Medvya.Patients.Infrastructure.Persistence;
using Medvya.Scheduling.Infrastructure.Persistence;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Medvya.DbMigrate;

/// <summary>
/// Every database change a deploy makes. The integration fixture calls <see cref="ApplyAsync"/> as the migration role, so a
/// migration needing more than that role fails the suite before it can fail a deploy.
/// </summary>
public static class DatabaseMigrator
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    static readonly NoTenant None = new();

    /// <summary>Migrations of every module (forward-only, idempotent), then the Hangfire tables. Run as the migration principal.</summary>
    public static async Task ApplyAsync(DbConnection connection, CancellationToken ct = default)
    {
        // New module = new line here, in dependency order (the Patients encrypted columns need the keys from EnsureRolesAndKeysAsync).
        await Migrate(new TenancyDbContext(Options<TenancyDbContext>(connection), None), ct);
        await Migrate(new AuditDbContext(Options<AuditDbContext>(connection), None), ct);
        await Migrate(new PatientsDbContext(Options<PatientsDbContext>(connection), None), ct);
        await Migrate(new IdentityDbContext(Options<IdentityDbContext>(connection)), ct);
        await Migrate(new BillingDbContext(Options<BillingDbContext>(connection), None), ct);
        await Migrate(new SchedulingDbContext(Options<SchedulingDbContext>(connection), None), ct);
        await Migrate(new NotificationsDbContext(Options<NotificationsDbContext>(connection), None), ct);
        await Migrate(new LeadsDbContext(Options<LeadsDbContext>(connection)), ct);

        // The app may not create its job tables (Jobs.cs), so a deploy does it here. Idempotent: Hangfire versions its own schema.
        SqlServerObjectsInstaller.Install(connection, "hangfire");
    }

    /// <summary>roles.sql as shipped in infra/sql: the single source of truth for who may do what. Idempotent. Administrator only.</summary>
    public static async Task EnsureRolesAsync(SqlConnection admin, CancellationToken ct = default)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("roles.sql")
            ?? throw new InvalidOperationException("roles.sql is not embedded.");
        using var reader = new StreamReader(stream);
        await using var cmd = new SqlCommand(await reader.ReadToEndAsync(ct), admin);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Rows per user table, from partition metadata only: no row is read, so row-level security and Always Encrypted do not matter and no
    /// patient data is exposed. Used by the restore drill (docs/runbooks/restore.md) to compare a restored database with its source.
    /// Ledger history tables are left out (their names carry the object id, which differs after a restore).
    /// </summary>
    public static async Task<IReadOnlyList<(string Table, long Rows)>> RowCountsAsync(SqlConnection connection, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("""
            SELECT s.name + N'.' + t.name, SUM(p.row_count)
            FROM sys.dm_db_partition_stats p
            JOIN sys.tables t ON t.object_id = p.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE p.index_id IN (0, 1) AND t.is_ms_shipped = 0 AND t.name NOT LIKE N'MSSQL[_]LedgerHistoryFor[_]%'
            GROUP BY s.name, t.name
            ORDER BY 1
            """, connection);
        var rows = new List<(string, long)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add((reader.GetString(0), reader.GetInt64(1)));
        return rows;
    }

    static async Task Migrate(DbContext db, CancellationToken ct)
    {
        await using (db)
        {
            // Names only (never data): a failed deploy must say which module and migration it stopped at.
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            await Console.Out.WriteLineAsync($"{db.GetType().Name}: {pending.Count} pending {string.Join(", ", pending)}");
            try { await db.Database.MigrateAsync(ct); }
            catch (SqlException e)
            {
                await Console.Error.WriteLineAsync($"{db.GetType().Name} failed: SQL error {e.Number} at line {e.LineNumber}: {e.Message}");
                throw;
            }
        }
    }

    static DbContextOptions<T> Options<T>(DbConnection connection) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseSqlServer(connection).Options;
}

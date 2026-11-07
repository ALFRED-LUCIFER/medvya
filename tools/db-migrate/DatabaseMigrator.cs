using Hangfire.PostgreSql;
using Medvya.Audit.Infrastructure.Persistence;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Infrastructure.Persistence;
using Medvya.Leads.Infrastructure.Persistence;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Notifications.Infrastructure.Persistence;
using Medvya.Patients.Infrastructure.Persistence;
using Medvya.Scheduling.Infrastructure.Persistence;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Medvya.DbMigrate;

/// <summary>
/// Every database change a deploy makes. The integration fixture calls <see cref="ApplyAsync"/>.
/// </summary>
public static class DatabaseMigrator
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    static readonly NoTenant None = new();

    /// <summary>
    /// The administrator's step (infra/postgres/roles.sql then table-rights.sql, idempotent): roles, schemas and privileges. Run it as a superuser or role administrator
    /// before <see cref="ApplyAsync"/>. <paramref name="logins"/> turns the named roles into password logins for development and tests;
    /// in Azure an Entra identity is made a login IN ROLE medvya_app / medvya_migrator with <see cref="MapEntraPrincipalAsync"/> instead.
    /// </summary>
    public static async Task ApplyRolesAsync(NpgsqlConnection connection, IEnumerable<(string Role, string Password)>? logins = null, CancellationToken ct = default)
    {
        await RunScriptAsync(connection, "roles.sql", ct);
        // roles.sql re-grants schema-wide; narrow the single tables again, as their owner (on Azure the administrator is not a superuser).
        await using (var role = new NpgsqlCommand("SET ROLE medvya_owner", connection)) await role.ExecuteNonQueryAsync(ct);
        await RunScriptAsync(connection, "table-rights.sql", ct);
        await using (var reset = new NpgsqlCommand("RESET ROLE", connection)) await reset.ExecuteNonQueryAsync(ct);
        foreach (var (role, password) in logins ?? [])
        {
            if (!role.StartsWith("medvya_", StringComparison.Ordinal)) throw new ArgumentException("Only Medvya roles can become logins.", nameof(logins));
            await using var cmd = new NpgsqlCommand("SELECT format('ALTER ROLE %I LOGIN PASSWORD %L', @role::text, @password::text)", connection);
            cmd.Parameters.AddWithValue("role", role);
            cmd.Parameters.AddWithValue("password", password);
            var alter = (string)(await cmd.ExecuteScalarAsync(ct))!;
            await using var run = new NpgsqlCommand(alter, connection);
            await run.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// Azure only (MDV-P0-042): makes an Entra identity (a managed identity) a login IN ROLE <paramref name="role"/>. Run it as an Entra
    /// administrator connected to database postgres (pgaadauth_create_principal_with_oid lives there), after <see cref="ApplyRolesAsync"/>.
    /// Idempotent: an existing login is only granted the role. Only the three login roles are allowed, never the owner or a module role.
    /// </summary>
    public static async Task MapEntraPrincipalAsync(NpgsqlConnection connection, string role, string principal, Guid objectId, CancellationToken ct = default)
    {
        if (role is not ("medvya_app" or "medvya_migrator" or "medvya_reporting")) throw new ArgumentException("Only medvya_app, medvya_migrator or medvya_reporting can be mapped.", nameof(role));
        await using (var exists = new NpgsqlCommand("SELECT 1 FROM pg_roles WHERE rolname = @p", connection))
        {
            exists.Parameters.AddWithValue("p", principal);
            if (await exists.ExecuteScalarAsync(ct) is null)
            {
                await using var create = new NpgsqlCommand("SELECT pgaadauth_create_principal_with_oid(@p, @oid, 'service', false, false)", connection);
                create.Parameters.AddWithValue("p", principal);
                create.Parameters.AddWithValue("oid", objectId.ToString());
                await create.ExecuteNonQueryAsync(ct);
            }
        }
        await using var grant = new NpgsqlCommand("SELECT format('GRANT %I TO %I', @r::text, @p::text)", connection);
        grant.Parameters.AddWithValue("r", role);
        grant.Parameters.AddWithValue("p", principal);
        await using var run = new NpgsqlCommand((string)(await grant.ExecuteScalarAsync(ct))!, connection);
        await run.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Migrations of every module (forward-only, idempotent), the Hangfire tables, then infra/postgres/table-rights.sql. Run as the migration principal.</summary>
    public static async Task ApplyAsync(NpgsqlConnection connection, CancellationToken ct = default)
    {
        // The migrator login holds no data rights; objects must be owned by medvya_owner (roles.sql) so default privileges reach the module roles.
        await using (var role = new NpgsqlCommand("SET ROLE medvya_owner", connection)) await role.ExecuteNonQueryAsync(ct);

        // New module = new line here, in dependency order.
        await Migrate(new TenancyDbContext(Options<TenancyDbContext>(connection), None), ct);
        await Migrate(new AuditDbContext(Options<AuditDbContext>(connection), None), ct);
        await Migrate(new PatientsDbContext(Options<PatientsDbContext>(connection), None, Medvya.Patients.Infrastructure.Persistence.NationalIdColumns.MigrationsOnly), ct);
        await Migrate(new IdentityDbContext(Options<IdentityDbContext>(connection)), ct);
        await Migrate(new BillingDbContext(Options<BillingDbContext>(connection), None), ct);
        await Migrate(new SchedulingDbContext(Options<SchedulingDbContext>(connection), None), ct);
        await Migrate(new NotificationsDbContext(Options<NotificationsDbContext>(connection), None), ct);
        await Migrate(new EncountersDbContext(Options<EncountersDbContext>(connection), None), ct);
        await Migrate(new LeadsDbContext(Options<LeadsDbContext>(connection)), ct);

        // The app may not create its job tables (Jobs.cs), so a deploy does it here. Idempotent: Hangfire versions its own schema.
        PostgreSqlObjectsInstaller.Install(connection, "hangfire");

        // Single-table rights need the tables, so they come last (as the owner, who may grant and revoke on its own tables).
        await RunScriptAsync(connection, "table-rights.sql", ct);
    }

    static async Task RunScriptAsync(NpgsqlConnection connection, string resource, CancellationToken ct)
    {
        await using var stream = typeof(DatabaseMigrator).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        await using var script = new NpgsqlCommand(await reader.ReadToEndAsync(ct), connection);
        await script.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Rows per user table, for the restore drill (docs/runbooks/restore.md): compare a restored database with its source.
    /// Counts only, no row is read. Includes the EF migrations history; tables are listed in a stable order.
    /// shortcut: counts as the connecting role, so once row-level security is forced (MDV-P0-034) run it as a role that sees every row.
    /// </summary>
    public static async Task<IReadOnlyList<(string Table, long Rows)>> RowCountsAsync(NpgsqlConnection connection, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT n.nspname || '.' || c.relname,
                   (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', n.nspname, c.relname), false, true, '')))[1]::text::bigint
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p') AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast')
            ORDER BY (n.nspname || '.' || c.relname) COLLATE "C"
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
            catch (PostgresException e)
            {
                await Console.Error.WriteLineAsync($"{db.GetType().Name} failed: SQL error {e.SqlState} at line {e.Line}: {e.MessageText}");
                throw;
            }
        }
    }

    static DbContextOptions<T> Options<T>(NpgsqlConnection connection) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseNpgsql(connection).Options;
}

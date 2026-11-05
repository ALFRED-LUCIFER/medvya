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

    static async Task Migrate(DbContext db, CancellationToken ct)
    {
        await using (db) await db.Database.MigrateAsync(ct);
    }

    static DbContextOptions<T> Options<T>(DbConnection connection) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseSqlServer(connection).Options;
}

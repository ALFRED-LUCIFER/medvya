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
/// One real PostgreSQL 16 per test class: infra/postgres/roles.sql as the superuser, migrations as medvya_migrator, and every context
/// runs as medvya_app (NOSUPERUSER NOBYPASSRLS, no DDL), so tests meet the real privileges and row-level security. Synthetic data only.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16").WithDatabase("medvya")
        .WithCommand("-c", "max_connections=600") // the pooled app connections plus unpooled OpenAs ones; the load tests raise the pool to 400
        .Build();

    const string LoginPassword = "Fixture_only_1"; // synthetic: a throwaway container, never reachable from outside

    // Every role becomes a login in the fixture, standing in for the Entra identity mapped to it in Azure.
    static readonly string[] Roles = ["medvya_app", "medvya_migrator", "medvya_reporting", "medvya_tenancy", "medvya_audit", "medvya_identity",
        "medvya_patients", "medvya_billing", "medvya_scheduling", "medvya_leads", "medvya_notifications", "medvya_jobs"];

    /// <summary>The container superuser: the administrator's step, bulk loads and the restore-drill counts. Bypasses row-level security.</summary>
    // Command Timeout 300: the 150,000-row audit seed runs the per-row hash-chain trigger and took longer than Npgsql's 30 s default on a shared CI runner.
    public string AdminConnectionString => new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { CommandTimeout = 300 }.ConnectionString;

    /// <summary>The running application: login medvya_app.</summary>
    public string ConnectionString => As("medvya_app");

    string As(string role) => new NpgsqlConnectionStringBuilder(AdminConnectionString) { Username = role, Password = LoginPassword }.ConnectionString;

    /// <summary>"app_user" is medvya_app, "migrator_user" medvya_migrator, "x_user" medvya_x, and "dbo" the superuser.</summary>
    string RoleConnection(string user) => user switch
    {
        "dbo" => AdminConnectionString,
        "app_user" => As("medvya_app"),
        "migrator_user" => As("medvya_migrator"),
        _ when user.EndsWith("_user", StringComparison.Ordinal) => As("medvya_" + user[..^"_user".Length]),
        _ => throw new ArgumentException($"Unknown test user {user}", nameof(user)),
    };

    public sealed class FixedTenant(Guid? id) : ICurrentTenant { public Guid? TenantId => id; }

    DbContextOptionsBuilder<T> Options<T>(Guid? tenantId) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseNpgsql(ConnectionString).AddInterceptors(new TenantSessionInterceptor(new FixedTenant(tenantId)));

    public TenancyDbContext Context(Guid? tenantId) => new(Options<TenancyDbContext>(tenantId).Options, new FixedTenant(tenantId));

    public AuditDbContext Audit(Guid? tenantId) => new(Options<AuditDbContext>(tenantId).Options, new FixedTenant(tenantId));

    /// <summary>National-ID encryption exactly as the Development API sets it up (this machine's key, data keys in patients."DataKeys").</summary>
    public NationalIdColumns NationalIds => nationalIds ??= Medvya.Patients.Infrastructure.PatientsModule.NationalIdColumnsFor(ConnectionString, "Development");
    NationalIdColumns? nationalIds;

    public PatientsDbContext Patients(Guid? tenantId, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] more) =>
        new(Options<PatientsDbContext>(tenantId).AddInterceptors(more).Options, new FixedTenant(tenantId), NationalIds);

    /// <summary>The Patients context as the application role (every context is); kept for the tests that name the role.</summary>
    public Task<PatientsDbContext> PatientsAs(string user, Guid tenantId) =>
        user == "app_user" ? Task.FromResult(Patients(tenantId)) : throw new ArgumentException($"Only app_user, not {user}", nameof(user));

    public BillingDbContext Billing(Guid? tenantId) => new(Options<BillingDbContext>(tenantId).Options, new FixedTenant(tenantId));

    /// <summary>Billing as the superuser, for seeds that must do what the app role may not (bypasses row-level security too).</summary>
    public BillingDbContext BillingAsAdmin(Guid tenantId) =>
        new(new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(AdminConnectionString).Options, new FixedTenant(tenantId));

    public SchedulingDbContext Scheduling(Guid? tenantId) => new(Options<SchedulingDbContext>(tenantId).Options, new FixedTenant(tenantId));

    public NotificationsDbContext Notifications(Guid? tenantId) => new(Options<NotificationsDbContext>(tenantId).Options, new FixedTenant(tenantId));

    public IdentityDbContext Identity() => new(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>
    /// Opens an unpooled connection (its session settings die with it) in a tenant and/or for a signed-in Medvya user (medvya.user_id,
    /// as MembershipDirectory sets it).
    /// </summary>
    public async Task<NpgsqlConnection> OpenAs(string user, Guid? tenantId = null, Guid? userId = null)
    {
        var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(RoleConnection(user)) { Pooling = false }.ConnectionString);
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
        await using (var admin = new NpgsqlConnection(AdminConnectionString))
        {
            await admin.OpenAsync();
            await Medvya.DbMigrate.DatabaseMigrator.ApplyRolesAsync(admin, Roles.Select(r => (r, LoginPassword)));
        }
        await using (var conn = new NpgsqlConnection(As("medvya_migrator")))
        {
            await conn.OpenAsync();
            await Medvya.DbMigrate.DatabaseMigrator.ApplyAsync(conn);
        }
        // The next deploy's administrator step, on a database that now has every table: its schema-wide grants must not undo the
        // single-table rights (MDV-P0-041), so tests see the rights a long-lived database has, not only a fresh one.
        await using var again = new NpgsqlConnection(AdminConnectionString);
        await again.OpenAsync();
        await Medvya.DbMigrate.DatabaseMigrator.ApplyRolesAsync(again);
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

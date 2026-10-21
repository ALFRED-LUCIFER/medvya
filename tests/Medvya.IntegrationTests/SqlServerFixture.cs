using Medvya.Audit.Infrastructure.Persistence;
using Medvya.Billing.Infrastructure.Persistence;
using System.Security.Cryptography;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Patients.Infrastructure;
using Medvya.Patients.Infrastructure.Persistence;
using Medvya.Identity.Infrastructure.Persistence;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Medvya.IntegrationTests;

/// <summary>
/// One real SQL Server 2022 per test class: roles from infra/sql/roles.sql, then migrated once as the migration role.
/// Context/Audit connect as the container administrator; OpenAs impersonates a least-privilege role user. Synthetic data only.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    // amd64-only image; on Apple silicon it runs under Rosetta (Colima --vz-rosetta).
    readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithCreateParameterModifier(p => p.Platform = "linux/amd64")
        .Build();

    // A real application database: the container default (master) is a system database, where ledger tables are refused.
    public string ConnectionString => new SqlConnectionStringBuilder(container.GetConnectionString()) { InitialCatalog = "medvya" }.ConnectionString;

    /// <summary>Test column master key: one RSA key per test process (SqlClient registers key store providers once per process).</summary>
    public static readonly LocalKeyStoreProvider Keys = LocalKeyStoreProvider.Register(() => RSA.Create(2048));

    public sealed class FixedTenant(Guid? id) : ICurrentTenant { public Guid? TenantId => id; }

    public TenancyDbContext Context(Guid? tenantId)
    {
        var tenant = new FixedTenant(tenantId);
        return new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new TenantSessionInterceptor(tenant))
            .Options, tenant);
    }

    public AuditDbContext Audit(Guid? tenantId)
    {
        var tenant = new FixedTenant(tenantId);
        return new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new TenantSessionInterceptor(tenant))
            .Options, tenant);
    }

    /// <summary>Patients context as administrator, with Always Encrypted on (the test CMK is registered).</summary>
    public PatientsDbContext Patients(Guid? tenantId)
    {
        var tenant = new FixedTenant(tenantId);
        return new PatientsDbContext(new DbContextOptionsBuilder<PatientsDbContext>()
            .UseSqlServer(PatientsModule.WithColumnEncryption(ConnectionString))
            .AddInterceptors(new TenantSessionInterceptor(tenant))
            .Options, tenant);
    }

    /// <summary>Patients context running as a role user (see <see cref="OpenAs"/>), with Always Encrypted on.</summary>
    public async Task<PatientsDbContext> PatientsAs(string user, Guid tenantId)
    {
        var conn = await OpenAs(user, tenantId, columnEncryption: true);
        return new PatientsDbContext(new DbContextOptionsBuilder<PatientsDbContext>().UseSqlServer(conn, contextOwnsConnection: true).Options, new FixedTenant(tenantId));
    }

    public BillingDbContext Billing(Guid? tenantId)
    {
        var tenant = new FixedTenant(tenantId);
        return new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new TenantSessionInterceptor(tenant))
            .Options, tenant);
    }

    /// <summary>Database users without logins (no passwords), one per role in infra/sql/roles.sql; tests impersonate them.</summary>
    public static readonly IReadOnlyDictionary<string, string> RoleUsers = new Dictionary<string, string>
    {
        ["migrator_user"] = "medvya_migrator", ["app_user"] = "medvya_app", ["reporting_user"] = "medvya_reporting",
        ["tenancy_user"] = "medvya_tenancy", ["audit_user"] = "medvya_audit", ["identity_user"] = "medvya_identity",
        ["patients_user"] = "medvya_patients", ["billing_user"] = "medvya_billing",
    };

    /// <summary>Opens a connection running as <paramref name="user"/> (EXECUTE AS USER), optionally in a tenant. Unpooled, so the impersonation dies with it.</summary>
    public async Task<SqlConnection> OpenAs(string user, Guid? tenantId = null, bool columnEncryption = false)
    {
        var builder = new SqlConnectionStringBuilder(ConnectionString) { Pooling = false };
        if (columnEncryption) builder.ColumnEncryptionSetting = SqlConnectionColumnEncryptionSetting.Enabled;
        var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("EXECUTE AS USER = @user; IF @tenant IS NOT NULL EXEC sys.sp_set_session_context N'tenant_id', @tenant, @read_only = 1;", conn);
        cmd.Parameters.AddWithValue("@user", user);
        cmd.Parameters.AddWithValue("@tenant", (object?)tenantId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return conn;
    }

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using (var master = new SqlConnection(container.GetConnectionString()))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand("CREATE DATABASE medvya", master);
            await create.ExecuteNonQueryAsync();
        }

        // As administrator: roles (twice, proving the script is re-runnable), then one user per role.
        await using (var admin = new SqlConnection(ConnectionString))
        {
            await admin.OpenAsync();
            var roles = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "roles.sql"));
            for (var i = 0; i < 2; i++)
            {
                await using var cmd = new SqlCommand(roles, admin);
                await cmd.ExecuteNonQueryAsync();
            }
            foreach (var (user, role) in RoleUsers)
            {
                // Identifiers come from the constant table above, never from input.
                await using var cmd = new SqlCommand($"CREATE USER [{user}] WITHOUT LOGIN; ALTER ROLE [{role}] ADD MEMBER [{user}];", admin);
                await cmd.ExecuteNonQueryAsync();
            }
            // Always Encrypted keys are the administrator's step, between roles.sql and migrations (as in Azure).
            await ColumnEncryption.EnsureKeysAsync(admin, LocalKeyStoreProvider.ProviderName, LocalKeyStoreProvider.KeyPath, Keys);
        }

        // Every migration runs as the migration role, so a migration needing more than that role grants fails here.
        await using var migrator = await OpenAs("migrator_user");
        await using var db = new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>().UseSqlServer(migrator).Options, new FixedTenant(null));
        await db.Database.MigrateAsync();
        await using var audit = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseSqlServer(migrator).Options, new FixedTenant(null));
        await audit.Database.MigrateAsync();
        await using var patients = new PatientsDbContext(new DbContextOptionsBuilder<PatientsDbContext>().UseSqlServer(migrator).Options, new FixedTenant(null));
        await patients.Database.MigrateAsync();
        await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(migrator).Options);
        await identity.Database.MigrateAsync();
        await using var billing = new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(migrator).Options, new FixedTenant(null));
        await billing.Database.MigrateAsync();
    }

    public IdentityDbContext Identity() => new(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(ConnectionString).Options);

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

using Medvya.Audit.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
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

    /// <summary>Database users without logins (no passwords), one per role in infra/sql/roles.sql; tests impersonate them.</summary>
    public static readonly IReadOnlyDictionary<string, string> RoleUsers = new Dictionary<string, string>
    {
        ["migrator_user"] = "medvya_migrator", ["app_user"] = "medvya_app", ["reporting_user"] = "medvya_reporting",
        ["tenancy_user"] = "medvya_tenancy", ["audit_user"] = "medvya_audit",
    };

    /// <summary>Opens a connection running as <paramref name="user"/> (EXECUTE AS USER), optionally in a tenant. Unpooled, so the impersonation dies with it.</summary>
    public async Task<SqlConnection> OpenAs(string user, Guid? tenantId = null)
    {
        var conn = new SqlConnection(new SqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString);
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
        }

        // Every migration runs as the migration role, so a migration needing more than that role grants fails here.
        await using var migrator = await OpenAs("migrator_user");
        await using var db = new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>().UseSqlServer(migrator).Options, new FixedTenant(null));
        await db.Database.MigrateAsync();
        await using var audit = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseSqlServer(migrator).Options, new FixedTenant(null));
        await audit.Database.MigrateAsync();
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

using Medvya.Audit.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Medvya.IntegrationTests;

/// <summary>One real SQL Server 2022 per test class, migrated once. Synthetic data only.</summary>
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

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using var db = Context(null);
        await db.Database.MigrateAsync();
        await using var audit = Audit(null);
        await audit.Database.MigrateAsync();
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

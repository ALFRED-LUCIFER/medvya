using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
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

    public string ConnectionString => container.GetConnectionString();

    public sealed class FixedTenant(Guid? id) : ICurrentTenant { public Guid? TenantId => id; }

    public TenancyDbContext Context(Guid? tenantId)
    {
        var tenant = new FixedTenant(tenantId);
        return new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new TenantSessionInterceptor(tenant))
            .Options, tenant);
    }

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using var db = Context(null);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}

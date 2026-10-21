using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Medvya.Tenancy.Infrastructure.Persistence;

/// <summary>Only for `dotnet ef migrations add`; never connects.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<TenancyDbContext>
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    public TenancyDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TenancyDbContext>().UseSqlServer("Server=design-time-only").Options, new NoTenant());
}

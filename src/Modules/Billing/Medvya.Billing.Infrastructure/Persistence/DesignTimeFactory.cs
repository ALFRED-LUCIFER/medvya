using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Medvya.Billing.Infrastructure.Persistence;

/// <summary>Only for `dotnet ef migrations add`; never connects.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    public BillingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer("Server=design-time-only").Options, new NoTenant());
}

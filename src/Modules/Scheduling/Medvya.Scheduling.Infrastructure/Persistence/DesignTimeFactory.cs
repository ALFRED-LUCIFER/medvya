using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Medvya.Scheduling.Infrastructure.Persistence;

/// <summary>Only for `dotnet ef migrations add`; never connects.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<SchedulingDbContext>
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    public SchedulingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SchedulingDbContext>().UseSqlServer("Server=design-time-only").Options, new NoTenant());
}

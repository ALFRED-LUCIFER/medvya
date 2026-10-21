using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Medvya.Audit.Infrastructure.Persistence;

/// <summary>Only for `dotnet ef migrations add`; never connects.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    public AuditDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AuditDbContext>().UseSqlServer("Server=design-time-only").Options, new NoTenant());
}

using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Medvya.Patients.Infrastructure.Persistence;

/// <summary>Only for `dotnet ef migrations add`; never connects.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<PatientsDbContext>
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    public PatientsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PatientsDbContext>().UseSqlServer("Server=design-time-only").Options, new NoTenant());
}

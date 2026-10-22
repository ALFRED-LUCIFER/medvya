using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Medvya.Leads.Infrastructure.Persistence;

/// <summary>Only for `dotnet ef migrations add`; never connects.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<LeadsDbContext>
{
    public LeadsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LeadsDbContext>().UseSqlServer("Server=design-time-only").Options);
}

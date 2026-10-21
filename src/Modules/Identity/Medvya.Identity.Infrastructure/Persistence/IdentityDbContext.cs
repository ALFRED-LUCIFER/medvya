using Medvya.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Identity.Infrastructure.Persistence;

/// <summary>Users are global, not per tenant: one person can work in several clinics. Clinic data stays behind memberships (Tenancy).</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<StaffUser> Users => Set<StaffUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.Entity<StaffUser>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.Issuer).HasMaxLength(256);
            e.Property(x => x.Subject).HasMaxLength(256);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        });
    }
}

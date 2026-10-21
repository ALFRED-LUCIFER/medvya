using Medvya.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Identity.Infrastructure.Persistence;

/// <summary>Users are global, not per tenant: one person can work in several clinics. Clinic data stays behind memberships (Tenancy).</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<StaffUser> Users => Set<StaffUser>();
    public DbSet<StaffSession> Sessions => Set<StaffSession>();

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
        // Not row-level secured: a session belongs to a person, not a clinic. Every query filters by user (and tenant for admin revoke).
        modelBuilder.Entity<StaffSession>(e =>
        {
            e.ToTable("Sessions", t => t.HasCheckConstraint("CK_Sessions_IdleTimeoutMinutes", "IdleTimeoutMinutes BETWEEN 1 AND 480"));
            e.Property(x => x.Device).HasMaxLength(64);
            e.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.TenantId, x.UserId });
        });
    }
}

using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure.Persistence;

/// <summary>
/// Tenant isolation layer 1 of 2: EF global query filters on the current tenant.
/// Layer 2 is Azure SQL row-level security on SESSION_CONTEXT (see the InitialTenancy migration), which holds even if a filter is bypassed.
/// </summary>
public sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options, ICurrentTenant tenant) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    Guid? CurrentTenantId => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("tenancy");
        modelBuilder.Entity<Tenant>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.ResidencyRegion).HasMaxLength(40);
            e.HasQueryFilter(x => x.Id == CurrentTenantId);
        });
        modelBuilder.Entity<Facility>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.TimeZone).HasMaxLength(64);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Membership>(e =>
        {
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.FacilityId }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<FeatureFlag>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Owner).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}

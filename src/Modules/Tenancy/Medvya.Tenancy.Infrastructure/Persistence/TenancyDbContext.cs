using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<PracticeProfile> Profiles => Set<PracticeProfile>();
    public DbSet<OperatingHours> OperatingHours => Set<OperatingHours>();
    public DbSet<Closure> Closures => Set<Closure>();
    public DbSet<SettingsChange> SettingsChanges => Set<SettingsChange>();
    public DbSet<PracticeDefaults> PracticeDefaultsSet => Set<PracticeDefaults>();
    public DbSet<DoctorLicence> DoctorLicences => Set<DoctorLicence>();

    Guid? CurrentTenantId => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("tenancy");
        modelBuilder.Entity<Tenant>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            // A tenant is bound to one country data plane for life (ADR-003, MDV-P1-002): changing either after the insert throws.
            // shortcut: enforced by the domain (init-only) and EF; add a column DENY or trigger if a raw-SQL path ever updates tenants.
            e.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            e.Property(x => x.ResidencyRegion).HasMaxLength(40).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            e.Property(x => x.IdleTimeoutMinutes).HasDefaultValue(15);
            e.ToTable(t => t.HasCheckConstraint("CK_Tenants_IdleTimeoutMinutes", "IdleTimeoutMinutes BETWEEN 1 AND 480"));
            e.HasQueryFilter(x => x.Id == CurrentTenantId);
        });
        modelBuilder.Entity<Facility>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.TimeZone).HasMaxLength(64);
            e.Property(x => x.Address).HasMaxLength(500);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Membership>(e =>
        {
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.FacilityId }).IsUnique();
            e.HasIndex(x => x.UserId); // the facility picker looks a user's memberships up across tenants
            // Only an Invited membership may lack its user.
            e.ToTable(t => t.HasCheckConstraint("CK_Memberships_UserUnlessInvited", "[Status] = 'Invited' OR [UserId] IS NOT NULL"));
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
        modelBuilder.Entity<Invitation>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.TokenHash).HasMaxLength(32).IsFixedLength();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<Membership>().WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PracticeProfile>(e =>
        {
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.RegistrationNumber).HasMaxLength(50);
            e.Property(x => x.TaxNumber).HasMaxLength(30);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.LogoKey).HasMaxLength(200);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.TenantId).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<OperatingHours>(e =>
        {
            e.Property(x => x.Weekday).HasConversion<int>();
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.FacilityId, x.Weekday }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_OperatingHours_OpensBeforeCloses", "[Opens] < [Closes]"));
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Closure>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.FacilityId, x.Date }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<SettingsChange>(e =>
        {
            e.Property(x => x.Section).HasMaxLength(40);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.OccurredAt });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PracticeDefaults>(e =>
        {
            e.ToTable("PracticeDefaults");
            e.HasKey(x => x.TenantId);
            e.Property(x => x.Specialty).HasMaxLength(100);
            e.Property(x => x.Qualifications).HasMaxLength(200);
            e.Property(x => x.Languages).HasMaxLength(200);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasOne<Tenant>().WithOne().HasForeignKey<PracticeDefaults>(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<DoctorLicence>(e =>
        {
            e.HasKey(x => x.MembershipId);
            e.Property(x => x.Authority).HasMaxLength(20);
            e.Property(x => x.Number).HasMaxLength(20);
            e.Property(x => x.CertificateName).HasMaxLength(200);
            e.HasOne<Membership>().WithOne().HasForeignKey<DoctorLicence>(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}

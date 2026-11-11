using System.Text.Json;
using Medvya.BuildingBlocks.Domain;
using Medvya.Prescribing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Medvya.Prescribing.Infrastructure.Persistence;

/// <summary>Tenant isolation layer 1 of 2: EF query filters; layer 2 is row-level security (Prescribing migrations).</summary>
public sealed class PrescribingDbContext(DbContextOptions<PrescribingDbContext> options, ICurrentTenant tenant) : DbContext(options)
{
    public DbSet<FormularyItem> FormularyItems => Set<FormularyItem>();
    public DbSet<OrderSet> OrderSets => Set<OrderSet>();
    public DbSet<ClinicalOrder> Orders => Set<ClinicalOrder>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<ReferralEvent> ReferralEvents => Set<ReferralEvent>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
    public DbSet<RxAlertOverride> RxAlertOverrides => Set<RxAlertOverride>();
    public DbSet<AuthorisedPrescription> AuthorisedPrescriptions => Set<AuthorisedPrescription>();
    public DbSet<PrescriptionCancellation> PrescriptionCancellations => Set<PrescriptionCancellation>();
    public DbSet<PrescriptionPdf> PrescriptionPdfs => Set<PrescriptionPdf>();
    public DbSet<RxLayoutApproval> RxLayoutApprovals => Set<RxLayoutApproval>();
    public DbSet<RxShare> RxShares => Set<RxShare>();
    public DbSet<RxShareCode> RxShareCodes => Set<RxShareCode>();
    public DbSet<PharmacyDispatchEvent> PharmacyDispatchEvents => Set<PharmacyDispatchEvent>();

    Guid? CurrentTenantId => tenant.TenantId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        Medvya.BuildingBlocks.Infrastructure.UtcDateTimeOffset.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("prescribing");
        modelBuilder.Entity<FormularyItem>(e =>
        {
            e.Property(x => x.Country).HasMaxLength(2);
            e.Property(x => x.Generic).HasMaxLength(200);
            e.Property(x => x.Strength).HasMaxLength(50);
            e.Property(x => x.Form).HasMaxLength(40);
            e.Property(x => x.NationalCode).HasMaxLength(30);
            e.Property(x => x.RegClass).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Atc).HasMaxLength(7);
            e.Property(x => x.DefaultSig).HasMaxLength(200);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.Country, x.Generic });
            // The same medicine once per clinic (the service also compares ignoring case); the starter list has no tenant, so its rows never collide.
            e.HasIndex(x => new { x.TenantId, x.Country, x.Generic, x.Strength, x.Form }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == null || x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ClinicalOrder>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.Destination).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.EncounterId });
            e.HasIndex(x => new { x.TenantId, x.PatientId }); // the timeline and merges
            e.ToTable(t => t.HasCheckConstraint("CK_Orders_OrderedHasTime", "(\"State\" = 'Ordered') = (\"OrderedAt\" IS NOT NULL)"));
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Referral>(e =>
        {
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Urgency).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Provider).HasMaxLength(200);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Sections).HasMaxLength(60);
            e.HasIndex(x => new { x.TenantId, x.PatientId, x.State }); // Patient 360's open referrals, and merges
            e.ToTable(t => t.HasCheckConstraint("CK_Referrals_SentHasChannel", "(\"SentAt\" IS NULL) = (\"Channel\" IS NULL)"));
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ReferralEvent>(e =>
        {
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.ReferralId, x.At });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PrescriptionItem>(e =>
        {
            e.Property(x => x.Medicine).HasMaxLength(200);
            e.Property(x => x.Strength).HasMaxLength(50);
            e.Property(x => x.Form).HasMaxLength(40);
            e.Property(x => x.Dose).HasMaxLength(9);
            e.Property(x => x.DoseUnit).HasMaxLength(20);
            e.Property(x => x.Route).HasMaxLength(20);
            e.Property(x => x.Frequency).HasMaxLength(20);
            e.Property(x => x.Timing).HasMaxLength(20);
            e.Property(x => x.Instructions).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.EncounterId });
            e.HasIndex(x => new { x.TenantId, x.PatientId }); // merges
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<RxAlertOverride>(e =>
        {
            e.Property(x => x.AlertKey).HasMaxLength(200);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Reason).HasMaxLength(200);
            e.Property(x => x.RuleSetVersion).HasMaxLength(40);
            e.HasIndex(x => new { x.TenantId, x.EncounterId });
            e.HasIndex(x => new { x.TenantId, x.PatientId }); // merges
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        // MDV-P2-017: the three are append-only and hash-chained (LedgerSql, in their migration).
        modelBuilder.Entity<AuthorisedPrescription>(e =>
        {
            e.Property(x => x.Lines).HasColumnType("jsonb").HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), v => JsonSerializer.Deserialize<List<AuthorisedLine>>(v, (JsonSerializerOptions?)null)!);
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.RuleSetVersion).HasMaxLength(40);
            e.Property(x => x.VerifyCode).HasMaxLength(22);
            e.HasIndex(x => new { x.TenantId, x.EncounterId, x.VersionNo }).IsUnique(); // two authorisations at once: the second loses here
            e.HasIndex(x => new { x.TenantId, x.VerifyCode }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PrescriptionCancellation>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(PrescriptionCancellation.MaxReason);
            e.HasIndex(x => new { x.TenantId, x.PrescriptionId }).IsUnique(); // two cancellations at once: the second loses here
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PrescriptionPdf>(e =>
        {
            e.Property(x => x.Paper).HasMaxLength(2);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.BlobKey).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.PrescriptionId });
            e.ToTable(t => t.HasCheckConstraint("CK_PrescriptionPdfs_OneCopy", "(\"Content\" IS NULL) <> (\"BlobKey\" IS NULL)"));
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        // MDV-P2-020: shares and dispatch states are append-only and hash-chained; codes count wrong tries, so they are not.
        modelBuilder.Entity<RxShare>(e =>
        {
            e.Property(x => x.Recipient).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.PharmacyName).HasMaxLength(RxShareRules.MaxPharmacyName);
            e.Property(x => x.PharmacyEmail).HasMaxLength(254);
            e.Property(x => x.LinkHash).HasMaxLength(32);
            e.HasIndex(x => new { x.TenantId, x.LinkHash }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.EncounterId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<RxShareCode>(e =>
        {
            e.Property(x => x.CodeHash).HasMaxLength(32);
            e.HasIndex(x => new { x.TenantId, x.ShareId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PharmacyDispatchEvent>(e =>
        {
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PharmacyRef).HasMaxLength(RxShareRules.MaxPharmacyRef);
            e.HasIndex(x => new { x.TenantId, x.ShareId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<RxLayoutApproval>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.UserId, x.LayoutVersion }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<OrderSet>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}

/// <summary>Only for `dotnet ef migrations add`; never connects.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<PrescribingDbContext>
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    public PrescribingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PrescribingDbContext>().UseNpgsql("Server=design-time-only").Options, new NoTenant());
}

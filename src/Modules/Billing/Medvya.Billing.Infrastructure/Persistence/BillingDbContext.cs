using Medvya.Billing.Domain;
using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Billing.Infrastructure.Persistence;

/// <summary>Tenant isolation layer 1 of 2: EF query filters; layer 2 is row-level security (InitialBilling migration).</summary>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options, ICurrentTenant tenant) : DbContext(options)
{
    public DbSet<DocSequence> DocSequences => Set<DocSequence>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<PriceVersion> PriceVersions => Set<PriceVersion>();
    public DbSet<BillingSettings> Settings => Set<BillingSettings>();

    Guid? CurrentTenantId => tenant.TenantId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        Medvya.BuildingBlocks.Infrastructure.UtcDateTimeOffset.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("billing");
        modelBuilder.Entity<DocSequence>(e =>
        {
            e.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FinancialYear).HasMaxLength(5);
            e.Property(x => x.Prefix).HasMaxLength(16);
            // One row per series; DocumentNumbering's MERGE relies on this key.
            e.HasIndex(x => new { x.TenantId, x.CountryCode, x.DocumentType, x.FinancialYear, x.Prefix }).IsUnique();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_DocSequences_NextNumber", "\"NextNumber\" >= 1");
                // A series only moves forward one number at a time (DocSequencesOnlyMoveForward).
                t.HasTrigger("TR_DocSequences_OnlyMoveForward");
            });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Service>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Service.MaxName);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Code).HasMaxLength(Service.MaxCode);
            e.Property(x => x.Sac).HasMaxLength(6).IsFixedLength();
            e.Property(x => x.TaxTreatment).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.ConsultationKind).HasConversion<string>().HasMaxLength(10);
            e.Property<uint>("xmin").IsRowVersion(); // concurrent edits conflict (409) instead of the later one silently winning (#889)
            // One active service per consultation kind: the doctors' fees are its prices.
            e.HasIndex(x => new { x.TenantId, x.ConsultationKind }).IsUnique().HasFilter("\"ConsultationKind\" IS NOT NULL AND \"ArchivedAt\" IS NULL");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Services_GstRate",
                    $"(\"TaxTreatment\" = 'Taxable' AND \"GstRateBasisPoints\" BETWEEN 0 AND {Service.MaxGstRateBasisPoints}) OR (\"TaxTreatment\" = 'Exempt' AND \"GstRateBasisPoints\" IS NULL)");
                t.HasCheckConstraint("CK_Services_Sac", "\"Sac\" IS NULL OR \"Sac\" ~ '^[0-9]{6}$'");
            });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PriceVersion>(e =>
        {
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasOne<Service>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ServiceId, x.DoctorId, x.EffectiveFrom });
            e.ToTable(t => t.HasCheckConstraint("CK_PriceVersions_Amount", $"\"AmountMinor\" BETWEEN 0 AND {PriceVersion.MaxAmountMinor}"));
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<BillingSettings>(e =>
        {
            e.HasKey(x => x.TenantId);
            e.Property(x => x.InvoicePrefix).HasMaxLength(16);
            e.Property(x => x.ReceiptPrefix).HasMaxLength(16);
            e.Property(x => x.CreditNotePrefix).HasMaxLength(16);
            e.PrimitiveCollection(x => x.PaymentMethods).ElementType().HasConversion<string>();
            e.Property<uint>("xmin").IsRowVersion();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}

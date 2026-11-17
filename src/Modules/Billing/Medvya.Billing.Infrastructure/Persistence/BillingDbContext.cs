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
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ProviderEvent> ProviderEvents => Set<ProviderEvent>();

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
        modelBuilder.Entity<Invoice>(e =>
        {
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Number).HasMaxLength(32);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.DiscountReason).HasMaxLength(Invoice.MaxReason);
            e.Property(x => x.CancelReason).HasMaxLength(Invoice.MaxReason);
            e.Property<uint>("xmin").IsRowVersion(); // two desks editing one draft: the later save is refused (409)
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("\"Number\" IS NOT NULL");
            // One open bill per visit; a cancelled one leaves room for a new one.
            e.HasIndex(x => new { x.TenantId, x.AppointmentId }).IsUnique().HasFilter("\"AppointmentId\" IS NOT NULL AND \"State\" <> 'Cancelled'");
            e.HasIndex(x => new { x.FacilityId, x.CreatedAt });
            e.HasIndex(x => x.PatientId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Invoices_Amounts", "\"SubtotalMinor\" >= 0 AND \"DiscountMinor\" >= 0 AND \"TaxMinor\" >= 0");
                // Never more collected than the bill's total: the database's own guard against over-collection.
                t.HasCheckConstraint("CK_Invoices_Paid", "\"PaidMinor\" >= 0 AND \"PaidMinor\" <= GREATEST(\"TotalMinor\", 0)");
                t.HasCheckConstraint("CK_Invoices_Number", "\"State\" IN ('Draft', 'Cancelled') OR \"Number\" IS NOT NULL");
            });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<InvoiceLine>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(Service.MaxName);
            e.Property(x => x.Sac).HasMaxLength(6).IsFixedLength();
            e.Property(x => x.TaxTreatment).HasConversion<string>().HasMaxLength(10);
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Service>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PriceVersion>().WithMany().HasForeignKey(x => x.PriceVersionId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_InvoiceLines_Quantity", $"\"Quantity\" BETWEEN 1 AND {InvoiceLine.MaxQuantity}"));
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Payment>(e =>
        {
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Reference).HasMaxLength(Payment.MaxReference);
            e.Property(x => x.ProviderPaymentId).HasMaxLength(40);
            e.Property(x => x.ProviderLinkId).HasMaxLength(40);
            e.Property(x => x.LinkUrl).HasMaxLength(200);
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.ProviderLinkId }).IsUnique().HasFilter("\"ProviderLinkId\" IS NOT NULL");
            // At most one payment the patient may still pay per bill (a Razorpay link for the balance).
            e.HasIndex(x => x.InvoiceId).IsUnique().HasFilter("\"State\" = 'Pending'");
            e.ToTable(t => t.HasCheckConstraint("CK_Payments_Amount", "\"AmountMinor\" > 0"));
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ProviderEvent>(e =>
        {
            // Per tenant: another clinic signing an event with the same id (with its own secret) can never mark this clinic's event as handled.
            e.HasKey(x => new { x.TenantId, x.Provider, x.EventId });
            e.Property(x => x.Provider).HasMaxLength(16);
            e.Property(x => x.EventId).HasMaxLength(ProviderEvent.MaxEventId);
            e.Property(x => x.Type).HasMaxLength(ProviderEvent.MaxType);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}

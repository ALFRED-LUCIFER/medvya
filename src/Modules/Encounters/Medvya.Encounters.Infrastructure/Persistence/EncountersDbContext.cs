using System.Text.Json;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Medvya.Encounters.Infrastructure.Persistence;

/// <summary>Tenant isolation layer 1 of 2: EF query filters; layer 2 is row-level security (Encounters migration).</summary>
public sealed class EncountersDbContext(DbContextOptions<EncountersDbContext> options, ICurrentTenant tenant) : DbContext(options)
{
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<Allergy> Allergies => Set<Allergy>();
    public DbSet<Problem> Problems => Set<Problem>();
    public DbSet<MedicationStatement> MedicationStatements => Set<MedicationStatement>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<ClinicalDocument> Documents => Set<ClinicalDocument>();
    public DbSet<Intake> Intakes => Set<Intake>();
    public DbSet<IntakeEscalation> IntakeEscalations => Set<IntakeEscalation>();
    public DbSet<MedicationDecision> MedicationDecisions => Set<MedicationDecision>();
    public DbSet<MedicationReconciliation> MedicationReconciliations => Set<MedicationReconciliation>();
    public DbSet<DoctorNoteTemplate> DoctorNoteTemplates => Set<DoctorNoteTemplate>();
    public DbSet<ConsultNote> ConsultNotes => Set<ConsultNote>();

    Guid? CurrentTenantId => tenant.TenantId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        Medvya.BuildingBlocks.Infrastructure.UtcDateTimeOffset.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("encounters");
        modelBuilder.Entity<Encounter>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AuthorityMode).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.AppointmentId }).IsUnique(); // one encounter per visit, even for two simultaneous starts
            e.HasIndex(x => new { x.TenantId, x.LeadClinicianId, x.Status }); // the doctor's open consultation
            e.HasIndex(x => new { x.TenantId, x.PatientId }); // care relationship and merges
            e.ToTable(t => t.HasCheckConstraint("CK_Encounters_EndedHasTime", "(\"Status\" = 'Ended') = (\"EndedAt\" IS NOT NULL)"));
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        // Chart facts (MDV-P2-004): one table each, by patient. Enums are stored as their names.
        modelBuilder.Entity<Allergy>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Substance).HasMaxLength(200);
            e.Property(x => x.Reaction).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.ToTable(t => t.HasCheckConstraint("CK_Allergies_SubstanceMatchesKind", "(\"Kind\" = 'Substance') = (\"Substance\" IS NOT NULL)"));
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Problem>(e =>
        {
            e.Property(x => x.Verification).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Icd10Code).HasMaxLength(10);
            e.Property(x => x.Description).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<MedicationStatement>(e =>
        {
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Dose).HasMaxLength(100);
            e.Property(x => x.Frequency).HasMaxLength(50);
            e.HasIndex(x => new { x.TenantId, x.AppointmentId });
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<MedicationDecision>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MedicineKey).HasMaxLength(200);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.FromDose).HasMaxLength(100);
            e.Property(x => x.NewDose).HasMaxLength(100);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.EncounterId, x.MedicineKey, x.DecidedAt });
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<MedicationReconciliation>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.EncounterId }).IsUnique(); // one confirmation per consultation, even for two simultaneous confirms
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Observation>(e =>
        {
            e.Property(x => x.Code).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Flag).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.PatientId, x.Code, x.TakenAt });
            e.HasIndex(x => new { x.TenantId, x.AppointmentId, x.Code }); // the visit's readings
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Intake>(e =>
        {
            e.Property(x => x.ChiefComplaint).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.AppointmentId }).IsUnique(); // one intake per visit, even for two simultaneous first saves
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<IntakeEscalation>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.FacilityId, x.EscalatedAt });
            e.HasIndex(x => new { x.TenantId, x.AppointmentId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<DoctorNoteTemplate>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ConsultNote>(e =>
        {
            e.Property(x => x.TemplateKey).HasMaxLength(50);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            // As text through a converter: Npgsql's dynamic JSON (which a Dictionary would need) stays off for the whole data source.
            e.Property(x => x.Sections).HasColumnType("jsonb").HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null)!);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.EncounterId }).IsUnique(); // one draft note per encounter, even for two simultaneous first saves
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ClinicalDocument>(e =>
        {
            e.ToTable("ClinicalDocuments");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ScanStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ContentType).HasMaxLength(50);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.BlobKey).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.PatientId, x.DocumentDate });
            e.HasIndex(x => x.BlobKey).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}

/// <summary>Only for `dotnet ef migrations add`; never connects.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<EncountersDbContext>
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    public EncountersDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<EncountersDbContext>().UseNpgsql("Server=design-time-only").Options, new NoTenant());
}

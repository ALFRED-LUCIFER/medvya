using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Security;
using Medvya.Patients.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Patients.Infrastructure.Persistence;

/// <summary>
/// Tenant isolation layer 1 of 2: EF query filters; layer 2 is row-level security (MDV-P0-034). Identifier values and member numbers are
/// encrypted by <see cref="NationalIdColumns"/> (MDV-P0-035), which every instance must be given.
/// </summary>
public sealed class PatientsDbContext(DbContextOptions<PatientsDbContext> options, ICurrentTenant tenant, NationalIdColumns nationalIds) : DbContext(options)
{
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<PatientIdentifier> Identifiers => Set<PatientIdentifier>();
    public DbSet<PayerCoverage> PayerCoverages => Set<PayerCoverage>();
    public DbSet<MergeRequest> MergeRequests => Set<MergeRequest>();
    public DbSet<PatientRelationship> Relationships => Set<PatientRelationship>();
    public DbSet<ConsentReceipt> ConsentReceipts => Set<ConsentReceipt>();

    public DbSet<DataKey> DataKeys => Set<DataKey>();

    Guid? CurrentTenantId => tenant.TenantId;

    internal const string SearchKeyColumn = "SearchKey";

    /// <summary>The blind-index value to compare with <c>EF.Property&lt;byte[]&gt;(i, NationalIdColumns.LookupIndex)</c> in the current tenant.</summary>
    public byte[] LookupIndexFor(string value) =>
        nationalIds.Index(tenant.TenantId ?? throw new KeyProtectionException(KeyProtectionException.NoTenant), value);

    internal NationalIdColumns NationalIds => nationalIds;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.AddInterceptors(nationalIds, NationalIdColumns.DecryptOnLoad);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        Medvya.BuildingBlocks.Infrastructure.UtcDateTimeOffset.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("patients");
        modelBuilder.Entity<Patient>(e =>
        {
            // PostgreSQL's xmin as a concurrency token (#889): a write carries the version it read (PatientRecord.Version) and is refused if the row moved on.
            e.Property<uint>("xmin").IsRowVersion();
            // Folded, lower-cased name (and Arabic name) for forgiving desk search, kept by the database on every write so no code path can forget it
            // and a bulk load fills it too. Derived from the names and never from an identifier: a national ID is not searchable in plaintext.
            e.Property<string>(SearchKeyColumn).HasComputedColumnSql("patients.search_key(\"FullName\", \"ArabicName\")", stored: true);
            e.Property(x => x.Mrn).HasMaxLength(32);
            e.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.ArabicName).HasMaxLength(200);
            e.Property(x => x.SexAtBirth).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.GenderIdentity).HasMaxLength(50);
            e.Property(x => x.PreferredLanguage).HasMaxLength(35);
            e.Property(x => x.Nationality).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.Mobile).HasMaxLength(20);
            e.Property(x => x.AlternatePhone).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.ComplexProperty(x => x.Address, a =>
            {
                a.Property(x => x.Line1).HasMaxLength(200);
                a.Property(x => x.Line2).HasMaxLength(200);
                a.Property(x => x.City).HasMaxLength(200);
                a.Property(x => x.District).HasMaxLength(200);
                a.Property(x => x.Region).HasMaxLength(200);
                a.Property(x => x.PostalCode).HasMaxLength(10);
                a.Property(x => x.Makani).HasMaxLength(10);
                a.Property(x => x.PoBox).HasMaxLength(10);
            });
            // One MRN sequence per organisation (MDV-P1-072); the indexes below serve the organisation-wide desk queries.
            e.HasIndex(x => new { x.TenantId, x.Mrn }).IsUnique();
            // Desk directory (newest first) and exact phone search (MDV-P1-018).
            e.HasIndex(x => new { x.TenantId, x.RegisteredAt });
            e.HasIndex(x => new { x.TenantId, x.Mobile });
            // Duplicate check: patients with the same date of birth (MDV-P1-016).
            e.HasIndex(x => new { x.TenantId, x.DateOfBirth });
            // Name search scans this narrow covering index instead of the wide table rows (LIKE '%…%' cannot seek).
            e.HasIndex(x => new { x.TenantId, x.FullName })
                .IncludeProperties(x => new { x.ArabicName, x.DateOfBirth, x.DateOfBirthEstimated, x.SexAtBirth, x.Mrn, x.Status, x.CountryCode, x.RegisteredAt });
            e.HasIndex(x => new { x.TenantId, x.MergedIntoPatientId }); // merged records of a survivor (MDV-P1-017)
            e.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsFixedLength();
            e.Property(x => x.IdempotencyRequestHash).HasMaxLength(64).IsFixedLength();
            // One registration per Idempotency-Key and registrar (MDV-P1-047): the last line if two identical requests race.
            e.HasIndex(x => new { x.TenantId, x.RegisteredBy, x.IdempotencyKeyHash }).IsUnique().HasFilter("\"IdempotencyKeyHash\" IS NOT NULL");
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PatientIdentifier>(e =>
        {
            e.Property(x => x.Scheme).HasConversion<string>().HasMaxLength(20);
            // Ciphertext and blind index only (MDV-P0-035); the string properties are filled by NationalIdColumns.
            e.Ignore(x => x.LookupValue);
            e.Ignore(x => x.DisplayValue);
            e.Property<byte[]?>(NationalIdColumns.LookupCipher).HasColumnName("LookupValue");
            e.Property<byte[]?>(NationalIdColumns.DisplayCipher).HasColumnName("DisplayValue");
            e.Property<byte[]?>(NationalIdColumns.LookupIndex);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex("TenantId", "Scheme", NationalIdColumns.LookupIndex);
            e.HasIndex(x => new { x.TenantId, x.MovedFromPatientId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PayerCoverage>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PayerName).HasMaxLength(200);
            e.Property(x => x.Plan).HasMaxLength(100);
            e.Ignore(x => x.MemberNumber);
            e.Property<byte[]?>(NationalIdColumns.MemberNumberCipher).HasColumnName("MemberNumber");
            e.Property(x => x.Network).HasMaxLength(100);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.PatientId).IsUnique(); // one current payer per patient in P1
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<DataKey>(e =>
        {
            e.HasKey(x => new { x.TenantId, x.Purpose });
            e.Property(x => x.Purpose).HasMaxLength(20);
            e.Property(x => x.WrapKeyId).HasMaxLength(300);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PatientRelationship>(e =>
        {
            e.ToTable("Relationships");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Relationship).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ConsentReceipt>(e =>
        {
            // Append-only (tamper evidence returns with MDV-P0-036).
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.PolicyVersion).HasMaxLength(32);
            e.Property(x => x.Jurisdiction).HasMaxLength(8);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.PatientId, x.Type, x.DecidedAt });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<MergeRequest>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.CandidatePatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.PatientId, x.CandidatePatientId }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}

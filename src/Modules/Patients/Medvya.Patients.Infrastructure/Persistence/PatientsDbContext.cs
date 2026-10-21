using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Patients.Infrastructure.Persistence;

/// <summary>
/// Tenant isolation layer 1 of 2: EF query filters; layer 2 is row-level security (InitialPatients migration).
/// Identifier values and member numbers are Always Encrypted and personal columns carry dynamic data masks; both are created
/// by raw SQL in the migration because EF Core cannot express them. Needs a connection with Column Encryption Setting=Enabled
/// (<see cref="PatientsModule.WithColumnEncryption"/>).
/// </summary>
public sealed class PatientsDbContext(DbContextOptions<PatientsDbContext> options, ICurrentTenant tenant) : DbContext(options)
{
    /// <summary>Deterministic Always Encrypted string columns need a BIN2 collation; the model must match so parameters do.</summary>
    public const string Bin2 = "Latin1_General_BIN2";

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<PatientIdentifier> Identifiers => Set<PatientIdentifier>();
    public DbSet<PayerCoverage> PayerCoverages => Set<PayerCoverage>();

    Guid? CurrentTenantId => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("patients");
        modelBuilder.Entity<Patient>(e =>
        {
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
            e.HasIndex(x => new { x.TenantId, x.FacilityId, x.Mrn }).IsUnique();
            // Desk directory (newest first) and exact phone search (MDV-P1-018).
            e.HasIndex(x => new { x.TenantId, x.FacilityId, x.RegisteredAt });
            e.HasIndex(x => new { x.TenantId, x.FacilityId, x.Mobile });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PatientIdentifier>(e =>
        {
            e.Property(x => x.Scheme).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LookupValue).HasMaxLength(64).UseCollation(Bin2);
            e.Property(x => x.DisplayValue).HasMaxLength(64);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.Scheme, x.LookupValue });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PayerCoverage>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PayerName).HasMaxLength(200);
            e.Property(x => x.Plan).HasMaxLength(100);
            e.Property(x => x.MemberNumber).HasMaxLength(50).UseCollation(Bin2);
            e.Property(x => x.Network).HasMaxLength(100);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.PatientId).IsUnique(); // one current payer per patient in P1
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}

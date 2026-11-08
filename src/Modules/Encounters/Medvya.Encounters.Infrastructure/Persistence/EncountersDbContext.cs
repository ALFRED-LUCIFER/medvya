using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Medvya.Encounters.Infrastructure.Persistence;

/// <summary>Tenant isolation layer 1 of 2: EF query filters; layer 2 is row-level security (Encounters migration).</summary>
public sealed class EncountersDbContext(DbContextOptions<EncountersDbContext> options, ICurrentTenant tenant) : DbContext(options)
{
    public DbSet<Encounter> Encounters => Set<Encounter>();

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
    }
}

/// <summary>Only for `dotnet ef migrations add`; never connects.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<EncountersDbContext>
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    public EncountersDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<EncountersDbContext>().UseNpgsql("Server=design-time-only").Options, new NoTenant());
}

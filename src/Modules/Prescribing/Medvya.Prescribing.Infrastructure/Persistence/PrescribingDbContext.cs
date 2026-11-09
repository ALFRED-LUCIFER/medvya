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

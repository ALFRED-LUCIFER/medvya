using Medvya.Billing.Domain;
using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Billing.Infrastructure.Persistence;

/// <summary>Tenant isolation layer 1 of 2: EF query filters; layer 2 is row-level security (InitialBilling migration).</summary>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options, ICurrentTenant tenant) : DbContext(options)
{
    public DbSet<DocSequence> DocSequences => Set<DocSequence>();

    Guid? CurrentTenantId => tenant.TenantId;

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
                t.HasCheckConstraint("CK_DocSequences_NextNumber", "[NextNumber] >= 1");
                // A series only moves forward one number at a time (DocSequencesOnlyMoveForward); EF must not use a bare OUTPUT clause.
                t.HasTrigger("TR_DocSequences_OnlyMoveForward");
            });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}

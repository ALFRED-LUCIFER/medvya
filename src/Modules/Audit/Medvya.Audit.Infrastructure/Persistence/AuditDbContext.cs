using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Audit.Infrastructure.Persistence;

/// <summary>
/// audit.AuditEvents is an append-only SQL ledger table (created by the InitialAudit migration): the database rejects
/// UPDATE and DELETE for everyone and keeps cryptographic digests. Row-level security scopes reads to the current tenant.
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, ICurrentTenant tenant) : DbContext(options)
{
    public DbSet<AuditEvent> Events => Set<AuditEvent>();

    Guid? CurrentTenantId => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("audit");
        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.ToTable("AuditEvents");
            e.Property(x => x.Action).HasMaxLength(64);
            e.Property(x => x.ResourceType).HasMaxLength(64);
            e.Property(x => x.Purpose).HasMaxLength(32);
            e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.ClientIp).HasMaxLength(45);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.PatientId, x.OccurredAt });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}

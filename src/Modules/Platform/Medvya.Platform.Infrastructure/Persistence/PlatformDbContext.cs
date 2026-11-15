using Medvya.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Medvya.Platform.Infrastructure.Persistence;

/// <summary>
/// The platform console's control-plane tables (MDV-P5-040): the Medvya team, their security keys, key links, sessions, the
/// platform audit log and the subscription plans (MDV-P5-042). No tenant data and no row-level security: none of it belongs to a practice. The console's database login holds
/// only the medvya_platform role, which has no right on any clinic schema (infra/postgres/roles.sql).
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<PlatformMember> Members => Set<PlatformMember>();
    public DbSet<SecurityKey> Keys => Set<SecurityKey>();
    public DbSet<KeyLink> Links => Set<KeyLink>();
    public DbSet<PlatformSession> Sessions => Set<PlatformSession>();
    public DbSet<PlatformAuditEntry> AuditEntries => Set<PlatformAuditEntry>();
    public DbSet<PlanVersion> PlanVersions => Set<PlanVersion>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        Medvya.BuildingBlocks.Infrastructure.UtcDateTimeOffset.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("platform");
        modelBuilder.Entity<PlatformMember>(e =>
        {
            e.ToTable("Members");
            e.Property(x => x.DisplayName).HasMaxLength(PlatformMember.NameMax);
            e.Property(x => x.Email).HasMaxLength(PlatformMember.EmailMax);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Issuer).HasMaxLength(256);
            e.Property(x => x.Subject).HasMaxLength(256);
            // One company account is one current member; unbound invitations and removed members do not collide.
            e.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique().HasFilter("\"Subject\" IS NOT NULL AND \"RemovedAt\" IS NULL");
        });
        modelBuilder.Entity<SecurityKey>(e =>
        {
            e.ToTable("SecurityKeys");
            e.Property(x => x.CredentialId).HasMaxLength(1023);
            e.HasIndex(x => x.CredentialId).IsUnique();
            e.HasIndex(x => x.MemberId);
            e.HasOne<PlatformMember>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<KeyLink>(e =>
        {
            e.ToTable("KeyLinks");
            e.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.TokenHash).HasMaxLength(32);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<PlatformMember>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PlatformSession>(e =>
        {
            e.ToTable("Sessions");
            e.Property(x => x.PendingCeremony).HasMaxLength(8000);
            e.HasIndex(x => x.MemberId);
            e.HasOne<PlatformMember>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PlatformAuditEntry>(e =>
        {
            e.ToTable("AuditEntries");
            e.Property(x => x.Action).HasMaxLength(64);
            e.Property(x => x.ResourceType).HasMaxLength(64);
            e.Property(x => x.Reason).HasMaxLength(PlatformAuditEntry.ReasonMax);
            e.Property(x => x.ClientIp).HasMaxLength(45);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.HasIndex(x => x.OccurredAt);
        });
        modelBuilder.Entity<PlanVersion>(e =>
        {
            e.ToTable("PlanVersions");
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Reason).HasMaxLength(PlatformAuditEntry.ReasonMax);
            e.HasIndex(x => new { x.Code, x.Version }).IsUnique();
            e.HasOne<PlatformMember>().WithMany().HasForeignKey(x => x.CreatedByMemberId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Prices).WithOne().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PlanPrice>(e =>
        {
            e.ToTable("PlanPrices");
            e.HasKey(x => new { x.PlanVersionId, x.Country });
            e.Property(x => x.Country).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        });
    }
}

/// <summary>Only for `dotnet ef migrations add`; never connects.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Server=design-time-only").Options);
}

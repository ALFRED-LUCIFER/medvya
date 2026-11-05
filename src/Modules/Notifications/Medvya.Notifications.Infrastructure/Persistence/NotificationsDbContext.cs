using Medvya.BuildingBlocks.Domain;
using Medvya.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Medvya.Notifications.Infrastructure.Persistence;

/// <summary>Tenant isolation layer 1 of 2: EF query filters; layer 2 is row-level security (InitialNotifications migration).</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, ICurrentTenant tenant) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationAttempt> Attempts => Set<NotificationAttempt>();

    Guid? CurrentTenantId => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");
        modelBuilder.Entity<Notification>(e =>
        {
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ProviderRef).HasMaxLength(80);
            e.Property(x => x.FailureCode).HasMaxLength(60);
            // The same outbox event never makes the same message twice.
            e.HasIndex(x => new { x.TenantId, x.SourceEventId, x.Kind, x.Channel }).IsUnique();
            e.HasIndex(x => new { x.Status, x.SendAt }); // the sender's "what is due" scan
            e.HasIndex(x => new { x.TenantId, x.AppointmentId });
            e.HasIndex(x => new { x.TenantId, x.FacilityId, x.CreatedAt });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<NotificationAttempt>(e =>
        {
            e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Code).HasMaxLength(60);
            e.HasIndex(x => x.NotificationId);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}

/// <summary>Used by <c>dotnet ef</c> only.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    public NotificationsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>().UseSqlServer("Server=design-time-only").Options, new NoTenant());
}

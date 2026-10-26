using Medvya.BuildingBlocks.Domain;
using Medvya.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Scheduling.Infrastructure.Persistence;

/// <summary>
/// Tenant isolation layer 1 of 2: EF query filters; layer 2 is row-level security (InitialScheduling migration).
/// A template's sessions, breaks and visit lengths are JSON columns: they are only ever read and replaced as a whole.
/// </summary>
public sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options, ICurrentTenant tenant) : DbContext(options)
{
    public DbSet<ScheduleResource> Resources => Set<ScheduleResource>();
    public DbSet<ScheduleTemplate> Templates => Set<ScheduleTemplate>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<SlotHold> SlotHolds => Set<SlotHold>();
    public DbSet<ScheduleSlot> ScheduleSlots => Set<ScheduleSlot>();
    public DbSet<VisitStatusEvent> StatusEvents => Set<VisitStatusEvent>();
    public DbSet<AppointmentReschedule> Reschedules => Set<AppointmentReschedule>();
    public DbSet<OutboxEvent> Outbox => Set<OutboxEvent>();
    public DbSet<QueueToken> QueueTokens => Set<QueueToken>();
    public DbSet<QueueDisplay> QueueDisplays => Set<QueueDisplay>();

    Guid? CurrentTenantId => tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("scheduling");
        modelBuilder.Entity<ScheduleResource>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.FacilityId, x.Name }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ScheduleTemplate>(e =>
        {
            e.Property(x => x.CalendarKind).HasConversion<string>().HasMaxLength(20);
            e.OwnsMany(x => x.Hours, o => o.ToJson());
            e.OwnsMany(x => x.Breaks, o => o.ToJson());
            e.OwnsMany(x => x.VisitLengths, o =>
            {
                o.ToJson();
                o.Property(x => x.VisitType).HasConversion<string>();
            });
            e.HasIndex(x => new { x.TenantId, x.FacilityId, x.CalendarId }).IsUnique(); // one template per calendar and location
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Appointment>(e =>
        {
            e.Property(x => x.CalendarKind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.VisitType).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ReasonCategory).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ReasonText).HasMaxLength(500);
            e.Property(x => x.Room).HasMaxLength(50);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.FacilityId, x.CalendarId, x.Start });
            // The day view and the board read a facility's time range: seek on Start and cover what the views need.
            e.HasIndex(x => new { x.TenantId, x.FacilityId, x.Start }).IncludeProperties(x => new { x.End, x.CalendarId, x.State, x.PatientId, x.VisitType });
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ScheduleSlot>(e =>
        {
            e.ToTable("ScheduleSlots");
            e.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            // No double booking (MDV-P1-022): one appointment per calendar, location and 5-minute unit, enforced by the database.
            e.HasIndex(x => new { x.CalendarId, x.FacilityId, x.SlotStart }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<SlotHold>(e =>
        {
            e.Property(x => x.VisitType).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.TenantId, x.FacilityId, x.CalendarId, x.Start });
            e.HasIndex(x => new { x.TenantId, x.HeldBy });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<VisitStatusEvent>(e =>
        {
            e.ToTable("VisitStatusEvents");
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CancelReason).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<AppointmentReschedule>(e =>
        {
            e.ToTable("AppointmentReschedules");
            e.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<QueueToken>(e =>
        {
            e.Property(x => x.Prefix).HasMaxLength(4);
            e.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            // One token per visit, and a token number is never given twice on the facility's day (MDV-P1-031).
            e.HasIndex(x => x.AppointmentId).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.FacilityId, x.Date, x.Prefix, x.Number }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<QueueDisplay>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.TokenHash).HasMaxLength(32);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.FacilityId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<OutboxEvent>(e =>
        {
            e.ToTable("OutboxEvents");
            e.Property(x => x.Type).HasMaxLength(100);
            e.HasIndex(x => new { x.PublishedAt, x.OccurredAt });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}

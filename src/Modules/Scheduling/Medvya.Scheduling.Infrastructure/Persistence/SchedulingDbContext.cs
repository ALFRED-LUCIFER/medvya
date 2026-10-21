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
    }
}

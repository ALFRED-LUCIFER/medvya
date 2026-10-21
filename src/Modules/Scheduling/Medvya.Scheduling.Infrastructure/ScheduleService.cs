using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// Templates and the day view. Doctors come from Tenancy (active doctor memberships at the facility) and their names from
/// Identity, both through application contracts; this module never reads another module's tables.
/// shortcut: the location is the facility; per-location calendars inside one facility arrive with LOCATION (MDV-P1-005).
/// shortcut: the facility comes from the request and is checked against the user's membership there; it becomes the
/// session's active facility with the facility picker (MDV-P0-012).
/// </summary>
public sealed class ScheduleService(
    SchedulingDbContext db, IFacilityDirectory facilities, IStaffUsers staff, ICurrentTenant tenant, ICurrentUser user, TimeProvider clock) : IScheduleService
{
    // Day frame when no calendar works that day, so an empty day still renders as a day.
    static readonly TimeOnly DefaultStart = new(9, 0), DefaultEnd = new(17, 0);

    sealed record Calendar(Guid Id, CalendarKind Kind, string Name);

    public async Task<ScheduleDay> GetDayAsync(Guid facilityId, DateOnly? day, CancellationToken cancellationToken = default)
    {
        await RequireAsync(facilityId, SchedulingAccess.CanView, cancellationToken);
        var (facility, zone) = await FacilityAsync(facilityId, cancellationToken);
        var date = day ?? ScheduleRules.LocalDate(clock.GetUtcNow(), zone);

        var templates = await db.Templates.AsNoTracking().Where(t => t.FacilityId == facilityId).ToDictionaryAsync(t => t.CalendarId, cancellationToken);
        var days = (await CalendarsAsync(facilityId, cancellationToken))
            .Select(c => (Calendar: c, Template: templates.GetValueOrDefault(c.Id)))
            .Select(x => (x.Calendar, x.Template, Day: x.Template is null ? CalendarDay.Closed : ScheduleRules.Day(x.Template, date, zone)))
            .ToList();

        var windows = days.SelectMany(x => x.Day.Hours).ToList();
        var start = windows.Count > 0 ? windows.Min(w => w.Start) : ScheduleRules.Instant(date, DefaultStart, zone);
        var end = windows.Count > 0 ? windows.Max(w => w.End) : ScheduleRules.Instant(date, DefaultEnd, zone);
        var columns = days.Select(x => new ScheduleColumn(x.Calendar.Id, x.Calendar.Kind, x.Calendar.Name, x.Template?.SlotMinutes,
            x.Day.Hours, x.Day.Slots, ScheduleRules.Unavailable(x.Day, start, end, x.Template is not null))).ToList();
        return new(facilityId, facility.TimeZone, date, start, end, columns);
    }

    public async Task<IReadOnlyList<TemplateView>> ListTemplatesAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        await RequireAsync(facilityId, SchedulingAccess.CanConfigure, cancellationToken);
        var templates = await db.Templates.AsNoTracking().Where(t => t.FacilityId == facilityId).ToDictionaryAsync(t => t.CalendarId, cancellationToken);
        return [.. (await CalendarsAsync(facilityId, cancellationToken)).Select(c => View(c, templates.GetValueOrDefault(c.Id)))];
    }

    public async Task<TemplateView> SaveTemplateAsync(SaveTemplate command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequireAsync(command.FacilityId, SchedulingAccess.CanConfigure, cancellationToken);
        var calendar = (await CalendarsAsync(command.FacilityId, cancellationToken)).SingleOrDefault(c => c.Id == command.CalendarId && c.Kind == command.CalendarKind)
            ?? throw new SchedulingValidationException([new("calendarId", "unknown")]);

        var template = await db.Templates.SingleOrDefaultAsync(t => t.FacilityId == command.FacilityId && t.CalendarId == command.CalendarId, cancellationToken);
        if (template is null)
        {
            template = new ScheduleTemplate
            {
                Id = Guid.CreateVersion7(), TenantId = TenantId(), FacilityId = command.FacilityId, CalendarId = calendar.Id, CalendarKind = calendar.Kind,
            };
            db.Templates.Add(template);
        }
        template.SlotMinutes = command.SlotMinutes;
        template.Hours = [.. (command.Hours ?? []).OfType<WorkingHours>().Select(h => h with { Room = Clean(h.Room) })];
        template.Breaks = [.. (command.Breaks ?? []).OfType<ScheduleBreak>().Select(b => b with { Label = Clean(b.Label) })];
        template.VisitLengths = [.. (command.VisitLengths ?? []).OfType<VisitLength>()];
        ScheduleRules.Validate(template); // the scoped context is discarded with the request, so a rejected change is never saved
        template.ChangedBy = user.UserId;
        template.ChangedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return View(calendar, template);
    }

    public async Task<ResourceView> AddResourceAsync(AddResource command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequireAsync(command.FacilityId, SchedulingAccess.CanConfigure, cancellationToken);
        var name = Clean(command.Name);
        if (name is null || name.Length > 100) throw new SchedulingValidationException([new("name", name is null ? "required" : "too_long")]);
        if (await db.Resources.AnyAsync(r => r.FacilityId == command.FacilityId && r.Name == name, cancellationToken))
            throw new SchedulingValidationException([new("name", "duplicate")]);

        var resource = new ScheduleResource
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId(), FacilityId = command.FacilityId, Name = name, CreatedBy = user.UserId, CreatedAt = clock.GetUtcNow(),
        };
        db.Resources.Add(resource);
        await db.SaveChangesAsync(cancellationToken);
        return new(resource.Id, resource.FacilityId, resource.Name);
    }

    /// <summary>Doctors first, then resources, each by name: the column order of the day view.</summary>
    async Task<List<Calendar>> CalendarsAsync(Guid facilityId, CancellationToken cancellationToken)
    {
        var doctors = new List<Calendar>();
        foreach (var id in await facilities.ActiveDoctorsAsync(facilityId, cancellationToken))
            doctors.Add(new(id, CalendarKind.Doctor, (await staff.GetAsync(id, cancellationToken))?.DisplayName ?? ""));
        var resources = await db.Resources.AsNoTracking().Where(r => r.FacilityId == facilityId)
            .Select(r => new Calendar(r.Id, CalendarKind.Resource, r.Name)).ToListAsync(cancellationToken);
        return [.. doctors.OrderBy(c => c.Name, StringComparer.Ordinal), .. resources.OrderBy(c => c.Name, StringComparer.Ordinal)];
    }

    async Task RequireAsync(Guid facilityId, Func<Role, bool> allowed, CancellationToken cancellationToken)
    {
        if (facilityId == Guid.Empty) throw new SchedulingValidationException([new("facilityId", "required")]);
        var role = user.UserId is { } id ? await facilities.ActiveRoleAsync(id, facilityId, cancellationToken) : null;
        if (role is not { } r || !allowed(r)) throw new SchedulingAccessDeniedException();
    }

    async Task<(FacilityInfo Facility, TimeZoneInfo Zone)> FacilityAsync(Guid facilityId, CancellationToken cancellationToken)
    {
        // An active membership was found for it, so the facility exists in this tenant.
        var facility = await facilities.FindAsync(facilityId, cancellationToken) ?? throw new SchedulingAccessDeniedException();
        return (facility, TimeZoneInfo.FindSystemTimeZoneById(facility.TimeZone));
    }

    static TemplateView View(Calendar c, ScheduleTemplate? t) =>
        new(c.Id, c.Kind, c.Name, t?.SlotMinutes, t?.Hours ?? [], t?.Breaks ?? [], t?.VisitLengths ?? [], t?.ChangedAt);

    static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    Guid TenantId() => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
}

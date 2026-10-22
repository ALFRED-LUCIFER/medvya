using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// Templates and the day view. Doctors come from Tenancy (active doctor memberships at the facility), their names from
/// Identity and patient names from Patients, all through application contracts; this module never reads another module's tables.
/// shortcut: the location is the facility; per-location calendars inside one facility arrive with LOCATION (MDV-P1-005).
/// </summary>
public sealed class ScheduleService(SchedulingDbContext db, SchedulingContext context) : IScheduleService
{
    // Day frame when no calendar works that day, so an empty day still renders as a day.
    static readonly TimeOnly DefaultStart = new(9, 0), DefaultEnd = new(17, 0);

    public async Task<ScheduleDay> GetDayAsync(Guid facilityId, DateOnly? day, CancellationToken cancellationToken = default)
    {
        await context.RequireAsync(facilityId, SchedulingAccess.CanView, cancellationToken);
        var (facility, zone) = await context.FacilityAsync(facilityId, cancellationToken);
        var now = context.Now;
        var date = day ?? ScheduleRules.LocalDate(now, zone);
        var (from, to) = (ScheduleRules.Instant(date, TimeOnly.MinValue, zone), ScheduleRules.Instant(date.AddDays(1), TimeOnly.MinValue, zone));
        var earliest = from.AddMinutes(-ScheduleRules.MaxVisitMinutes); // lower bound so the (facility, start) index can seek

        var templates = await db.Templates.AsNoTracking().Where(t => t.FacilityId == facilityId).ToDictionaryAsync(t => t.CalendarId, cancellationToken);
        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.FacilityId == facilityId && a.Start < to && a.End > from && a.Start > earliest).OrderBy(a => a.Start).ToListAsync(cancellationToken);
        var me = context.UserId;
        var holds = await db.SlotHolds.AsNoTracking()
            .Where(h => h.FacilityId == facilityId && h.Start < to && h.End > from && h.ExpiresAt > now && h.HeldBy != me).ToListAsync(cancellationToken);
        var names = await context.PatientsAsync(appointments.Select(a => a.PatientId), cancellationToken);

        var days = (await context.CalendarsAsync(facilityId, cancellationToken))
            .Select(c => (Calendar: c, Template: templates.GetValueOrDefault(c.Id)))
            .Select(x => (x.Calendar, x.Template, Day: x.Template is null ? CalendarDay.Closed : ScheduleRules.Day(x.Template, date, zone),
                Booked: appointments.Where(a => a.CalendarId == x.Calendar.Id).ToList()))
            .ToList();

        // The frame covers every column's hours and every appointment, including one left outside changed hours.
        var edges = days.SelectMany(x => x.Day.Hours.Select(w => (w.Start, w.End)).Concat(x.Booked.Select(a => (a.Start, a.End)))).ToList();
        var start = edges.Count > 0 ? edges.Min(e => e.Start) : ScheduleRules.Instant(date, DefaultStart, zone);
        var end = edges.Count > 0 ? edges.Max(e => e.End) : ScheduleRules.Instant(date, DefaultEnd, zone);

        var columns = days.Select(x =>
        {
            var occupied = x.Booked.Where(a => Appointment.Occupies(a.State)).ToList();
            var held = holds.Where(h => h.CalendarId == x.Calendar.Id).ToList();
            var slots = x.Day.Slots
                .Where(s => !occupied.Exists(a => a.Start < s.End && s.Start < a.End))
                .Select(s => new SlotView(s.Start, s.End, held.Exists(h => h.Start < s.End && s.Start < h.End)))
                .ToList();
            return new ScheduleColumn(x.Calendar.Id, x.Calendar.Kind, x.Calendar.Name, x.Template?.SlotMinutes, x.Day.Hours, slots,
                ScheduleRules.Unavailable(x.Day, start, end, x.Template is not null),
                [.. x.Booked.Select(a => SchedulingContext.Chip(a, names))], x.Template is null ? [] : Lengths(x.Template));
        }).ToList();
        return new(facilityId, facility.TimeZone, date, start, end, columns);
    }

    public async Task<IReadOnlyList<TemplateView>> ListTemplatesAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        await context.RequireAsync(facilityId, SchedulingAccess.CanConfigure, cancellationToken);
        var templates = await db.Templates.AsNoTracking().Where(t => t.FacilityId == facilityId).ToDictionaryAsync(t => t.CalendarId, cancellationToken);
        return [.. (await context.CalendarsAsync(facilityId, cancellationToken)).Select(c => View(c, templates.GetValueOrDefault(c.Id), []))];
    }

    public async Task<TemplateView> SaveTemplateAsync(SaveTemplate command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await context.RequireAsync(command.FacilityId, SchedulingAccess.CanConfigure, cancellationToken);
        var calendar = (await context.CalendarsAsync(command.FacilityId, cancellationToken)).SingleOrDefault(c => c.Id == command.CalendarId && c.Kind == command.CalendarKind)
            ?? throw new SchedulingValidationException([new("calendarId", "unknown")]);

        var template = await db.Templates.SingleOrDefaultAsync(t => t.FacilityId == command.FacilityId && t.CalendarId == command.CalendarId, cancellationToken);
        if (template is null)
        {
            template = new ScheduleTemplate
            {
                Id = Guid.CreateVersion7(), TenantId = context.TenantId, FacilityId = command.FacilityId, CalendarId = calendar.Id, CalendarKind = calendar.Kind,
            };
            db.Templates.Add(template);
        }
        template.SlotMinutes = command.SlotMinutes;
        template.Hours = [.. (command.Hours ?? []).OfType<WorkingHours>().Select(h => h with { Room = Clean(h.Room) })];
        template.Breaks = [.. (command.Breaks ?? []).OfType<ScheduleBreak>().Select(b => b with { Label = Clean(b.Label) })];
        template.VisitLengths = [.. (command.VisitLengths ?? []).OfType<VisitLength>()];
        ScheduleRules.Validate(template); // the scoped context is discarded with the request, so a rejected change is never saved
        template.ChangedBy = context.UserId;
        template.ChangedAt = context.Now;
        await db.SaveChangesAsync(cancellationToken);

        // Existing appointments are never moved by a template change; the ones the new hours no longer cover are listed for action.
        var (_, zone) = await context.FacilityAsync(command.FacilityId, cancellationToken);
        var now = context.Now;
        var future = await db.Appointments.AsNoTracking()
            .Where(a => a.FacilityId == command.FacilityId && a.CalendarId == calendar.Id && a.Start >= now).OrderBy(a => a.Start).ToListAsync(cancellationToken);
        var outside = future
            .Where(a => Appointment.Occupies(a.State) && !ScheduleRules.Covers(ScheduleRules.Day(template, ScheduleRules.LocalDate(a.Start, zone), zone), a.Start, a.End))
            .ToList();
        var names = await context.PatientsAsync(outside.Select(a => a.PatientId), cancellationToken);
        return View(calendar, template, [.. outside.Select(a => SchedulingContext.Chip(a, names))]);
    }

    public async Task<ResourceView> AddResourceAsync(AddResource command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await context.RequireAsync(command.FacilityId, SchedulingAccess.CanConfigure, cancellationToken);
        var name = Clean(command.Name);
        if (name is null || name.Length > 100) throw new SchedulingValidationException([new("name", name is null ? "required" : "too_long")]);
        if (await db.Resources.AnyAsync(r => r.FacilityId == command.FacilityId && r.Name == name, cancellationToken))
            throw new SchedulingValidationException([new("name", "duplicate")]);

        var resource = new ScheduleResource
        {
            Id = Guid.CreateVersion7(), TenantId = context.TenantId, FacilityId = command.FacilityId, Name = name, CreatedBy = context.UserId, CreatedAt = context.Now,
        };
        db.Resources.Add(resource);
        await db.SaveChangesAsync(cancellationToken);
        return new(resource.Id, resource.FacilityId, resource.Name);
    }

    /// <summary>Every visit type with its length in this calendar (one slot unless the template says otherwise).</summary>
    static List<VisitLength> Lengths(ScheduleTemplate t) =>
        [.. Enum.GetValues<VisitType>().Select(v => new VisitLength { VisitType = v, Minutes = ScheduleRules.SlotsFor(t, v) * t.SlotMinutes })];

    static TemplateView View(Calendar c, ScheduleTemplate? t, IReadOnlyList<AppointmentChip> outside) =>
        new(c.Id, c.Kind, c.Name, t?.SlotMinutes, t?.Hours ?? [], t?.Breaks ?? [], t?.VisitLengths ?? [], t?.ChangedAt, outside);

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

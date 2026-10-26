using Medvya.Patients.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
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
        var date = day ?? ScheduleRules.LocalDate(context.Now, zone);
        var calendars = await context.CalendarsAsync(facilityId, cancellationToken);
        return (await DaysAsync(facility, zone, calendars, [date], cancellationToken)).Single();
    }

    public async Task<ScheduleWeek> GetWeekAsync(Guid facilityId, Guid calendarId, DateOnly? day, CancellationToken cancellationToken = default)
    {
        await context.RequireAsync(facilityId, SchedulingAccess.CanView, cancellationToken);
        var (facility, zone) = await context.FacilityAsync(facilityId, cancellationToken);
        var calendar = (await context.CalendarsAsync(facilityId, cancellationToken)).SingleOrDefault(c => c.Id == calendarId)
            ?? throw new SchedulingValidationException([new("calendarId", "unknown")]);
        var days = await DaysAsync(facility, zone, [calendar], ScheduleRules.Week(day ?? ScheduleRules.LocalDate(context.Now, zone)), cancellationToken);
        return new(facilityId, facility.TimeZone, calendarId, days);
    }

    /// <summary>
    /// Day views of these calendars for these facility-local dates, from one read each of templates, appointments, holds and patient
    /// names (no query per day or per calendar). The appointment read seeks the (facility, start) index between bounded instants.
    /// </summary>
    async Task<List<ScheduleDay>> DaysAsync(FacilityInfo facility, TimeZoneInfo zone, IReadOnlyList<Calendar> calendars, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken)
    {
        var now = context.Now;
        var (from, to) = (ScheduleRules.DayBounds(dates.Min(), zone).From, ScheduleRules.DayBounds(dates.Max(), zone).To);
        var earliest = from.AddMinutes(-ScheduleRules.MaxVisitMinutes); // lower bound so the (facility, start) index can seek
        var ids = calendars.Select(c => c.Id).ToList();
        var templates = await db.Templates.AsNoTracking().Where(t => t.FacilityId == facility.Id && ids.Contains(t.CalendarId)).ToDictionaryAsync(t => t.CalendarId, cancellationToken);
        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.FacilityId == facility.Id && a.Start < to && a.End > from && a.Start > earliest && ids.Contains(a.CalendarId) && !a.Unscheduled) // queued walk-ins take no slot
            .OrderBy(a => a.Start).ToListAsync(cancellationToken);
        var me = context.UserId;
        var holds = await db.SlotHolds.AsNoTracking()
            .Where(h => h.FacilityId == facility.Id && ids.Contains(h.CalendarId) && h.Start < to && h.End > from && h.ExpiresAt > now && h.HeldBy != me).ToListAsync(cancellationToken);
        var names = await context.PatientsAsync(appointments.Select(a => a.PatientId), cancellationToken);
        return [.. dates.Select(date => Day(facility, zone, calendars, templates, appointments, holds, names, date))];
    }

    static ScheduleDay Day(FacilityInfo facility, TimeZoneInfo zone, IReadOnlyList<Calendar> calendars, Dictionary<Guid, ScheduleTemplate> templates,
        List<Appointment> appointments, List<SlotHold> holds, IReadOnlyDictionary<Guid, PatientSummary> names, DateOnly date)
    {
        var (from, to) = ScheduleRules.DayBounds(date, zone);
        var days = calendars
            .Select(c => (Calendar: c, Template: templates.GetValueOrDefault(c.Id)))
            .Select(x => (x.Calendar, x.Template, Day: x.Template is null ? CalendarDay.Closed : ScheduleRules.Day(x.Template, date, zone),
                Booked: appointments.Where(a => a.CalendarId == x.Calendar.Id && a.Start < to && a.End > from).ToList()))
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
                [.. x.Booked.Select(a => SchedulingContext.Chip(a, names))], x.Template is null ? [] : Lengths(x.Template), x.Day.Slots.Count);
        }).ToList();
        return new(facility.Id, facility.TimeZone, date, start, end, columns);
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
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.SaveChangesAsync(cancellationToken);
            await context.AuditAsync(SchedulingAudit.TemplateUpdate, SchedulingAudit.TemplateResource, SchedulingAudit.Operations, template.Id, null, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }

        // Existing appointments are never moved by a template change; the ones the new hours no longer cover are listed for action.
        var (_, zone) = await context.FacilityAsync(command.FacilityId, cancellationToken);
        var now = context.Now;
        var future = await db.Appointments.AsNoTracking()
            .Where(a => a.FacilityId == command.FacilityId && a.CalendarId == calendar.Id && a.Start >= now && !a.Unscheduled).OrderBy(a => a.Start).ToListAsync(cancellationToken);
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
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await context.AuditAsync(SchedulingAudit.ResourceCreate, SchedulingAudit.ResourceResource, SchedulingAudit.Operations, resource.Id, null, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new(resource.Id, resource.FacilityId, resource.Name);
    }

    /// <summary>Every visit type with its length in this calendar (one slot unless the template says otherwise).</summary>
    static List<VisitLength> Lengths(ScheduleTemplate t) =>
        [.. Enum.GetValues<VisitType>().Select(v => new VisitLength { VisitType = v, Minutes = ScheduleRules.SlotsFor(t, v) * t.SlotMinutes })];

    static TemplateView View(Calendar c, ScheduleTemplate? t, IReadOnlyList<AppointmentChip> outside) =>
        new(c.Id, c.Kind, c.Name, t?.SlotMinutes, t?.Hours ?? [], t?.Breaks ?? [], t?.VisitLengths ?? [], t?.ChangedAt, outside);

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

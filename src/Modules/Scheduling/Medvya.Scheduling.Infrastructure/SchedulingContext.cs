using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>A doctor or resource column of a facility.</summary>
public sealed record Calendar(Guid Id, CalendarKind Kind, string Name);

/// <summary>
/// What the scheduling use cases share: the access check against the caller's membership at the facility, the facility's
/// time zone, its calendars (through Tenancy and Identity contracts) and patient names (through the Patients contract).
/// shortcut: the facility comes from the request and is checked against the user's membership there; it becomes the
/// session's active facility with the facility picker (MDV-P0-012).
/// </summary>
public sealed class SchedulingContext(
    SchedulingDbContext db, IFacilityDirectory facilities, IStaffUsers staff, IPatientDirectory patients,
    ICurrentTenant tenant, ICurrentUser user, IStaffAccess access, TimeProvider clock, IAuditLog audit, IFeatureFlags flags)
{
    /// <summary>
    /// Writes the use case's audit event inside its open transaction, after its changes are saved and before the commit, so the
    /// event commits with the change and a failed audit write rolls it back (fail closed, MDV-P0-017). Opaque ids and codes only.
    /// </summary>
    /// <param name="detail">Codes only (states, a reason code), never names or free text.</param>
    public Task AuditAsync(string action, string resourceType, string purpose, Guid? resourceId, Guid? patientId, CancellationToken cancellationToken, string? detail = null)
    {
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("An audited write runs in a transaction.");
        return audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = UserId, Action = action, ResourceType = resourceType,
            ResourceId = resourceId, PatientId = patientId, Purpose = purpose, Outcome = AuditOutcome.Allowed, OccurredAt = Now, Detail = detail,
        }, tx.GetDbTransaction(), cancellationToken);
    }

    public DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>The visit steps the tenant has on (MDV audit UX-05): a step that is off is skipped on the board and by the consultation.</summary>
    public async Task<VisitSteps> StepsAsync(CancellationToken cancellationToken) =>
        new(await flags.IsEnabledAsync(VisitStepFlags.Nurse, cancellationToken), await flags.IsEnabledAsync(VisitStepFlags.Billing, cancellationToken));

    public Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    public Guid? UserId => user.UserId;

    /// <summary>
    /// Leave and exceptions of a calendar (MDV-P1-012, owner decision of 7 Nov, #231): practice configuration manages every calendar,
    /// a doctor with booking access only their own. Only they see the free-text leave reason; everyone else sees "On leave".
    /// </summary>
    public async Task<bool> CanManageCalendarAsync(Guid facilityId, Guid calendarId, CancellationToken cancellationToken)
    {
        var configure = await access.AccessAsync(SchedulingAccess.CanConfigure.Capability, facilityId, cancellationToken);
        if (Array.IndexOf(SchedulingAccess.CanConfigure.Levels, configure) >= 0) return true;
        if (calendarId != UserId) return false;
        var book = await access.AccessAsync(SchedulingAccess.CanBook.Capability, facilityId, cancellationToken);
        return Array.IndexOf(SchedulingAccess.CanBook.Levels, book) >= 0;
    }

    public async Task RequireAsync(Guid facilityId, SchedulingAccess.Need need, CancellationToken cancellationToken)
    {
        if (facilityId == Guid.Empty) throw new SchedulingValidationException([new("facilityId", "required")]);
        var granted = await access.AccessAsync(need.Capability, facilityId, cancellationToken);
        if (!need.Levels.Contains(granted)) throw new SchedulingAccessDeniedException();
    }

    public async Task<(FacilityInfo Facility, TimeZoneInfo Zone)> FacilityAsync(Guid facilityId, CancellationToken cancellationToken)
    {
        // Called after RequireAsync: an active membership was found for it, so the facility exists in this tenant.
        var facility = await facilities.FindAsync(facilityId, cancellationToken) ?? throw new SchedulingAccessDeniedException();
        return (facility, TimeZoneInfo.FindSystemTimeZoneById(facility.TimeZone));
    }

    /// <summary>Holidays of the facility between two facility-local dates (inclusive), by date (MDV-P1-005).</summary>
    public Task<IReadOnlyDictionary<DateOnly, string>> ClosuresAsync(Guid facilityId, DateOnly first, DateOnly last, CancellationToken cancellationToken) =>
        facilities.ClosuresAsync(facilityId, first, last, cancellationToken);

    /// <summary>Leave and one-off sessions whose dates meet [first, last], optionally of one calendar (MDV-P1-012).</summary>
    public Task<List<ScheduleAdjustment>> ExceptionsAsync(Guid facilityId, DateOnly first, DateOnly last, Guid? calendarId, CancellationToken cancellationToken) =>
        db.Exceptions.AsNoTracking().Where(x => x.FacilityId == facilityId && x.FromDate <= last && x.ToDate >= first && (calendarId == null || x.CalendarId == calendarId))
            .ToListAsync(cancellationToken);

    /// <summary>The room of the calendar's session running now, else of the session the visit was booked in; else unchanged.</summary>
    public async Task<string?> RoomAsync(Appointment visit, TimeZoneInfo zone, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var template = await db.Templates.AsNoTracking().SingleOrDefaultAsync(t => t.FacilityId == visit.FacilityId && t.CalendarId == visit.CalendarId, cancellationToken);
        if (template is null) return visit.Room;
        var today = ScheduleRules.LocalDate(now, zone);
        var hours = ScheduleRules.Day(template, today, zone, await ExceptionsAsync(visit.FacilityId, today, today, visit.CalendarId, cancellationToken)).Hours;
        var session = hours.FirstOrDefault(w => w.Start <= now && now < w.End) ?? hours.FirstOrDefault(w => w.Start <= visit.Start && visit.Start < w.End);
        return session?.Room ?? visit.Room;
    }

    /// <summary>Doctors first, then resources, each by name: the column order of the day view.</summary>
    public async Task<List<Calendar>> CalendarsAsync(Guid facilityId, CancellationToken cancellationToken)
    {
        var ids = await facilities.ActiveDoctorsAsync(facilityId, cancellationToken);
        var names = await staff.DisplayNamesAsync(ids, cancellationToken); // one query for every doctor
        var doctors = ids.Select(id => new Calendar(id, CalendarKind.Doctor, names.GetValueOrDefault(id) ?? "")).ToList();
        var resources = await db.Resources.AsNoTracking().Where(r => r.FacilityId == facilityId)
            .Select(r => new Calendar(r.Id, CalendarKind.Resource, r.Name)).ToListAsync(cancellationToken);
        return [.. doctors.OrderBy(c => c.Name, StringComparer.Ordinal), .. resources.OrderBy(c => c.Name, StringComparer.Ordinal)];
    }

    /// <summary>True while the calendar is an active doctor membership or an existing resource of the facility.</summary>
    public async Task<bool> IsActiveAsync(Guid facilityId, Guid calendarId, CalendarKind kind, CancellationToken cancellationToken) =>
        kind == CalendarKind.Doctor
            ? (await facilities.ActiveDoctorsAsync(facilityId, cancellationToken)).Contains(calendarId)
            : await db.Resources.AnyAsync(r => r.FacilityId == facilityId && r.Id == calendarId, cancellationToken);

    /// <summary>Staff display names by user id (doctors' calendars are their user ids).</summary>
    public Task<IReadOnlyDictionary<Guid, string>> DisplayNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        staff.DisplayNamesAsync(ids, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, PatientSummary>> PatientsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        patients.SummariesAsync([.. ids.Distinct()], cancellationToken);

    public static AppointmentChip Chip(Appointment a, IReadOnlyDictionary<Guid, PatientSummary> names) =>
        new(a.Id, AppointmentReference.Of(a.Id), a.PatientId, names.TryGetValue(a.PatientId, out var p) ? p.FullName : "", a.VisitType, a.Start, a.End, a.State, a.SeriesId);
}

using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

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
    ICurrentTenant tenant, ICurrentUser user, TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();

    public Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    public Guid? UserId => user.UserId;

    public async Task RequireAsync(Guid facilityId, Func<Role, bool> allowed, CancellationToken cancellationToken)
    {
        if (facilityId == Guid.Empty) throw new SchedulingValidationException([new("facilityId", "required")]);
        var role = user.UserId is { } id ? await facilities.ActiveRoleAsync(id, facilityId, cancellationToken) : null;
        if (role is not { } r || !allowed(r)) throw new SchedulingAccessDeniedException();
    }

    public async Task<(FacilityInfo Facility, TimeZoneInfo Zone)> FacilityAsync(Guid facilityId, CancellationToken cancellationToken)
    {
        // Called after RequireAsync: an active membership was found for it, so the facility exists in this tenant.
        var facility = await facilities.FindAsync(facilityId, cancellationToken) ?? throw new SchedulingAccessDeniedException();
        return (facility, TimeZoneInfo.FindSystemTimeZoneById(facility.TimeZone));
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

    public Task<IReadOnlyDictionary<Guid, PatientSummary>> PatientsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        patients.SummariesAsync([.. ids.Distinct()], cancellationToken);

    public static AppointmentChip Chip(Appointment a, IReadOnlyDictionary<Guid, PatientSummary> names) =>
        new(a.Id, AppointmentReference.Of(a.Id), a.PatientId, names.TryGetValue(a.PatientId, out var p) ? p.FullName : "", a.VisitType, a.Start, a.End, a.State);
}

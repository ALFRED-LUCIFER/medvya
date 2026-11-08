using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// Care relationships from visits (MDV-P2-024): a visit at the facility, one there today (facility-local day), or one in the clinician's
/// own calendar. A cancelled visit never happened, so it counts for nothing; a no-show was still the clinician's patient.
/// </summary>
public sealed class VisitCareRelationship(SchedulingDbContext db, IFacilityDirectory facilities, TimeProvider clock) : ICareRelationshipSource
{
    public async Task<bool> RelatedAsync(Guid patientId, Guid facilityId, Guid clinicianUserId, CareScope scope, CancellationToken cancellationToken)
    {
        var visits = db.Appointments.AsNoTracking().Where(a => a.PatientId == patientId && a.FacilityId == facilityId && a.State != AppointmentState.Cancelled);
        switch (scope)
        {
            case CareScope.Assigned:
                visits = visits.Where(a => a.CalendarKind == CalendarKind.Doctor && a.CalendarId == clinicianUserId);
                break;
            case CareScope.FacilityToday:
                if (await facilities.FindAsync(facilityId, cancellationToken) is not { } facility) return false;
                var zone = TimeZoneInfo.FindSystemTimeZoneById(facility.TimeZone);
                var today = ScheduleRules.LocalDate(clock.GetUtcNow(), zone);
                var (from, to) = (ScheduleRules.Instant(today, TimeOnly.MinValue, zone), ScheduleRules.Instant(today.AddDays(1), TimeOnly.MinValue, zone));
                visits = visits.Where(a => a.Start >= from && a.Start < to);
                break;
        }
        return await visits.AnyAsync(cancellationToken);
    }
}

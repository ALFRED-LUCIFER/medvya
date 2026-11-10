using Medvya.Patients.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Patients.Infrastructure;

/// <summary>
/// A patient registered at the facility is one of its patients (MDV-P2-024), which is a care relationship only under "all facility
/// clinicians"; the narrower scopes need a visit or encounter, which their own modules answer.
/// MDV-P1-072: the record is shared by the organisation, but this relationship stays anchored to where the person was registered, so a clinician
/// at another location opens the chart through a visit there, an encounter or an access request (audited), never by registration alone.
/// </summary>
public sealed class RegistrationCareRelationship(PatientsDbContext db) : ICareRelationshipSource
{
    public Task<bool> RelatedAsync(Guid patientId, Guid facilityId, Guid clinicianUserId, CareScope scope, CancellationToken cancellationToken) =>
        scope == CareScope.Facility
            ? db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId && p.FacilityId == facilityId, cancellationToken)
            : Task.FromResult(false);
}

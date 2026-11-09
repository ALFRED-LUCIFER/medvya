using Medvya.Encounters.Application;
using Medvya.Encounters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Encounters.Infrastructure;

/// <summary>An encounter's patient, facility and clinician for other modules (<see cref="IEncounterDirectory"/>); the room is not looked up.</summary>
public sealed class EncounterDirectory(EncountersDbContext db) : IEncounterDirectory
{
    public async Task<EncounterView?> FindAsync(Guid encounterId, CancellationToken cancellationToken = default) =>
        await db.Encounters.AsNoTracking().Where(e => e.Id == encounterId)
            .Select(e => new EncounterView(e.Id, e.AppointmentId, e.PatientId, e.FacilityId, e.LeadClinicianId, e.Status, e.AuthorityMode, e.StartedAt, e.EndedAt, null))
            .SingleOrDefaultAsync(cancellationToken);
}

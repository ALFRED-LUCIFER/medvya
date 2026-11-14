using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Scheduling.Application;
using Medvya.Encounters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Encounters.Infrastructure;

/// <summary>An encounter's patient, facility and clinician for other modules (<see cref="IEncounterDirectory"/>); the room is not looked up.</summary>
public sealed class EncounterDirectory(EncountersDbContext db, IFollowUpBookings followUps) : IEncounterDirectory
{
    public async Task<EncounterPlan> PlanAsync(Guid encounterId, CancellationToken cancellationToken = default) => new(
        [.. (await db.Problems.AsNoTracking().Where(p => p.EncounterId == encounterId).OrderBy(p => p.RecordedAt).ThenBy(p => p.Id).ToListAsync(cancellationToken))
            .Select(p => new PlanDiagnosis(p.Description, p.Verification == ProblemVerification.Provisional))],
        (await followUps.FindAsync(encounterId, cancellationToken))?.DueOn);

    public async Task<EncounterView?> FindAsync(Guid encounterId, CancellationToken cancellationToken = default) =>
        await db.Encounters.AsNoTracking().Where(e => e.Id == encounterId)
            .Select(e => new EncounterView(e.Id, e.AppointmentId, e.PatientId, e.FacilityId, e.LeadClinicianId, e.Status, e.AuthorityMode, e.StartedAt, e.EndedAt, null, e.IdentityConfirmedAt, e.IdentityConfirmedBy))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<EncounterView>> FindManyAsync(IReadOnlyCollection<Guid> encounterIds, CancellationToken cancellationToken = default) =>
        await db.Encounters.AsNoTracking().Where(e => encounterIds.Contains(e.Id))
            .Select(e => new EncounterView(e.Id, e.AppointmentId, e.PatientId, e.FacilityId, e.LeadClinicianId, e.Status, e.AuthorityMode, e.StartedAt, e.EndedAt, null, e.IdentityConfirmedAt, e.IdentityConfirmedBy))
            .ToListAsync(cancellationToken);
}

/// <summary>The patient's own, scanned-clean documents (<see cref="IDocumentDirectory"/>).</summary>
public sealed class DocumentDirectory(EncountersDbContext db) : IDocumentDirectory
{
    public Task<bool> BelongsToAsync(Guid documentId, Guid patientId, CancellationToken cancellationToken = default) =>
        db.Documents.AsNoTracking().AnyAsync(d => d.Id == documentId && d.PatientId == patientId && d.ScanStatus == Medvya.Encounters.Domain.ScanStatus.Clean, cancellationToken);
}

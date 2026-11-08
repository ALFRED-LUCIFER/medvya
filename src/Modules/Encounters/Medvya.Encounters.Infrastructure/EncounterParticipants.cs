using System.Data.Common;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Patients.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// Under "assigned only" (MDV-P2-024), a doctor who led an encounter of the patient at the facility keeps a care relationship with them,
/// even when the visit was in a colleague's calendar. The wider scopes are answered by visits and registration already.
/// </summary>
public sealed class EncounterCareRelationship(EncountersDbContext db) : ICareRelationshipSource
{
    public Task<bool> RelatedAsync(Guid patientId, Guid facilityId, Guid clinicianUserId, CareScope scope, CancellationToken cancellationToken) =>
        scope == CareScope.Assigned
            ? db.Encounters.AsNoTracking().AnyAsync(e => e.PatientId == patientId && e.FacilityId == facilityId && e.LeadClinicianId == clinicianUserId, cancellationToken)
            : Task.FromResult(false);
}

/// <summary>
/// Encounters are clinical data, so merging two records that both have one needs a second clinician (MDV-P1-017). A merge moves them to the
/// surviving record and an undo moves them back, inside the merge's transaction on its connection.
/// </summary>
public sealed class EncounterMergeParticipant(EncountersDbContext db, ICurrentTenant tenant) : IPatientMergeParticipant
{
    public async Task<bool> HasClinicalDataAsync(Guid patientId, CancellationToken cancellationToken) =>
        await db.Encounters.AsNoTracking().AnyAsync(e => e.PatientId == patientId, cancellationToken)
        || await db.Intakes.AsNoTracking().AnyAsync(i => i.PatientId == patientId, cancellationToken)
        || await db.Observations.AsNoTracking().AnyAsync(o => o.PatientId == patientId, cancellationToken);

    public Task<int> VisitsAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(0); // visits are Scheduling's to count

    public async Task MoveAsync(Guid fromPatientId, Guid toPatientId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var shared = await SharedAsync(transaction, cancellationToken);
        await shared.Encounters.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.Intakes.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.Observations.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.IntakeEscalations.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
    }

    public async Task RestoreAsync(Guid fromPatientId, Guid toPatientId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var shared = await SharedAsync(transaction, cancellationToken);
        await shared.Encounters.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.Intakes.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.Observations.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.IntakeEscalations.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
    }

    async Task<EncountersDbContext> SharedAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var shared = new EncountersDbContext(new DbContextOptionsBuilder<EncountersDbContext>().UseNpgsql(transaction.Connection!).Options, tenant);
        await shared.Database.UseTransactionAsync(transaction, cancellationToken);
        return shared;
    }
}

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
        || await db.Allergies.AsNoTracking().AnyAsync(a => a.PatientId == patientId, cancellationToken)
        || await db.Problems.AsNoTracking().AnyAsync(p => p.PatientId == patientId, cancellationToken)
        || await db.MedicationStatements.AsNoTracking().AnyAsync(m => m.PatientId == patientId, cancellationToken)
        || await db.Intakes.AsNoTracking().AnyAsync(i => i.PatientId == patientId, cancellationToken)
        || await db.Observations.AsNoTracking().AnyAsync(o => o.PatientId == patientId, cancellationToken)
        || await db.HistoryEntries.AsNoTracking().AnyAsync(h => h.PatientId == patientId, cancellationToken)
        || await db.Immunisations.AsNoTracking().AnyAsync(i => i.PatientId == patientId, cancellationToken)
        || await db.MilestoneResponses.AsNoTracking().AnyAsync(m => m.PatientId == patientId, cancellationToken)
        || await db.GeriatricAssessments.AsNoTracking().AnyAsync(g => g.PatientId == patientId, cancellationToken)
        || await db.CareTargets.AsNoTracking().AnyAsync(c => c.PatientId == patientId, cancellationToken)
        || await db.CareChecks.AsNoTracking().AnyAsync(c => c.PatientId == patientId, cancellationToken)
        || await db.CareGoals.AsNoTracking().AnyAsync(c => c.PatientId == patientId, cancellationToken)
        || await db.PregnancyEpisodes.AsNoTracking().AnyAsync(p => p.PatientId == patientId, cancellationToken)
        || await db.PregnancyRuledOuts.AsNoTracking().AnyAsync(p => p.PatientId == patientId, cancellationToken);

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
        await shared.Allergies.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.MedicationStatements.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.MedicationDecisions.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.MedicationReconciliations.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.Problems.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.HistoryEntries.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.Immunisations.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.MilestoneResponses.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.GeriatricAssessments.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.CareTargets.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.CareChecks.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.CareGoals.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        // shortcut: two records that both have an open pregnancy cannot merge (the unique index refuses and the merge rolls back); a clinician
        // closes one first. A merge step that asks which to keep when a real case needs it.
        await shared.PregnancyEpisodes.Where(e => e.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, toPatientId).SetProperty(e => e.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.PregnancyRuledOuts.Where(e => e.PatientId == fromPatientId)
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
        await shared.Allergies.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.MedicationStatements.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.MedicationDecisions.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.MedicationReconciliations.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.Problems.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.HistoryEntries.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.Immunisations.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.MilestoneResponses.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.GeriatricAssessments.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.CareTargets.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.CareChecks.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.CareGoals.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.PregnancyEpisodes.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PatientId, fromPatientId).SetProperty(e => e.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.PregnancyRuledOuts.Where(e => e.PatientId == toPatientId && e.MergedFromPatientId == fromPatientId)
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

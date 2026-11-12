using Medvya.Audit.Application;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// The chart's Visits block (MDV-1088): each past encounter with its diagnoses, prescribed medicines, placed tests and whether the note is signed.
/// Clinical access first, then the reads, then the audit event with the patient (as the chart does).
/// shortcut: the latest <see cref="IPatientVisits.Limit"/> visits only, older ones are on the full timeline; a medicine is listed once even if its prescription was later cancelled, the prescription itself shows the state.
/// </summary>
public sealed class PatientVisitsService(EncountersDbContext db, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock, IVisitTests tests) : IPatientVisits
{
    public const string ReadAction = "patient.visits.read";
    static readonly Access[] Treats = [Access.Read, Access.Limited, Access.Full, Access.Sign];

    public async Task<IReadOnlyList<VisitSummary>> ListAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Treats, cancellationToken);
        var visits = await db.Encounters.AsNoTracking().Where(e => e.PatientId == patientId)
            .OrderByDescending(e => e.StartedAt).ThenByDescending(e => e.Id).Take(IPatientVisits.Limit).ToListAsync(cancellationToken);
        var ids = visits.Select(v => v.Id).ToList();
        var signed = (await db.ConsultNotes.AsNoTracking().Where(n => ids.Contains(n.EncounterId) && n.Status == NoteStatus.Signed).Select(n => n.EncounterId).ToListAsync(cancellationToken)).ToHashSet();
        var problems = (await db.Problems.AsNoTracking().Where(p => p.EncounterId != null && ids.Contains(p.EncounterId.Value)).OrderBy(p => p.RecordedAt).ToListAsync(cancellationToken))
            .ToLookup(p => p.EncounterId!.Value);
        var medicines = (await db.MedicationStatements.AsNoTracking().Where(m => m.EncounterId != null && ids.Contains(m.EncounterId.Value)).OrderBy(m => m.Name).ToListAsync(cancellationToken))
            .ToLookup(m => m.EncounterId!.Value);
        var placed = (await tests.ForEncountersAsync(ids, cancellationToken)).ToLookup(t => t.EncounterId);
        await ChartReads.AuditAsync(audit, tenant, user.UserId, clock, ReadAction, patientId, cancellationToken);
        return [.. visits.Select(v => new VisitSummary(v.Id, v.StartedAt, v.Status, v.LeadClinicianId, signed.Contains(v.Id),
            [.. problems[v.Id].Select(p => new VisitDiagnosis(p.Description, p.Icd10Code))],
            [.. medicines[v.Id].DistinctBy(m => (m.Name, m.Dose)).Select(m => new VisitMedicine(m.Name, m.Dose))],
            [.. placed[v.Id].Select(t => t.Name).Distinct()]))];
    }
}

using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// Patient 360's chart (MDV-P2-004). Clinical access first (role, care relationship, facility), then the read, then the audit event with
/// the patient: an unauthorised caller reads nothing. A fact with no source cannot exist (the column is required), so none is shown bare.
/// </summary>
public sealed class PatientChartService(EncountersDbContext db, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IPatientChart
{
    public const string ReadAction = "patient.chart.read";
    /// <summary>Every clinician who treats patients may read the chart; Patient 360 is not granted to the front desk at all.</summary>
    static readonly Access[] Treats = [Access.Read, Access.Limited, Access.Full, Access.Sign];
    const int TrendPoints = 12, RecentEncounters = 5;

    public async Task<PatientChart> GetAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Treats, cancellationToken);

        var allergies = await db.Allergies.AsNoTracking().Where(a => a.PatientId == patientId).OrderBy(a => a.RecordedAt).ToListAsync(cancellationToken);
        var problems = await db.Problems.AsNoTracking().Where(p => p.PatientId == patientId).OrderBy(p => p.RecordedAt).ToListAsync(cancellationToken);
        var medicines = await db.MedicationStatements.AsNoTracking().Where(m => m.PatientId == patientId).OrderBy(m => m.State).ThenBy(m => m.Name).ToListAsync(cancellationToken);
        // The latest reading of each code, chosen in the database.
        var vitals = await db.Observations.AsNoTracking().Where(o => o.PatientId == patientId && o.Code != ObservationCode.HbA1c)
            .GroupBy(o => o.Code).Select(g => g.OrderByDescending(o => o.TakenAt).First()).ToListAsync(cancellationToken);
        var trends = await ChartReads.TrendsAsync(db, patientId, TrendPoints, cancellationToken);
        var encounters = await db.Encounters.AsNoTracking().Where(e => e.PatientId == patientId).OrderByDescending(e => e.StartedAt).Take(RecentEncounters)
            .Select(e => new RecentEncounter(e.Id, e.StartedAt, e.Status)).ToListAsync(cancellationToken);
        var me = user.UserId;
        var open = await db.Encounters.AsNoTracking().Where(e => e.PatientId == patientId && e.LeadClinicianId == me && e.Status == EncounterStatus.Active)
            .Select(e => (Guid?)e.Id).FirstOrDefaultAsync(cancellationToken);

        await ChartReads.AuditAsync(audit, tenant, me, clock, ReadAction, patientId, cancellationToken);

        var listed = allergies.Where(a => a.Kind == AllergyKind.Substance).Select(a => new AllergyView(a.Substance!, a.Reaction, a.Status, a.Source)).ToList();
        // A listed allergy always wins over a "no known allergies" entry: the safer reading when the chart disagrees with itself.
        var state = listed.Count > 0 ? AllergyState.Listed : allergies.Count > 0 ? AllergyState.NoKnownAllergies : AllergyState.NotRecorded;
        return new(state, listed, [.. problems.Select(p => new ProblemView(p.Icd10Code, p.Description, p.Verification, p.OnsetOn, p.Source))],
            [.. medicines.Select(m => new MedicineView(m.Name, m.Dose, m.State, m.Source))],
            [.. vitals.OrderBy(o => o.Code).Select(o => new VitalView(o.Code, o.ValueHundredths / 100m, Observation.Units(o.Code), o.TakenAt, o.Source))], trends, encounters, open);
    }
}

/// <summary>What the chart and the timeline both read and write: the trend series and the audit event of a read.</summary>
static class ChartReads
{
    static readonly ObservationCode[] TrendCodes = [ObservationCode.HbA1c, ObservationCode.BloodPressureSystolic, ObservationCode.BloodPressureDiastolic, ObservationCode.Weight];

    /// <summary>The last <paramref name="take"/> readings of each trend measure, oldest first; a measure with none is left out.</summary>
    public static async Task<List<TrendView>> TrendsAsync(EncountersDbContext db, Guid patientId, int take, CancellationToken cancellationToken)
    {
        var trends = new List<TrendView>();
        foreach (var code in TrendCodes)
        {
            var points = await db.Observations.AsNoTracking().Where(o => o.PatientId == patientId && o.Code == code)
                .OrderByDescending(o => o.TakenAt).Take(take).Select(o => new TrendPoint(o.ValueHundredths / 100m, o.TakenAt, o.Source)).ToListAsync(cancellationToken);
            if (points.Count > 0) trends.Add(new(code, Observation.Units(code), [.. points.AsEnumerable().Reverse()]));
        }
        return trends;
    }

    public static Task AuditAsync(IAuditLog audit, ICurrentTenant tenant, Guid? actor, TimeProvider clock, string action, Guid patientId, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context."), ActorUserId = actor, Action = action,
            ResourceType = "patient", ResourceId = patientId, PatientId = patientId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
        }, cancellationToken);
}

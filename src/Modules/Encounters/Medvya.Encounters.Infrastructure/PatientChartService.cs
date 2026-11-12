using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Identity.Application;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// Patient 360's chart (MDV-P2-004). Clinical access first (role, care relationship, facility), then the read, then the audit event with
/// the patient: an unauthorised caller reads nothing. A fact with no source cannot exist (the column is required), so none is shown bare.
/// </summary>
public sealed class PatientChartService(EncountersDbContext db, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock, IStaffUsers staff, IConsultVisits visits) : IPatientChart
{
    public const string ReadAction = "patient.chart.read", AllergyFlagAction = "patient.chart.allergy-flag";
    /// <summary>Every clinician who treats patients may read the chart; Patient 360 is not granted to the front desk at all.</summary>
    static readonly Access[] Treats = [Access.Read, Access.Limited, Access.Full, Access.Sign];
    const int TrendPoints = 12, RecentEncounters = 5, CareTeamSize = 5;

    public async Task<IReadOnlyList<AllergyFlag>> AllergyFlagsAsync(IReadOnlyList<Guid> patientIds, Guid facilityId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patientIds);
        var ids = patientIds.Distinct().ToList();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ids.Count, IPatientChart.MaxFlags);
        List<Guid> allowed = [];
        foreach (var id in ids)
        {
            try
            {
                await clinical.RequireAsync(id, facilityId, Capability.Patient360Chart, Treats, cancellationToken);
                allowed.Add(id);
            }
            catch (ClinicalAccessDeniedException)
            {
                // Left out: a list of visits shows a flag only where the clinician may see the chart.
            }
        }
        var rows = (await db.Allergies.AsNoTracking().Where(a => allowed.Contains(a.PatientId)).ToListAsync(cancellationToken)).ToLookup(a => a.PatientId);
        // shortcut: "today" is the last 24 hours, not the facility's day; exact day boundary when Today needs it.
        var since = clock.GetUtcNow().AddHours(-24);
        var abnormal = (await db.Observations.AsNoTracking().Where(o => allowed.Contains(o.PatientId) && o.FacilityId == facilityId && o.AppointmentId != null && o.TakenAt >= since && (o.Flag == VitalAlert.High || o.Flag == VitalAlert.Low))
            .Select(o => o.PatientId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        foreach (var id in allowed) await ChartReads.AuditAsync(audit, tenant, user.UserId, clock, AllergyFlagAction, id, cancellationToken);
        return [.. allowed.Select(id => new AllergyFlag(id, ChartReads.BannerOf([.. rows[id]]).State, abnormal.Contains(id)))];
    }

    public async Task<PatientChart> GetAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Treats, cancellationToken);

        var allergies = await db.Allergies.AsNoTracking().Where(a => a.PatientId == patientId).OrderBy(a => a.RecordedAt).ToListAsync(cancellationToken);
        var problems = await db.Problems.AsNoTracking().Where(p => p.PatientId == patientId).OrderBy(p => p.RecordedAt).ToListAsync(cancellationToken);
        var medicines = await db.MedicationStatements.AsNoTracking().Where(m => m.PatientId == patientId).OrderBy(m => m.State).ThenBy(m => m.Name).ToListAsync(cancellationToken);
        var trends = await ChartReads.TrendsAsync(db, patientId, TrendPoints, cancellationToken);
        var encounters = await db.Encounters.AsNoTracking().Where(e => e.PatientId == patientId).OrderByDescending(e => e.StartedAt).Take(RecentEncounters)
            .Select(e => new RecentEncounter(e.Id, e.StartedAt, e.Status)).ToListAsync(cancellationToken);
        var me = user.UserId;
        var history = await PatientHistoryService.ListAsync(db, staff, patientId, cancellationToken);
        // The care team: who led this patient's consultations, the most recent first.
        var leads = (await db.Encounters.AsNoTracking().Where(e => e.PatientId == patientId).OrderByDescending(e => e.StartedAt).Select(e => e.LeadClinicianId).ToListAsync(cancellationToken))
            .Distinct().Take(CareTeamSize).ToList();
        var names = await staff.DisplayNamesAsync(leads, cancellationToken);
        var open = await db.Encounters.AsNoTracking().Where(e => e.PatientId == patientId && e.LeadClinicianId == me && e.Status == EncounterStatus.Active)
            .Select(e => (Guid?)e.Id).FirstOrDefaultAsync(cancellationToken);

        // The doctor's own waiting visit today, asked of Scheduling; the start (POST /v1/encounters) is where access is decided again. A calendar id is the doctor's user id.
        var startable = open is null && me is { } doctor ? await visits.StartableTodayAsync(patientId, facilityId, doctor, cancellationToken) : null;

        await ChartReads.AuditAsync(audit, tenant, me, clock, ReadAction, patientId, cancellationToken);

        var banner = ChartReads.BannerOf(allergies);
        // The latest reading of each code is the last point of its trend, flagged against the code's adult range.
        var latest = trends.Select(t => Latest(t.Code, t.Points[^1])).ToList();
        return new(banner.State, banner.Allergies, [.. problems.Select(p => new ProblemView(p.Icd10Code, p.Description, p.Verification, p.OnsetOn, p.Source, p.Status))],
            [.. medicines.Select(m => new MedicineView(m.Name, m.Dose, m.State, m.Source, m.StoppedAt, m.EncounterId))],
            [.. latest.Where(v => !Observation.IsLab(v.Code))], trends, encounters, open, [.. latest.Where(v => Observation.IsLab(v.Code))], BmiOf(latest),
            history, history.FirstOrDefault(h => h.Section == HistorySection.BloodGroup)?.Text, [.. leads.Select(id => names.GetValueOrDefault(id)).OfType<string>()], startable);
    }

    static VitalView Latest(ObservationCode code, TrendPoint p)
    {
        var range = ReferenceRanges.For(code);
        return new(code, p.Value, Observation.Units(code), p.TakenAt, p.Source, range?.Flag((int)(p.Value * 100)), range?.Text);
    }

    /// <summary>BMI from the latest weight and height, naming the older of the two; none without both.</summary>
    static BmiView? BmiOf(List<VitalView> latest)
    {
        if (latest.Find(v => v.Code == ObservationCode.Weight) is not { } w || latest.Find(v => v.Code == ObservationCode.Height) is not { } h) return null;
        var older = w.TakenAt <= h.TakenAt ? w : h;
        return new(ReferenceRanges.BmiTenths((int)(w.Value * 100), (int)(h.Value * 100)) / 10m, older.Code, older.TakenAt);
    }
}

/// <summary>What the chart and the timeline both read and write: the trend series and the audit event of a read.</summary>
static class ChartReads
{
    /// <summary>A listed allergy always wins over a "no known allergies" entry: the safer reading when the chart disagrees with itself.</summary>
    public static AllergyBanner BannerOf(IReadOnlyCollection<Allergy> rows)
    {
        var listed = rows.Where(a => a.Kind == AllergyKind.Substance).Select(a => new AllergyView(a.Substance!, a.Reaction, a.Status, a.Source)).ToList();
        return new(listed.Count > 0 ? AllergyState.Listed : rows.Count > 0 ? AllergyState.NoKnownAllergies : AllergyState.NotRecorded, listed);
    }

    /// <summary>The last <paramref name="take"/> readings of every code with a reading, oldest first, in one query; a code with none is left out.</summary>
    public static async Task<List<TrendView>> TrendsAsync(EncountersDbContext db, Guid patientId, int take, CancellationToken cancellationToken)
    {
        var groups = await db.Observations.AsNoTracking().Where(o => o.PatientId == patientId).GroupBy(o => o.Code)
            .Select(g => new { g.Key, Points = g.OrderByDescending(o => o.TakenAt).ThenByDescending(o => o.Id).Take(take).Select(o => new TrendPoint(o.ValueHundredths / 100m, o.TakenAt, o.Source)).ToList() })
            .ToListAsync(cancellationToken);
        return [.. groups.OrderBy(g => g.Key).Select(g => new TrendView(g.Key, Observation.Units(g.Key), [.. g.Points.AsEnumerable().Reverse()]))];
    }

    public static Task AuditAsync(IAuditLog audit, ICurrentTenant tenant, Guid? actor, TimeProvider clock, string action, Guid patientId, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context."), ActorUserId = actor, Action = action,
            ResourceType = "patient", ResourceId = patientId, PatientId = patientId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
        }, cancellationToken);
}

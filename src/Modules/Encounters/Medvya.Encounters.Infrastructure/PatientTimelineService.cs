using System.Globalization;
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
/// The timeline (MDV-P2-005): encounters, results and medicines of one patient, newest first, each with its source. Clinical access first,
/// then the read, then the audit event with the patient (as the chart does). Each source is asked for one page more than needed and the
/// pages are merged, so no entry of any source is skipped or repeated across "Load earlier".
/// Documents join once their scan has passed (MDV-P2-006); an unscanned or quarantined file is not in the record.
/// shortcut: lab-feed results (the lab integration) and signed notes (MDV-P2-012) are not sources yet. A page can repeat entries only if more than one page of one source shares a single instant.
/// </summary>
public sealed class PatientTimelineService(EncountersDbContext db, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IPatientTimeline
{
    public const string ReadAction = "patient.timeline.read", TrendsAction = "patient.trends.read";
    static readonly Access[] Treats = [Access.Read, Access.Limited, Access.Full, Access.Sign];
    const int TrendPoints = 24;

    public async Task<TimelinePage> PageAsync(Guid patientId, Guid facilityId, TimelineQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Treats, cancellationToken);
        var limit = Math.Clamp(query.Limit, 1, TimelineQuery.MaxLimit);
        var entries = new List<TimelineEntry>();
        bool Wants(TimelineKind k) => query.Kinds.Count == 0 || query.Kinds.Contains(k);
        var cut = query.Before?.At; // rows at the cut instant are kept and sorted out by id below
        var take = limit + 1;

        if (Wants(TimelineKind.Encounter) && (query.Source is null or ChartSource.ClinicianVerified))
        {
            var rows = await db.Encounters.AsNoTracking().Where(e => e.PatientId == patientId && (cut == null || e.StartedAt <= cut))
                .OrderByDescending(e => e.StartedAt).ThenByDescending(e => e.Id).Take(take + 1).ToListAsync(cancellationToken);
            entries.AddRange(rows.Select(e => new TimelineEntry(e.Id, TimelineKind.Encounter, e.StartedAt, e.Status == EncounterStatus.Active ? "ConsultationInProgress" : "Consultation",
                null, null, e.LeadClinicianId, ChartSource.ClinicianVerified)));
        }
        if (Wants(TimelineKind.Lab))
        {
            var rows = await db.Observations.AsNoTracking().Where(o => o.PatientId == patientId && o.Code == ObservationCode.HbA1c && (cut == null || o.TakenAt <= cut) && (query.Source == null || o.Source == query.Source))
                .OrderByDescending(o => o.TakenAt).ThenByDescending(o => o.Id).Take(take + 1).ToListAsync(cancellationToken);
            entries.AddRange(rows.Select(o => new TimelineEntry(o.Id, TimelineKind.Lab, o.TakenAt, o.Code.ToString(), null,
                $"{(o.ValueHundredths / 100m).ToString("0.##", CultureInfo.InvariantCulture)} {Observation.Units(o.Code)}", null, o.Source)));
        }
        if (Wants(TimelineKind.Medicine))
        {
            var rows = await db.MedicationStatements.AsNoTracking().Where(m => m.PatientId == patientId && (cut == null || m.RecordedAt <= cut) && (query.Source == null || m.Source == query.Source))
                .OrderByDescending(m => m.RecordedAt).ThenByDescending(m => m.Id).Take(take + 1).ToListAsync(cancellationToken);
            entries.AddRange(rows.Select(m => new TimelineEntry(m.Id, TimelineKind.Medicine, m.RecordedAt, m.State == MedicationState.Active ? "MedicineActive" : "MedicineStopped", m.Name, m.Dose, null, m.Source)));
        }
        if (Wants(TimelineKind.Document))
        {
            var rows = await db.Documents.AsNoTracking().Where(d => d.PatientId == patientId && d.ScanStatus == ScanStatus.Clean && (cut == null || d.UploadedAt <= cut) && (query.Source == null || d.Source == query.Source))
                .OrderByDescending(d => d.UploadedAt).ThenByDescending(d => d.Id).Take(take + 1).ToListAsync(cancellationToken);
            entries.AddRange(rows.Select(d => new TimelineEntry(d.Id, TimelineKind.Document, d.UploadedAt, $"Document{d.Type}", null, d.DocumentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d.UploadedBy, d.Source)));
        }

        var ordered = entries.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).AsEnumerable();
        if (query.Before is { } before) ordered = ordered.Where(e => e.At < before.At || (e.At == before.At && e.Id.CompareTo(before.Id) < 0));
        var page = ordered.Take(limit + 1).ToList();
        await ChartReads.AuditAsync(audit, tenant, user.UserId, clock, ReadAction, patientId, cancellationToken);
        return new([.. page.Take(limit)], page.Count > limit);
    }

    public async Task<IReadOnlyList<TrendView>> TrendsAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Treats, cancellationToken);
        var trends = await ChartReads.TrendsAsync(db, patientId, TrendPoints, cancellationToken);
        await ChartReads.AuditAsync(audit, tenant, user.UserId, clock, TrendsAction, patientId, cancellationToken);
        return trends;
    }
}

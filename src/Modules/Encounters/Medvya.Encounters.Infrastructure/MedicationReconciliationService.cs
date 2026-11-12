using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Patients.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// Medication reconciliation (MDV-P2-011). Clinical access first (a doctor, care relationship, the encounter's facility), then the work with its
/// audit event in one transaction. The table is built from the chart's own rows: chart (clinician-verified), patient-reported at this visit, and
/// other sources (imports). Decisions are validated here, never only in the screen, and confirm is refused until every row has one.
/// </summary>
public sealed class MedicationReconciliationService(
    EncountersDbContext db, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock, IStaffAccess staff) : IMedicationReconciliation
{
    public const string ReadAction = "medication.reconcile.read", DecideAction = "medication.reconcile.decide", ConfirmAction = "medication.reconcile.confirm", Resource = "encounter";
    static readonly Access[] Doctors = [Access.Full];

    public async Task<ReconciliationView?> GetAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await audit.WriteAsync(Event(ReadAction, encounter), cancellationToken);
        return await ViewAsync(encounter, cancellationToken);
    }

    public async Task<ReconciliationView?> DecideAsync(Guid encounterId, DecideMedicine command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await FindAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await OpenAsync(encounter, cancellationToken);
        var rows = await RowsAsync(encounter, cancellationToken);
        var row = command.Key is { Length: > 0 } key ? rows.SingleOrDefault(r => r.Key == key) : null;
        if (row is null) throw new ReconciliationValidationException([("key", "unknown")]);
        if (command.Kind is not { } kind) throw new ReconciliationValidationException([("kind", "not_allowed")]);
        var (newDose, reason) = (command.NewDose?.Trim(), command.Reason?.Trim());
        var steps = kind == MedicationDecisionKind.Taper ? command.Steps?.Select(s => s is null ? null! : s with { Dose = s.Dose?.Trim() ?? "" }).ToList() : null;
        var errors = MedicationReconciliationRules.Validate(kind, row.Chart, newDose, reason, steps);
        if (errors.Count > 0) throw new ReconciliationValidationException(errors);
        var now = clock.GetUtcNow();
        // shortcut: a taper starts on the day it is decided (in the tenant's country); a later start date when a doctor needs one.
        DateOnly? endsOn = steps is null ? null
            : MedicationReconciliationRules.TaperEnd(CountryRules.For(await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country.")).Today(now), steps);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.MedicationDecisions.Add(new MedicationDecision
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = encounter.PatientId, EncounterId = encounter.Id, MedicineKey = row.Key, Name = row.Name, Kind = kind,
            FromDose = kind is MedicationDecisionKind.Change or MedicationDecisionKind.Taper ? row.Chart?.Dose : null, NewDose = kind == MedicationDecisionKind.Change ? newDose : null,
            Steps = steps, EndsOn = endsOn, Reason = string.IsNullOrEmpty(reason) ? null : reason, DecidedBy = Me, DecidedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(DecideAction, encounter), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await ViewAsync(encounter, cancellationToken);
    }

    public async Task<ReconciliationView?> ConfirmAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await OpenAsync(encounter, cancellationToken);
        var rows = await RowsAsync(encounter, cancellationToken);
        if (rows.Any(r => r.Decision is null)) throw new ReconciliationConflictException("undecided");

        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.MedicationReconciliations.Add(new MedicationReconciliation { Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = encounter.PatientId, EncounterId = encounter.Id, ConfirmedBy = Me, ConfirmedAt = now });
        var chartRows = await ChartRowsAsync(encounter.PatientId, cancellationToken);
        foreach (var row in rows)
        {
            var d = row.Decision!;
            var chart = chartRows.GetValueOrDefault(row.Key);
            switch (d.Kind)
            {
                case MedicationDecisionKind.Keep: Review(chart!, now); break;
                case MedicationDecisionKind.Change: chart!.Dose = d.NewDose; Review(chart, now); break;
                // shortcut: the chart holds the first step's dose and is not stopped on the end date by itself; a scheduled step-down when the chart keeps dose history.
                case MedicationDecisionKind.Taper: chart!.Dose = d.Steps![0].Dose; Review(chart, now); break;
                case MedicationDecisionKind.Stop: (chart!.State, chart.StoppedAt) = (MedicationState.Stopped, now); Review(chart, now); break;
                case MedicationDecisionKind.Add:
                    // What the patient (or the other source) says, blank where they said nothing: never a guessed dose.
                    var from = row.Patient ?? row.Other;
                    if (chart is not null) { (chart.State, chart.StoppedAt, chart.Dose) = (MedicationState.Active, null, from?.Dose); Review(chart, now); }
                    else db.MedicationStatements.Add(new MedicationStatement
                    {
                        Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = encounter.PatientId, FacilityId = encounter.FacilityId, Name = row.Name, Dose = from?.Dose, Frequency = from?.Frequency,
                        State = MedicationState.Active, Source = ChartSource.ClinicianVerified, RecordedBy = Me, RecordedAt = now, ReviewedAt = now, ReviewedBy = Me,
                    });
                    break;
            }
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            throw new ReconciliationConflictException("confirmed"); // confirmed from another tab in the same moment
        }
        await audit.WriteAsync(Event(ConfirmAction, encounter), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return await ViewAsync(encounter, cancellationToken);
    }

    void Review(MedicationStatement chart, DateTimeOffset now) => (chart.ReviewedAt, chart.ReviewedBy) = (now, Me);

    async Task<Encounter?> FindAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.Patient360Chart, Doctors, cancellationToken);
        return encounter;
    }

    async Task OpenAsync(Encounter encounter, CancellationToken cancellationToken)
    {
        if (await db.MedicationReconciliations.AnyAsync(r => r.EncounterId == encounter.Id, cancellationToken)) throw new ReconciliationConflictException("confirmed");
        if (encounter.Status == EncounterStatus.Ended) throw new ReconciliationConflictException("ended");
    }

    async Task<ReconciliationView> ViewAsync(Encounter encounter, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(encounter, cancellationToken);
        var confirmed = await db.MedicationReconciliations.AsNoTracking().Where(r => r.EncounterId == encounter.Id).Select(r => (DateTimeOffset?)r.ConfirmedAt).SingleOrDefaultAsync(cancellationToken);
        return new ReconciliationView(encounter.Id, encounter.PatientId, rows, rows.Count(r => r.Decision is not null), rows.Count, rows.Count(r => r.Conflict is not null), confirmed);
    }

    /// <summary>The chart's medicine of each name (the latest), tracked so confirm can change it.</summary>
    async Task<Dictionary<string, MedicationStatement>> ChartRowsAsync(Guid patientId, CancellationToken cancellationToken) =>
        (await db.MedicationStatements.Where(m => m.PatientId == patientId && m.Source == ChartSource.ClinicianVerified).ToListAsync(cancellationToken))
            .GroupBy(m => MedicationReconciliationRules.Key(m.Name)).ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.RecordedAt).ThenByDescending(m => m.Id).First());

    async Task<List<MedicineRow>> RowsAsync(Encounter encounter, CancellationToken cancellationToken)
    {
        // A patient-reported medicine counts only for the visit it was said at; an old visit's list must not return as today's claim.
        var all = await db.MedicationStatements.AsNoTracking().Where(m => m.PatientId == encounter.PatientId && (m.AppointmentId == null || m.AppointmentId == encounter.AppointmentId))
            .ToListAsync(cancellationToken);
        var decisions = (await db.MedicationDecisions.AsNoTracking().Where(d => d.EncounterId == encounter.Id).ToListAsync(cancellationToken))
            .GroupBy(d => d.MedicineKey).ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.DecidedAt).ThenByDescending(d => d.Id).First());
        var rows = new List<MedicineRow>();
        foreach (var g in all.GroupBy(m => MedicationReconciliationRules.Key(m.Name)))
        {
            var chartRow = Latest(g, m => m.Source == ChartSource.ClinicianVerified);
            var patientRow = Latest(g, m => m.Source == ChartSource.PatientReported);
            var otherRow = Latest(g, m => m.Source is ChartSource.Imported or ChartSource.SyncedFromHis);
            var (chart, patient, other) = (Side(chartRow), Side(patientRow), Side(otherRow));
            // Settled history (stopped everywhere) is not today's question, unless it was decided here: the confirmed table keeps its rows.
            var d = decisions.GetValueOrDefault(g.Key);
            if (d is null && new[] { chart, patient, other }.All(s => s is null or { State: MedicationState.Stopped })) continue;
            rows.Add(new MedicineRow(g.Key, (chartRow ?? patientRow ?? otherRow)!.Name, chart, patient, other, MedicationReconciliationRules.Conflict(chart, patient, other),
                MedicationReconciliationRules.Allowed(chart), d is null ? null : new MedicineDecisionView(d.Kind, d.NewDose, d.Reason, d.DecidedAt, d.FromDose, d.Steps, d.EndsOn)));
        }
        return [.. rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
    }

    static MedicineSide? Side(MedicationStatement? m) => m is null ? null : new MedicineSide(m.Dose, m.Frequency, m.State, m.NeedsClarification);

    static MedicationStatement? Latest(IEnumerable<MedicationStatement> g, Func<MedicationStatement, bool> from) =>
        g.Where(from).OrderByDescending(m => m.RecordedAt).ThenByDescending(m => m.Id).FirstOrDefault();

    AuditEvent Event(string action, Encounter e) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = e.Id,
        PatientId = e.PatientId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
    };

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
    Guid Me => user.UserId ?? throw new InvalidOperationException("No user.");
}

/// <summary>The current Stop, Change and Taper decisions of one encounter (the latest decision per medicine); see <see cref="IReconciliationStops"/>.</summary>
public sealed class ReconciliationStops(EncountersDbContext db) : IReconciliationStops
{
    public async Task<IReadOnlyList<StoppedMedicine>> StopsAsync(Guid encounterId, CancellationToken cancellationToken = default) =>
        [.. (await CurrentAsync(encounterId, cancellationToken)).Where(d => d.Kind == MedicationDecisionKind.Stop).Select(d => new StoppedMedicine(d.Name, d.Reason))];

    public async Task<IReadOnlyList<ChangedMedicine>> ChangesAsync(Guid encounterId, CancellationToken cancellationToken = default) =>
        [.. (await CurrentAsync(encounterId, cancellationToken)).Where(d => d.Kind is MedicationDecisionKind.Change or MedicationDecisionKind.Taper)
            .Select(d => new ChangedMedicine(d.Name, d.Kind, d.FromDose, d.NewDose, d.Steps, d.EndsOn, d.Reason))];

    async Task<IEnumerable<MedicationDecision>> CurrentAsync(Guid encounterId, CancellationToken cancellationToken) =>
        (await db.MedicationDecisions.AsNoTracking().Where(d => d.EncounterId == encounterId).ToListAsync(cancellationToken))
            .GroupBy(d => d.MedicineKey).Select(g => g.OrderByDescending(d => d.DecidedAt).ThenByDescending(d => d.Id).First()).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);
}

/// <summary>The allergies and active medicines of one patient for the prescription checks; see <see cref="IPrescribingChart"/>.</summary>
public sealed class PrescribingChart(EncountersDbContext db) : IPrescribingChart
{
    public async Task<PrescribingFacts> FactsAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        var allergies = await db.Allergies.AsNoTracking().Where(a => a.PatientId == patientId).OrderBy(a => a.RecordedAt).ThenBy(a => a.Id).ToListAsync(cancellationToken);
        var medicines = await db.MedicationStatements.AsNoTracking().Where(m => m.PatientId == patientId && m.State == MedicationState.Active)
            .OrderBy(m => m.Name).ThenBy(m => m.Id).Select(m => new ChartMedicine(m.Id, m.Name, m.RecordedAt)).ToListAsync(cancellationToken);
        // A flagged reading the nurse has not kept may be a typing slip: a dose is never worked out from it.
        var weight = await db.Observations.AsNoTracking().Where(o => o.PatientId == patientId && o.Code == ObservationCode.Weight && (o.Flag == null || o.FlagKept))
            .OrderByDescending(o => o.TakenAt).ThenByDescending(o => o.Id).Select(o => new ChartReading(o.ValueHundredths, o.TakenAt)).FirstOrDefaultAsync(cancellationToken);
        var egfr = await LatestAsync(patientId, ObservationCode.Egfr, cancellationToken);
        var potassium = await LatestAsync(patientId, ObservationCode.Potassium, cancellationToken);
        // The banner's rule decides the state, so the checks never read "not recorded" differently from the chart.
        return new(ChartReads.BannerOf(allergies).State,
            [.. allergies.Where(a => a.Kind == AllergyKind.Substance && a.Status != AllergyStatus.NotAnAllergy).Select(a => new ChartAllergy(a.Id, a.Substance!, a.Status))], medicines, weight, egfr, potassium);
    }

    Task<ChartReading?> LatestAsync(Guid patientId, ObservationCode code, CancellationToken cancellationToken) =>
        db.Observations.AsNoTracking().Where(o => o.PatientId == patientId && o.Code == code)
            .OrderByDescending(o => o.TakenAt).ThenByDescending(o => o.Id).Select(o => new ChartReading(o.ValueHundredths, o.TakenAt)).FirstOrDefaultAsync(cancellationToken);
}

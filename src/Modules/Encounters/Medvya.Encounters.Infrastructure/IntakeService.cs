using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// Nurse intake (MDV-P2-007). Clinical access first (role at Full for vitals and allergies entry, care relationship, facility of the visit),
/// then the work in one transaction with its audit event (purpose treatment, with the patient). Vitals are checked here, never only in the
/// screen: an impossible value is refused, an out-of-range one is saved with its flag and blocks the handoff until the nurse re-measures or
/// keeps it on purpose. Blanks stay blank. The handoff moves the board card in the same transaction and tells the boards after the commit.
/// </summary>
public sealed class IntakeService(
    EncountersDbContext db, IConsultVisits visits, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IIntake
{
    public const string ReadAction = "intake.read", RecordAction = "intake.record", KeepAction = "intake.keep", EscalateAction = "intake.escalate",
        HandOffAction = "intake.handoff", EscalationsAction = "intake.escalations.read", Resource = "intake";
    public const int MaxText = 500;
    static readonly Access[] Nurses = [Access.Full], Doctors = [Access.Full];

    public async Task<IntakeView?> GetAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        if (await visits.FindAsync(appointmentId, cancellationToken) is not { } visit) return null;
        await RequireAsync(visit, cancellationToken);
        await audit.WriteAsync(Event(ReadAction, visit), cancellationToken);
        return await ViewAsync(visit, cancellationToken);
    }

    public async Task<IntakeView?> RecordAsync(Guid appointmentId, RecordIntake command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await visits.FindAsync(appointmentId, cancellationToken) is not { } visit) return null;
        await RequireAsync(visit, cancellationToken);
        if (visit.State != AppointmentState.WithNurse) throw new IntakeConflictException("not_with_nurse");

        var complaint = command.ChiefComplaint?.Trim();
        var errors = new List<(string, string)>();
        if (complaint?.Length > MaxText) errors.Add(("chiefComplaint", "too_long"));
        var readings = new List<(VitalKind Kind, decimal Value)>();
        for (var i = 0; i < (command.Vitals?.Count ?? 0); i++)
        {
            var r = command.Vitals![i];
            if (r.Value is null) continue; // a blank is unknown, not zero
            if (r.Kind is not { } kind || !Enum.IsDefined(kind)) errors.Add(($"vitals[{i}].kind", "invalid"));
            else if (readings.Exists(x => x.Kind == kind)) errors.Add(($"vitals[{i}].kind", "duplicate"));
            else if (!VitalRules.IsPossible(kind, r.Value.Value)) errors.Add(($"vitals[{i}].value", "implausible"));
            else if (decimal.Round(r.Value.Value, 1) != r.Value.Value) errors.Add(($"vitals[{i}].value", "too_precise"));
            else readings.Add((kind, r.Value.Value));
        }
        var current = await CurrentAsync(appointmentId, cancellationToken);
        decimal? Level(VitalKind k) => readings.Where(x => x.Kind == k).Select(x => (decimal?)x.Value).FirstOrDefault() ?? current.GetValueOrDefault(k)?.ValueTenths / 10m;
        if (Level(VitalKind.SystolicBp) is { } sys && Level(VitalKind.DiastolicBp) is { } dia && sys <= dia) errors.Add(("vitals", "systolic_not_above_diastolic"));
        if (errors.Count > 0) throw new IntakeValidationException(errors);

        var (now, me) = (clock.GetUtcNow(), user.UserId ?? throw new InvalidOperationException("No user."));
        var row = await EnsureAsync(visit, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var (kind, value) in readings)
        {
            var tenths = VitalRules.ToTenths(value);
            if (current.GetValueOrDefault(kind)?.ValueTenths == tenths) continue; // the same value again is not a re-measure and keeps the nurse's decision
            var code = VitalRules.Code(kind);
            var before = await db.Observations.AsNoTracking().Where(o => o.PatientId == visit.PatientId && o.Code == code && o.AppointmentId != appointmentId)
                .OrderByDescending(o => o.TakenAt).ThenByDescending(o => o.Id).Select(o => new { o.ValueHundredths, o.TakenAt }).FirstOrDefaultAsync(cancellationToken);
            db.Observations.Add(new Observation
            {
                Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = visit.FacilityId, PatientId = visit.PatientId, AppointmentId = appointmentId, Code = code,
                ValueHundredths = tenths * 10, Flag = VitalRules.Flag(kind, value, before is null ? null : VitalRules.FromTenths(before.ValueHundredths / 10)), PreviousTenths = before?.ValueHundredths / 10, PreviousAt = before?.TakenAt,
                Source = ChartSource.ClinicianVerified, RecordedBy = me, VerifiedBy = me, TakenAt = now,
            });
        }
        if (complaint is not null) row.ChiefComplaint = complaint.Length == 0 ? null : complaint;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(RecordAction, visit), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await ViewAsync(visit, cancellationToken);
    }

    public async Task<IntakeView?> KeepAsync(Guid appointmentId, VitalKind kind, CancellationToken cancellationToken = default)
    {
        if (await visits.FindAsync(appointmentId, cancellationToken) is not { } visit) return null;
        await RequireAsync(visit, cancellationToken);
        if (visit.State != AppointmentState.WithNurse) throw new IntakeConflictException("not_with_nurse");
        if (!Enum.IsDefined(kind)) throw new IntakeValidationException([("kind", "invalid")]);
        if (!(await CurrentAsync(appointmentId, cancellationToken)).TryGetValue(kind, out var latest) || latest.Flag is null)
            throw new IntakeValidationException([("kind", "not_flagged")]);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Observations.Where(o => o.Id == latest.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.FlagKept, true), cancellationToken);
        await audit.WriteAsync(Event(KeepAction, visit), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await ViewAsync(visit, cancellationToken);
    }

    public async Task<IntakeView?> EscalateAsync(Guid appointmentId, EscalateIntake command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await visits.FindAsync(appointmentId, cancellationToken) is not { } visit) return null;
        await RequireAsync(visit, cancellationToken);
        if (visit.State != AppointmentState.WithNurse) throw new IntakeConflictException("not_with_nurse");
        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) throw new IntakeValidationException([("reason", "required")]);
        if (reason.Length > MaxText) throw new IntakeValidationException([("reason", "too_long")]);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.IntakeEscalations.Add(new IntakeEscalation
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = visit.FacilityId, PatientId = visit.PatientId, AppointmentId = appointmentId, Reason = reason,
            EscalatedBy = user.UserId ?? throw new InvalidOperationException("No user."), EscalatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(EscalateAction, visit), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await ViewAsync(visit, cancellationToken);
    }

    public async Task<IntakeView?> HandOffAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        if (await visits.FindAsync(appointmentId, cancellationToken) is not { } visit) return null;
        await RequireAsync(visit, cancellationToken);
        if (visit.State is not (AppointmentState.WithNurse or AppointmentState.ReadyForDoctor)) throw new IntakeConflictException("not_with_nurse");
        var blockers = await BlockersAsync(visit, cancellationToken);
        if (blockers.Count > 0) throw new IntakeConflictException("handoff_blocked", blockers);

        var (now, me) = (clock.GetUtcNow(), user.UserId ?? throw new InvalidOperationException("No user."));
        var row = await EnsureAsync(visit, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (row.HandedOffAt is null) (row.HandedOffAt, row.HandedOffBy, row.UpdatedAt) = (now, me, now);
        await db.SaveChangesAsync(cancellationToken);
        var moved = await visits.HandOffAsync(appointmentId, tx.GetDbTransaction(), cancellationToken);
        await audit.WriteAsync(Event(HandOffAction, visit), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        if (visit.State == AppointmentState.WithNurse) await visits.NotifyAsync(moved, cancellationToken);
        return await ViewAsync(moved, cancellationToken);
    }

    public async Task<IReadOnlyList<EscalationView>> EscalationsAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        var open = await db.IntakeEscalations.AsNoTracking().Where(e => e.FacilityId == facilityId && !db.Encounters.Any(x => x.AppointmentId == e.AppointmentId))
            .OrderBy(e => e.EscalatedAt).ThenBy(e => e.Id).ToListAsync(cancellationToken);
        var shown = new List<EscalationView>();
        var allowed = new Dictionary<Guid, bool>();
        foreach (var e in open)
        {
            if (!allowed.TryGetValue(e.PatientId, out var may))
            {
                try
                {
                    await clinical.RequireAsync(e.PatientId, facilityId, Capability.Patient360Chart, Doctors, cancellationToken);
                    may = true;
                }
                catch (ClinicalAccessDeniedException)
                {
                    may = false; // not this doctor's patient: under "assigned only" the escalation is the treating doctor's to see
                }
                allowed[e.PatientId] = may;
            }
            if (may) shown.Add(new EscalationView(e.Id, e.AppointmentId, e.PatientId, e.Reason, e.EscalatedAt));
        }
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = EscalationsAction, ResourceType = Resource,
            Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
        }, cancellationToken);
        return shown;
    }

    Task<Access> RequireAsync(ConsultVisit visit, CancellationToken cancellationToken) =>
        clinical.RequireAsync(visit.PatientId, visit.FacilityId, Capability.VitalsAllergiesEntry, Nurses, cancellationToken);

    /// <summary>The latest reading of each kind for the visit.</summary>
    async Task<Dictionary<VitalKind, Observation>> CurrentAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        (await db.Observations.AsNoTracking().Where(o => o.AppointmentId == appointmentId).ToListAsync(cancellationToken))
            .GroupBy(o => VitalRules.Kind(o.Code)!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.TakenAt).ThenByDescending(o => o.Id).First());

    async Task<List<string>> BlockersAsync(ConsultVisit visit, CancellationToken cancellationToken)
    {
        var blockers = new List<string>();
        if ((await CurrentAsync(visit.Id, cancellationToken)).Values.Any(o => o.Flag is not null && !o.FlagKept)) blockers.Add("vitals_undecided");
        return blockers;
    }

    async Task<IntakeView> ViewAsync(ConsultVisit visit, CancellationToken cancellationToken)
    {
        var row = await db.Intakes.AsNoTracking().SingleOrDefaultAsync(i => i.AppointmentId == visit.Id, cancellationToken);
        var vitals = (await CurrentAsync(visit.Id, cancellationToken)).OrderBy(p => p.Key).Select(p => p.Value)
            .Select(o => new IntakeVital(VitalRules.Kind(o.Code)!.Value, VitalRules.FromTenths(o.ValueTenths), o.Flag, o.PreviousTenths is { } p ? VitalRules.FromTenths(p) : null, o.PreviousAt, o.FlagKept, o.TakenAt)).ToList();
        var fresh = await visits.FindAsync(visit.Id, cancellationToken) ?? visit; // the handoff or a colleague may have moved the card
        return new IntakeView(visit.Id, visit.PatientId, visit.FacilityId, fresh.State.ToString(), row?.ChiefComplaint, vitals,
            await db.IntakeEscalations.AnyAsync(e => e.AppointmentId == visit.Id, cancellationToken), row?.HandedOffAt is not null, await BlockersAsync(visit, cancellationToken));
    }

    /// <summary>The visit's intake row, created on the first save. Two first saves at once meet the unique index and share the winner's row.</summary>
    async Task<Intake> EnsureAsync(ConsultVisit visit, CancellationToken cancellationToken)
    {
        if (await db.Intakes.SingleOrDefaultAsync(i => i.AppointmentId == visit.Id, cancellationToken) is { } existing) return existing;
        db.Intakes.Add(new Intake { Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = visit.FacilityId, PatientId = visit.PatientId, AppointmentId = visit.Id, UpdatedAt = clock.GetUtcNow() });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
        }
        return await db.Intakes.SingleAsync(i => i.AppointmentId == visit.Id, cancellationToken);
    }

    AuditEvent Event(string action, ConsultVisit visit) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = visit.Id,
        PatientId = visit.PatientId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
    };

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
}

/// <summary>
/// With the nurse-intake flag on, a visit leaves the nurse only by the nurse's handoff (MDV-P2-007): the board's generic move is refused until
/// the intake was handed off. With the flag off the clinic has no nurse step and the board moves as before.
/// </summary>
public sealed class IntakeHandoffGate(EncountersDbContext db, IFeatureFlags flags) : IHandoffGate
{
    public async Task<bool> MayMoveOnAsync(Guid appointmentId, CancellationToken cancellationToken = default) =>
        !await flags.IsEnabledAsync(IntakeFlag.Key, cancellationToken)
        || await db.Intakes.AsNoTracking().AnyAsync(i => i.AppointmentId == appointmentId && i.HandedOffAt != null, cancellationToken);
}

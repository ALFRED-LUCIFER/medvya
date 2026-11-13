using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Patients.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// Nurse intake (MDV-P2-007), and the same entry from the consultation (MDV audit P0-2). Clinical access first (role at Full for vitals and allergies entry, care relationship, facility of the visit),
/// then the work in one transaction with its audit event (purpose treatment, with the patient). Vitals are checked here, never only in the
/// screen: an impossible value is refused, an out-of-range one is saved with its flag and blocks the handoff until the nurse re-measures or
/// keeps it on purpose. Blanks stay blank. The handoff moves the board card in the same transaction and tells the boards after the commit.
/// </summary>
public sealed class IntakeService(
    EncountersDbContext db, IConsultVisits visits, IPatientDirectory patients, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IIntake
{
    public const string ReadAction = "intake.read", RecordAction = "intake.record", KeepAction = "intake.keep", EscalateAction = "intake.escalate",
        HandOffAction = "intake.handoff", AllergyAddAction = "intake.allergy.add", AllergyStatusAction = "intake.allergy.status",
        NoKnownAllergiesAction = "intake.allergy.none", MedicineAddAction = "intake.medicine.add", EscalationsAction = "intake.escalations.read", Resource = "intake";
    public const int MaxText = 500, MaxName = 100, MaxDetail = 50;
    static readonly AllergyStatus[] NewAllergyStatuses = [AllergyStatus.Confirmed, AllergyStatus.PatientReported, AllergyStatus.Unknown];
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
        if (!Open(visit)) throw new IntakeConflictException("visit_closed");

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
        var born = (await patients.SummariesAsync([visit.PatientId], cancellationToken)).GetValueOrDefault(visit.PatientId)?.DateOfBirth
            ?? throw new InvalidOperationException("The visit's patient is not in the directory.");
        var ageInMonths = VitalRules.AgeInMonths(born, DateOnly.FromDateTime(now.UtcDateTime));
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
                ValueHundredths = tenths * 10, Flag = VitalRules.Flag(kind, value, before is null ? null : VitalRules.FromTenths(before.ValueHundredths / 10), ageInMonths), PreviousTenths = before?.ValueHundredths / 10, PreviousAt = before?.TakenAt,
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
        if (!Open(visit)) throw new IntakeConflictException("visit_closed");
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
        // Ids and state only (no reason, no patient): an open Today refetches its escalations at once instead of on its next refresh (MDV-P2-002).
        await visits.NotifyAsync(visit, cancellationToken);
        return await ViewAsync(visit, cancellationToken);
    }

    public async Task<IntakeView?> AddAllergyAsync(Guid appointmentId, AddAllergy command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await OpenAsync(appointmentId, cancellationToken) is not { } visit) return null;
        var (substance, reaction) = (command.Substance?.Trim(), command.Reaction?.Trim());
        var errors = new List<(string, string)>();
        if (string.IsNullOrEmpty(substance)) errors.Add(("substance", "required"));
        else if (substance.Length > MaxName) errors.Add(("substance", "too_long"));
        if (reaction?.Length > MaxText) errors.Add(("reaction", "too_long"));
        // A new allergy is added with a status the nurse chose; "Not an allergy" is for an allergy someone else recorded.
        var status = command.Status ?? default;
        if (command.Status is null || !NewAllergyStatuses.Contains(status)) errors.Add(("status", "invalid"));
        // A patient has a handful of allergies, so the comparison is made here, ignoring case.
        if (errors.Count == 0 && (await db.Allergies.AsNoTracking().Where(a => a.PatientId == visit.PatientId && a.Kind == AllergyKind.Substance).Select(a => a.Substance).ToListAsync(cancellationToken))
                .Exists(x => string.Equals(x, substance, StringComparison.OrdinalIgnoreCase)))
            errors.Add(("substance", "duplicate"));
        if (errors.Count > 0) throw new IntakeValidationException(errors);

        var (now, me) = (clock.GetUtcNow(), user.UserId ?? throw new InvalidOperationException("No user."));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Allergies.Add(new Allergy
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = visit.FacilityId, PatientId = visit.PatientId, Kind = AllergyKind.Substance, Substance = substance!,
            Reaction = string.IsNullOrEmpty(reaction) ? null : reaction, Status = status, Source = ChartSource.PatientReported, RecordedBy = me, RecordedAt = now,
            StatusSetBy = me, StatusSetAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(AllergyAddAction, visit), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await ViewAsync(visit, cancellationToken);
    }

    public async Task<IntakeView?> SetAllergyStatusAsync(Guid appointmentId, Guid allergyId, SetAllergyStatus command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await OpenAsync(appointmentId, cancellationToken) is not { } visit) return null;
        // Only an allergy of this visit's patient: another patient's (or another clinic's) id is simply not found.
        if (!await db.Allergies.AnyAsync(a => a.Id == allergyId && a.PatientId == visit.PatientId && a.Kind == AllergyKind.Substance, cancellationToken)) return null;
        if (command.Status is not { } status || !Enum.IsDefined(status)) throw new IntakeValidationException([("status", "invalid")]);

        var (now, me) = (clock.GetUtcNow(), user.UserId ?? throw new InvalidOperationException("No user."));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Allergies.Where(a => a.Id == allergyId && a.PatientId == visit.PatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status).SetProperty(a => a.StatusSetBy, me).SetProperty(a => a.StatusSetAt, now), cancellationToken);
        await audit.WriteAsync(Event(AllergyStatusAction, visit), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await ViewAsync(visit, cancellationToken);
    }

    public async Task<IntakeView?> ConfirmNoKnownAllergiesAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        if (await OpenAsync(appointmentId, cancellationToken) is not { } visit) return null;
        if (await db.Allergies.AnyAsync(a => a.PatientId == visit.PatientId && a.Kind == AllergyKind.Substance, cancellationToken)) throw new IntakeConflictException("has_allergies");

        var (now, me) = (clock.GetUtcNow(), user.UserId ?? throw new InvalidOperationException("No user."));
        var row = await EnsureAsync(visit, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        (row.NoKnownAllergiesBy, row.NoKnownAllergiesAt, row.UpdatedAt) = (me, now, now);
        // The chart keeps the statement too (Patient 360 shows "No known allergies"); the per-visit confirmation above is what the handoff asks again each visit.
        if (!await db.Allergies.AnyAsync(a => a.PatientId == visit.PatientId, cancellationToken))
            db.Allergies.Add(new Allergy { Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = visit.FacilityId, PatientId = visit.PatientId, Kind = AllergyKind.NoKnownAllergies,
                Status = AllergyStatus.Confirmed, Source = ChartSource.ClinicianVerified, RecordedBy = me, RecordedAt = now, StatusSetBy = me, StatusSetAt = now });
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(NoKnownAllergiesAction, visit), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await ViewAsync(visit, cancellationToken);
    }

    public async Task<IntakeView?> AddMedicineAsync(Guid appointmentId, AddMedicine command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await OpenAsync(appointmentId, cancellationToken) is not { } visit) return null;
        var (name, dose, frequency) = (command.Name?.Trim(), command.Dose?.Trim(), command.Frequency?.Trim());
        var errors = new List<(string, string)>();
        if (string.IsNullOrEmpty(name)) errors.Add(("name", "required"));
        else if (name.Length > MaxName) errors.Add(("name", "too_long"));
        if (dose?.Length > MaxDetail) errors.Add(("dose", "too_long"));
        if (frequency?.Length > MaxDetail) errors.Add(("frequency", "too_long"));
        if (errors.Count > 0) throw new IntakeValidationException(errors);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.MedicationStatements.Add(new MedicationStatement
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = visit.FacilityId, PatientId = visit.PatientId, AppointmentId = appointmentId, Name = name!,
            Dose = string.IsNullOrEmpty(dose) ? null : dose, Frequency = string.IsNullOrEmpty(frequency) ? null : frequency, NeedsClarification = command.NeedsClarification,
            State = MedicationState.Active, Source = ChartSource.PatientReported, RecordedBy = user.UserId ?? throw new InvalidOperationException("No user."), RecordedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(MedicineAddAction, visit), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await ViewAsync(visit, cancellationToken);
    }

    /// <summary>The visit, once the caller may work its intake and the visit is still open (<see cref="Open"/>). Null: no such visit.</summary>
    async Task<ConsultVisit?> OpenAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        if (await visits.FindAsync(appointmentId, cancellationToken) is not { } visit) return null;
        await RequireAsync(visit, cancellationToken);
        return Open(visit) ? visit : throw new IntakeConflictException("visit_closed");
    }

    /// <summary>
    /// Vitals, weight, allergies and medicines are recorded with the nurse and again in the consultation until it ends, so a clinic without a
    /// nurse (intake off) can still run the allergy check and weigh a child (MDV audit P0-2). Between the handoff and the consultation nothing
    /// changes under the doctor's eyes. The escalation and the handoff stay the nurse's.
    /// </summary>
    static bool Open(ConsultVisit visit) => visit.State is AppointmentState.WithNurse or AppointmentState.InConsult;

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
        // Every allergy needs a status set by the clinic (the one a patient or an import brought has none until a nurse looks), and a patient with
        // none on record needs "no known allergies" confirmed on purpose at this visit, never assumed.
        var listed = await db.Allergies.AsNoTracking().Where(a => a.PatientId == visit.PatientId && a.Kind == AllergyKind.Substance).Select(a => a.StatusSetAt).ToListAsync(cancellationToken);
        if (listed.Contains(null)) blockers.Add("allergy_status_required");
        else if (listed.Count == 0 && !await db.Intakes.AsNoTracking().AnyAsync(i => i.AppointmentId == visit.Id && i.NoKnownAllergiesAt != null, cancellationToken))
            blockers.Add("allergies_not_confirmed");
        return blockers;
    }

    async Task<IntakeView> ViewAsync(ConsultVisit visit, CancellationToken cancellationToken)
    {
        var row = await db.Intakes.AsNoTracking().SingleOrDefaultAsync(i => i.AppointmentId == visit.Id, cancellationToken);
        var vitals = (await CurrentAsync(visit.Id, cancellationToken)).OrderBy(p => p.Key).Select(p => p.Value)
            .Select(o => new IntakeVital(VitalRules.Kind(o.Code)!.Value, VitalRules.FromTenths(o.ValueTenths), o.Flag, o.PreviousTenths is { } p ? VitalRules.FromTenths(p) : null, o.PreviousAt, o.FlagKept, o.TakenAt)).ToList();
        var allergies = await db.Allergies.AsNoTracking().Where(a => a.PatientId == visit.PatientId && a.Kind == AllergyKind.Substance).OrderBy(a => a.RecordedAt).ThenBy(a => a.Id)
            .Select(a => new IntakeAllergy(a.Id, a.Substance!, a.Reaction, a.StatusSetAt == null ? null : a.Status, a.Source, a.StatusSetAt)).ToListAsync(cancellationToken);
        var medicines = await db.MedicationStatements.AsNoTracking().Where(m => m.PatientId == visit.PatientId && m.AppointmentId == visit.Id).OrderBy(m => m.RecordedAt).ThenBy(m => m.Id)
            .Select(m => new IntakeMedicine(m.Id, m.Name, m.Dose, m.Frequency, m.NeedsClarification, m.Source, m.RecordedAt)).ToListAsync(cancellationToken);
        var fresh = await visits.FindAsync(visit.Id, cancellationToken) ?? visit; // the handoff or a colleague may have moved the card
        return new IntakeView(visit.Id, visit.PatientId, visit.FacilityId, fresh.State.ToString(), row?.ChiefComplaint, vitals,
            allergies, medicines, allergies.Count == 0 && row?.NoKnownAllergiesAt is not null,
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

/// <summary>Visits with an open nurse escalation (MDV-P1-103), as the escalation list reads them: open until a consultation starts. Ids only.</summary>
public sealed class EscalatedVisits(EncountersDbContext db) : IEscalatedVisits
{
    public async Task<IReadOnlySet<Guid>> EscalatedAsync(IReadOnlyCollection<Guid> appointmentIds, CancellationToken cancellationToken = default) =>
        (await db.IntakeEscalations.AsNoTracking().Where(e => appointmentIds.Contains(e.AppointmentId) && !db.Encounters.Any(x => x.AppointmentId == e.AppointmentId))
            .Select(e => e.AppointmentId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
}

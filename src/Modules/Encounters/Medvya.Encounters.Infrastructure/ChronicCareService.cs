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
/// Chronic care targets, care gaps and goals (MDV-P2-038); see <see cref="IChronicCare"/>. The gap rules are <see cref="CareGapRules"/>: this
/// class only gathers their facts, from this module's tables, the child health schedule (<see cref="IChildHealth"/>) and Prescribing's
/// placed orders (<see cref="IPlacedOrders"/>). Input is validated here. Audit events carry no value.
/// </summary>
public sealed class ChronicCareService(
    EncountersDbContext db, IClinicalAccess clinical, IChildHealth childHealth, IPregnancies pregnancies, IPlacedOrders orders, IPatientDirectory patients, IFacilityDirectory facilities,
    ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IChronicCare
{
    public const string ReadAction = "patient.care.read", TargetAction = "patient.care-target.set", CheckAction = "patient.care-check.record", GoalAction = "patient.care-goal.save";
    static readonly Access[] Treats = [Access.Read, Access.Limited, Access.Full, Access.Sign];
    static readonly Access[] Decides = [Access.Full];

    public async Task<ChronicCareView> GetAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Treats, cancellationToken);
        await audit.WriteAsync(Event(ReadAction, patientId), cancellationToken);
        var now = clock.GetUtcNow();
        var zone = await ZoneAsync(facilityId, cancellationToken);
        var codes = (await db.Problems.AsNoTracking().Where(p => p.PatientId == patientId && p.Status == ProblemStatus.Active && p.Icd10Code != null)
            .Select(p => p.Icd10Code).ToListAsync(cancellationToken));
        var diabetes = codes.Any(CareGapRules.IsDiabetes);
        var latest = await db.Observations.AsNoTracking().Where(o => o.PatientId == patientId).GroupBy(o => o.Code)
            .Select(g => g.OrderByDescending(o => o.TakenAt).ThenByDescending(o => o.Id).First()).ToDictionaryAsync(o => o.Code, cancellationToken);
        var set = await CurrentTargetsAsync(patientId, cancellationToken);
        var checks = await db.CareChecks.AsNoTracking().Where(c => c.PatientId == patientId).GroupBy(c => c.Kind)
            .Select(g => new { g.Key, Last = g.Max(c => (DateOnly?)c.DoneOn) }).ToDictionaryAsync(x => x.Key, x => x.Last, cancellationToken);
        var falls = await db.GeriatricAssessments.AsNoTracking().Where(a => a.PatientId == patientId && a.AssessedAt >= now.AddYears(-1))
            .Select(a => new FallsFact(a.AssessedAt, a.FallsLast12Months, a.TimedUpAndGoTenths != null, a.LyingSystolic != null && a.StandingSystolic != null))
            .ToListAsync(cancellationToken);
        // A signed note of the last year whose structured examination looked at the feet (MDV-P2-042) is a foot check done on its signing day.
        var examined = await (from v in db.SignedNoteVersions.AsNoTracking()
                              join en in db.Encounters.AsNoTracking() on v.EncounterId equals en.Id
                              where en.PatientId == patientId && v.SignedAt >= now.AddYears(-1)
                              select new { v.SignedAt, v.Sections }).ToListAsync(cancellationToken);
        var footByNote = examined.Where(x => CareGapRules.FeetExamined(x.Sections.GetValueOrDefault(NoteTemplates.Examination)))
            .Select(x => (DateOnly?)ChildHealthService.DayIn(zone, x.SignedAt)).Max();
        var placed = await orders.SinceAsync(patientId, now.AddYears(-1), cancellationToken);
        var born = (await patients.SummariesAsync([patientId], cancellationToken)).GetValueOrDefault(patientId)?.DateOfBirth;
        // Only a child has a schedule to be due on; asking for an adult would audit a child health read that never happened.
        var doses = born is { } b && LifeStages.Of(b, ChildHealthService.DayIn(zone, now), openPregnancy: false) == LifeStage.Child
            ? await childHealth.VaccinesDueAsync(patientId, facilityId, cancellationToken) : [];
        // The open pregnancy's gestation today (MDV-P2-034), and whether an OGTT was placed since it began (the due date less 40 weeks).
        var open = (await pregnancies.StatusAsync(patientId, facilityId, cancellationToken)).Open;
        var began = open is null ? (DateOnly?)null : open.DueDate.AddDays(-PregnancyRules.TermDays);
        var ogttOrdered = began is { } from && placed.Any(o => CareGapRules.IsGlucoseTest(o.Name) && ChildHealthService.DayIn(zone, o.OrderedAt) >= from);
        var facts = new CareFacts(diabetes, checks.GetValueOrDefault(CareCheckKind.DiabeticEyeExam), new[] { checks.GetValueOrDefault(CareCheckKind.DiabeticFootExam), footByNote }.Max(), latest.ToDictionary(x => x.Key, x => x.Value.TakenAt),
            placed, [.. doses.Select(d => new DueDoseFact(d.Vaccine, d.Dose, d.DueFrom))], open?.GestationDays, ogttOrdered, falls);

        var goals = await db.CareGoals.AsNoTracking().Where(g => g.PatientId == patientId).OrderBy(g => g.CreatedAt).ThenBy(g => g.Id).Take(IChronicCare.MaxGoals).ToListAsync(cancellationToken);
        return new(diabetes || codes.Any(CareGapRules.IsHypertension) || set.Count > 0,
            [.. CareTargets.Defaults.Keys.Select(code => Target(code, set.GetValueOrDefault(code), latest.GetValueOrDefault(code)))],
            CareGapRules.Due(facts, now, zone), [.. goals.Select(Goal)], CareTargets.SignOff is not null);
    }

    public async Task<CareTargetView> SetTargetAsync(Guid patientId, Guid facilityId, SetCareTarget command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Decides, cancellationToken);
        if (command.Code is not { } code || !CareTargets.Defaults.ContainsKey(code)) throw Invalid("code", "unknown");
        if (command.Limit is not { } limit) throw Invalid("limit", "required");
        // A value too large to hold is out of range too, so the conversion can never overflow.
        var hundredths = limit is > 0 and < 100_000 ? (int)Math.Round(limit * 100, MidpointRounding.AwayFromZero) : -1;
        if (!CareTargets.Allowed(code, hundredths)) throw Invalid("limit", "out_of_range");
        var row = new CareTarget { Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = patientId, Code = code, LimitHundredths = hundredths, SetBy = UserId, SetAt = clock.GetUtcNow() };
        await SaveAsync(TargetAction, patientId, () => db.CareTargets.Add(row), cancellationToken);
        var latest = await db.Observations.AsNoTracking().Where(o => o.PatientId == patientId && o.Code == code).OrderByDescending(o => o.TakenAt).ThenByDescending(o => o.Id).FirstOrDefaultAsync(cancellationToken);
        return Target(code, row, latest);
    }

    public async Task RecordCheckAsync(Guid patientId, Guid facilityId, RecordCareCheck command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Decides, cancellationToken);
        var today = ChildHealthService.DayIn(await ZoneAsync(facilityId, cancellationToken), clock.GetUtcNow());
        var errors = new List<(string, string)>();
        if (command.Kind is not { } kind || !Enum.IsDefined(kind)) errors.Add(("kind", "required"));
        if (command.DoneOn is not { } on) errors.Add(("doneOn", "required"));
        else if (on > today) errors.Add(("doneOn", "future"));
        else if (on < today.AddYears(-5)) errors.Add(("doneOn", "too_old"));
        if (errors.Count > 0) throw new ChronicCareValidationException(errors);
        var row = new CareCheck
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = patientId, FacilityId = facilityId, Kind = command.Kind!.Value, DoneOn = command.DoneOn!.Value,
            RecordedBy = UserId, RecordedAt = clock.GetUtcNow(),
        };
        await SaveAsync(CheckAction, patientId, () => db.CareChecks.Add(row), cancellationToken);
    }

    public async Task<CareGoalView> AddGoalAsync(Guid patientId, Guid facilityId, AddCareGoal command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Decides, cancellationToken);
        var text = Blank(command.Text);
        var target = Blank(command.Target);
        var errors = new List<(string, string)>();
        if (text is null) errors.Add(("text", "required"));
        else if (text.Length > CareGoal.MaxText) errors.Add(("text", "too_long"));
        if (target?.Length > CareGoal.MaxTarget) errors.Add(("target", "too_long"));
        if (command.ProgressPercent is { } p && p is < 0 or > 100) errors.Add(("progressPercent", "out_of_range"));
        if (await db.CareGoals.CountAsync(g => g.PatientId == patientId, cancellationToken) >= IChronicCare.MaxGoals) errors.Add(("goal", "too_many"));
        if (errors.Count > 0) throw new ChronicCareValidationException(errors);
        var now = clock.GetUtcNow();
        var row = new CareGoal
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = patientId, Text = text!, Target = target, ProgressPercent = command.ProgressPercent ?? 0,
            CreatedBy = UserId, CreatedAt = now, UpdatedBy = UserId, UpdatedAt = now,
        };
        await SaveAsync(GoalAction, patientId, () => db.CareGoals.Add(row), cancellationToken);
        return Goal(row);
    }

    public async Task<CareGoalView?> UpdateGoalAsync(Guid patientId, Guid facilityId, Guid goalId, UpdateCareGoal command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Decides, cancellationToken);
        if (command.ProgressPercent is not { } p || p is < 0 or > 100) throw Invalid("progressPercent", "out_of_range");
        var row = await db.CareGoals.SingleOrDefaultAsync(g => g.Id == goalId && g.PatientId == patientId, cancellationToken);
        if (row is null) return null;
        await SaveAsync(GoalAction, patientId, () => { row.ProgressPercent = p; row.UpdatedBy = UserId; row.UpdatedAt = clock.GetUtcNow(); }, cancellationToken);
        return Goal(row);
    }

    async Task<Dictionary<ObservationCode, CareTarget>> CurrentTargetsAsync(Guid patientId, CancellationToken cancellationToken) =>
        await db.CareTargets.AsNoTracking().Where(t => t.PatientId == patientId).GroupBy(t => t.Code)
            .Select(g => g.OrderByDescending(t => t.SetAt).ThenByDescending(t => t.Id).First()).ToDictionaryAsync(t => t.Code, cancellationToken);

    static CareTargetView Target(ObservationCode code, CareTarget? set, Observation? latest)
    {
        var d = CareTargets.Defaults[code];
        var limit = set?.LimitHundredths ?? d.DefaultHundredths;
        return new(code, Observation.Units(code), d.Direction, limit / 100m, set is null, latest?.ValueHundredths / 100m, latest?.TakenAt,
            latest is null ? null : CareTargets.Met(d.Direction, latest.ValueHundredths, limit), set?.SetBy, set?.SetAt);
    }

    static CareGoalView Goal(CareGoal g) => new(g.Id, g.Text, g.Target, g.ProgressPercent, g.CreatedBy, g.CreatedAt, g.UpdatedBy, g.UpdatedAt);

    async Task<TimeZoneInfo> ZoneAsync(Guid facilityId, CancellationToken cancellationToken) =>
        TimeZoneInfo.FindSystemTimeZoneById((await facilities.FindAsync(facilityId, cancellationToken) ?? throw new InvalidOperationException("Facility not in this tenant.")).TimeZone);

    async Task SaveAsync(string action, Guid patientId, Action change, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        change();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(action, patientId), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    static ChronicCareValidationException Invalid(string field, string code) => new([(field, code)]);

    static string? Blank(string? value) => value?.Trim() is { Length: > 0 } t ? t : null;

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
    Guid UserId => user.UserId ?? throw new InvalidOperationException("No user.");

    AuditEvent Event(string action, Guid patientId) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action,
        ResourceType = "patient", ResourceId = patientId, PatientId = patientId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
    };
}

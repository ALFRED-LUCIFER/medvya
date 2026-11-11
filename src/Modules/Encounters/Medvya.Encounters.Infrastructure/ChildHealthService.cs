using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// Immunisations and milestones (MDV-P2-036). Clinical access first, then the work with its audit event (no vaccine or answer in it) in one
/// transaction. The schedule is the facility's country's (<see cref="ImmunisationSchedule"/>); "today" is the facility's day. Input is validated
/// here, never only in the screen, and nothing is overwritten: every record is a new row.
/// </summary>
public sealed class ChildHealthService(
    EncountersDbContext db, IClinicalAccess clinical, IStaffAccess staff, IFacilityDirectory facilities, IPatientDirectory patients, IStaffUsers users,
    ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IChildHealth
{
    public const string ReadAction = "childhealth.read", DoseAction = "immunisation.record", MilestoneAction = "milestone.answer", Resource = "patient";
    public const int MaxBatch = 50, MaxGivenBy = 100, MaxReason = 500;
    static readonly Access[] Treats = [Access.Read, Access.Limited, Access.Full, Access.Sign], Records = [Access.Full];

    public async Task<ChildHealthView> GetAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Treats, cancellationToken);
        var context = await ContextAsync(patientId, facilityId, cancellationToken);
        await audit.WriteAsync(Event(ReadAction, patientId), cancellationToken);
        return await ViewAsync(patientId, context, cancellationToken);
    }

    public async Task<IReadOnlyList<DueVaccine>> VaccinesDueAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        var view = await GetAsync(patientId, facilityId, cancellationToken);
        return [.. view.Bands.SelectMany(b => b.Doses).Where(d => d.State is DoseState.DueNow or DoseState.Overdue).OrderBy(d => d.DueFrom)
            .Select(d => new DueVaccine(d.Vaccine, d.Dose, d.State, d.DueFrom, d.DeferReason is not null))];
    }

    public async Task<ChildHealthView> RecordDoseAsync(Guid patientId, Guid facilityId, RecordDose command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.VitalsAllergiesEntry, Records, cancellationToken);
        var context = await ChildAsync(patientId, facilityId, cancellationToken);
        var errors = new List<(string, string)>();
        var vaccine = command.Vaccine?.Trim() ?? "";
        if (command.Dose is not { } dose || context.Schedule.BandOf(vaccine, dose) is null) errors.Add(("dose", "unknown"));
        var (batch, givenBy, reason) = (Blank(command.Batch), Blank(command.GivenBy), Blank(command.Reason));
        switch (command.Outcome)
        {
            case DoseOutcome.Given:
                if (command.GivenOn is not { } on) errors.Add(("givenOn", "required"));
                else if (on > context.Today) errors.Add(("givenOn", "future"));
                else if (on < context.Born) errors.Add(("givenOn", "before_birth"));
                if (batch?.Length > MaxBatch) errors.Add(("batch", "too_long"));
                if (givenBy?.Length > MaxGivenBy) errors.Add(("givenBy", "too_long"));
                if (reason is not null) errors.Add(("reason", "not_for_given"));
                break;
            case DoseOutcome.Deferred:
                if (reason is null) errors.Add(("reason", "required")); // a deferral without a reason is refused
                else if (reason.Length > MaxReason) errors.Add(("reason", "too_long"));
                if (command.GivenOn is not null || batch is not null || givenBy is not null) errors.Add(("givenOn", "not_for_deferred"));
                break;
            default:
                errors.Add(("outcome", "required"));
                break;
        }
        if (errors.Count > 0) throw new ChildHealthValidationException(errors);
        // shortcut: a check, then a write; two simultaneous "given" saves of one dose both land. The view shows the dose given either way; add a partial unique index if it happens.
        if (await db.Immunisations.AnyAsync(i => i.PatientId == patientId && i.Vaccine == vaccine && i.Dose == command.Dose && i.Outcome == DoseOutcome.Given, cancellationToken))
            throw new ChildHealthConflictException("already_given");

        await SaveAsync(DoseAction, patientId, () => db.Immunisations.Add(new Immunisation
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = patientId, FacilityId = facilityId, Vaccine = vaccine, Dose = command.Dose!.Value,
            Outcome = command.Outcome!.Value, GivenOn = command.GivenOn, Batch = batch, GivenBy = givenBy, DeferReason = reason, RecordedBy = Me, RecordedAt = clock.GetUtcNow(),
        }), cancellationToken);
        return await ViewAsync(patientId, context, cancellationToken);
    }

    public async Task<ChildHealthView> AnswerMilestoneAsync(Guid patientId, Guid facilityId, AnswerMilestone command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.VitalsAllergiesEntry, Records, cancellationToken);
        var context = await ChildAsync(patientId, facilityId, cancellationToken);
        var errors = new List<(string, string)>();
        // Only the band the child is in today: an answer for another age would be read as this age's.
        if (DevelopmentalMilestones.BandOn(context.Born, context.Today) is not { } band || band.Key != command.Band || !band.Milestones.Contains(command.Milestone ?? ""))
            errors.Add(("milestone", "unknown"));
        if (command.Answer is not { } answer || !Enum.IsDefined(answer)) errors.Add(("answer", "required"));
        if (command.AnsweredBy is not { } by || !Enum.IsDefined(by)) errors.Add(("answeredBy", "required"));
        if (errors.Count > 0) throw new ChildHealthValidationException(errors);

        await SaveAsync(MilestoneAction, patientId, () => db.MilestoneResponses.Add(new MilestoneResponse
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = patientId, FacilityId = facilityId, Band = command.Band!, Milestone = command.Milestone!,
            Answer = command.Answer!.Value, AnsweredBy = command.AnsweredBy!.Value, RecordedBy = Me, RecordedAt = clock.GetUtcNow(),
        }), cancellationToken);
        return await ViewAsync(patientId, context, cancellationToken);
    }

    sealed record Context(DateOnly Born, DateOnly Today, ImmunisationSchedule Schedule, TimeZoneInfo Zone)
    {
        // The child life stage by age (MDV-P2-032): a pregnant adolescent is still due her childhood doses, so a pregnancy does not hide them.
        public bool Child => LifeStages.Of(Born, Today, openPregnancy: false) == LifeStage.Child;
    }

    async Task<Context> ContextAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken)
    {
        var born = (await patients.SummariesAsync([patientId], cancellationToken)).GetValueOrDefault(patientId)?.DateOfBirth
            ?? throw new InvalidOperationException("The patient is not in the directory.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById((await facilities.FindAsync(facilityId, cancellationToken) ?? throw new InvalidOperationException("Facility not in this tenant.")).TimeZone);
        var country = await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country.");
        return new(born, DayIn(zone, clock.GetUtcNow()), ImmunisationSchedule.For(country), zone);
    }

    async Task<Context> ChildAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(patientId, facilityId, cancellationToken);
        return context.Child ? context : throw new ChildHealthConflictException("not_a_child");
    }

    async Task<ChildHealthView> ViewAsync(Guid patientId, Context c, CancellationToken cancellationToken)
    {
        var name = c.Schedule.Name();
        if (!c.Child) return new(false, name, ImmunisationSchedule.SignOff is not null, [], null);
        var doses = await db.Immunisations.AsNoTracking().Where(i => i.PatientId == patientId).OrderBy(i => i.RecordedAt).ThenBy(i => i.Id).ToListAsync(cancellationToken);
        var band = DevelopmentalMilestones.BandOn(c.Born, c.Today);
        var answers = band is null ? [] : await db.MilestoneResponses.AsNoTracking().Where(m => m.PatientId == patientId && m.Band == band.Key)
            .OrderBy(m => m.RecordedAt).ThenBy(m => m.Id).ToListAsync(cancellationToken);
        var names = await users.DisplayNamesAsync([.. doses.Select(d => d.RecordedBy).Concat(answers.Select(a => a.RecordedBy)).Distinct()], cancellationToken);

        // The latest record of a dose is its answer, except that a given one always wins (it cannot be un-given).
        var latest = doses.GroupBy(d => (d.Vaccine, d.Dose)).ToDictionary(g => g.Key, g => g.LastOrDefault(d => d.Outcome == DoseOutcome.Given) ?? g.Last());
        DoseView Dose(ScheduleBand b, ScheduledDose s)
        {
            var r = latest.GetValueOrDefault((s.Vaccine, s.Dose));
            var deferred = r?.Outcome == DoseOutcome.Deferred;
            return new(s.Vaccine, s.Dose, b.StateOn(c.Born, c.Today, r?.Outcome == DoseOutcome.Given), b.From.On(c.Born), b.Until.On(c.Born), r?.GivenOn, r?.Batch,
                r is null ? null : r.GivenBy ?? names.GetValueOrDefault(r.RecordedBy), r?.DeferReason, deferred ? DayIn(c.Zone, r!.RecordedAt) : null);
        }
        var lastAnswer = answers.GroupBy(a => a.Milestone).ToDictionary(g => g.Key, g => g.Last());
        MilestoneView Milestone(string key) => lastAnswer.GetValueOrDefault(key) is { } a
            ? new(key, a.Answer, a.AnsweredBy, names.GetValueOrDefault(a.RecordedBy), a.RecordedAt) : new(key, null, null, null, null);

        return new(true, name, ImmunisationSchedule.SignOff is not null, [.. c.Schedule.Bands().Select(b => new ScheduleBandView(b.Key, [.. b.Doses.Select(s => Dose(b, s))]))],
            band is null ? null : new(band.Key, [.. band.Milestones.Select(Milestone)], DevelopmentalMilestones.SignOff is not null));
    }

    /// <summary>The facility's calendar day at an instant: due and overdue turn over at the clinic's midnight, not UTC's.</summary>
    public static DateOnly DayIn(TimeZoneInfo zone, DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    static string? Blank(string? value) => value?.Trim() is { Length: > 0 } t ? t : null;

    async Task SaveAsync(string action, Guid patientId, Action change, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        change();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(action, patientId), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    AuditEvent Event(string action, Guid patientId) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = patientId,
        PatientId = patientId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
    };

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
    Guid Me => user.UserId ?? throw new InvalidOperationException("No user.");
}

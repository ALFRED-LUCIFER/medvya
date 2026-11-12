using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
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
    public const string GrowthReadAction = "growth.read", GrowthAction = "growth.record";
    public const int MaxBatch = 50, MaxGivenBy = 100, MaxReason = 500;
    /// <summary>Head circumference bounds that catch a typing slip (a value in mm or inches), not a clinical range.</summary>
    public const decimal MinHeadCm = 20, MaxHeadCm = 70;
    static readonly ObservationCode[] GrowthCodes = [ObservationCode.Weight, ObservationCode.Height, ObservationCode.Length, ObservationCode.HeadCircumference];
    const double DaysPerMonth = 30.4375; // WHO's month, for the centile lines' monthly points
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

    public async Task<GrowthView> GrowthAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Treats, cancellationToken);
        var context = await ContextAsync(patientId, facilityId, cancellationToken);
        await audit.WriteAsync(Event(GrowthReadAction, patientId), cancellationToken);
        return await GrowthViewAsync(patientId, context, cancellationToken);
    }

    public async Task<GrowthView> RecordGrowthAsync(Guid patientId, Guid facilityId, RecordGrowth command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.VitalsAllergiesEntry, Records, cancellationToken);
        var context = await ChildAsync(patientId, facilityId, cancellationToken);
        var errors = new List<(string, string)>();
        if (command.TakenOn is not { } on) errors.Add(("takenOn", "required"));
        else if (on > context.Today) errors.Add(("takenOn", "future"));
        else if (on < context.Born) errors.Add(("takenOn", "before_birth"));
        if (command is { WeightKg: null, LengthCm: null, HeadCm: null }) errors.Add(("weightKg", "required"));
        if (command.WeightKg is { } w && !VitalRules.IsPossible(VitalKind.Weight, w)) errors.Add(("weightKg", "out_of_range"));
        if (command.LengthCm is { } l && !VitalRules.IsPossible(VitalKind.Height, l)) errors.Add(("lengthCm", "out_of_range"));
        // Lying or standing changes the reading by 0.7 cm against WHO's tables, so a length without its position is refused, never guessed.
        if (command.LengthCm is not null && (command.Position is not { } p || !Enum.IsDefined(p))) errors.Add(("position", "required"));
        if (command.HeadCm is { } h && (h < MinHeadCm || h > MaxHeadCm)) errors.Add(("headCm", "out_of_range"));
        if (errors.Count > 0) throw new ChildHealthValidationException(errors);

        // Today's measurement is now; an earlier day's is that day's noon at the clinic, so it stays on its day in the facility's zone.
        var at = command.TakenOn == context.Today ? clock.GetUtcNow()
            : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(command.TakenOn!.Value.ToDateTime(new TimeOnly(12, 0)), context.Zone), TimeSpan.Zero);
        Observation Reading(ObservationCode code, decimal value) => new()
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = patientId, Code = code, ValueHundredths = (int)Math.Round(value * 100, MidpointRounding.AwayFromZero),
            TakenAt = at, Source = ChartSource.ClinicianVerified, FacilityId = facilityId, RecordedBy = Me,
        };
        List<Observation> readings = [];
        if (command.WeightKg is { } kg) readings.Add(Reading(ObservationCode.Weight, kg));
        if (command.LengthCm is { } cm) readings.Add(Reading(command.Position == BodyPosition.Lying ? ObservationCode.Length : ObservationCode.Height, cm));
        if (command.HeadCm is { } head) readings.Add(Reading(ObservationCode.HeadCircumference, head));
        await SaveAsync(GrowthAction, patientId, () => db.Observations.AddRange(readings), cancellationToken);
        return await GrowthViewAsync(patientId, context, cancellationToken);
    }

    async Task<GrowthView> GrowthViewAsync(Guid patientId, Context c, CancellationToken cancellationToken)
    {
        var signedOff = WhoGrowthStandards.SignOff is not null;
        if (!c.Child) return new(false, signedOff, null, [], [], null);
        // A flagged intake reading the nurse did not keep is not final, as everywhere else in the chart.
        var rows = await db.Observations.AsNoTracking().Where(o => o.PatientId == patientId && GrowthCodes.Contains(o.Code) && (o.Flag == null || o.FlagKept))
            .OrderBy(o => o.TakenAt).ThenBy(o => o.Id).ToListAsync(cancellationToken);
        var days = rows.GroupBy(o => DayIn(c.Zone, o.TakenAt)).Select(g =>
        {
            decimal? Last(Func<Observation, bool> pick) => g.LastOrDefault(pick)?.ValueHundredths / 100m;
            var length = g.LastOrDefault(o => o.Code is ObservationCode.Height or ObservationCode.Length);
            // shortcut: nurse intake records "Height" without asking lying or standing; it is read as WHO's position for the age. Ask in intake if reviewers want it.
            BodyPosition? position = length is null ? null : length.Code == ObservationCode.Length ? BodyPosition.Lying : length.AppointmentId is null ? BodyPosition.Standing : null;
            return new GrowthMeasurementView(g.Key, g.Key.DayNumber - c.Born.DayNumber, Last(o => o.Code == ObservationCode.Weight), length?.ValueHundredths / 100m, position,
                Last(o => o.Code == ObservationCode.HeadCircumference));
        }).ToList();
        var weighed = days.Where(d => d.WeightKg is not null).ToList();
        var velocity = weighed.Count < 2 ? null
            : new WeightVelocityView(weighed[^1].WeightKg!.Value - weighed[^2].WeightKg!.Value, weighed[^1].AgeDays - weighed[^2].AgeDays, weighed[^2].TakenOn, weighed[^1].TakenOn);

        var noCentiles = c.Today.DayNumber - c.Born.DayNumber > WhoGrowthStandards.LastDay ? "over_five" : c.Sex == SexAtBirth.Other ? "sex_other" : null;
        if (noCentiles is not null) return new(true, signedOff, noCentiles, days, [], velocity);
        var male = c.Sex == SexAtBirth.Male;
        double? Plotted(GrowthIndicator indicator, GrowthMeasurementView m)
        {
            double? length = m.LengthCm is { } cm ? WhoGrowthStandards.LengthForTables((double)cm, m.AgeDays, m.Position) : null;
            return indicator switch
            {
                GrowthIndicator.WeightForAge => (double?)m.WeightKg,
                GrowthIndicator.LengthForAge => length,
                _ => m.WeightKg is { } kg && length is { } l ? (double)kg / Math.Pow(l / 100, 2) : null,
            };
        }
        GrowthSeriesView Series(GrowthIndicator indicator)
        {
            var points = days.Select(m => (m, value: Plotted(indicator, m))).Where(p => p.value is not null)
                .Select(p => (p.m, value: p.value!.Value, z: WhoGrowthStandards.ZScore(indicator, male, p.m.AgeDays, p.value!.Value))).ToList();
            var lines = WhoGrowthStandards.Lines.Select(line => new CentileLineView(line.Centile, [.. Enumerable.Range(0, 61).Select(month =>
            {
                var day = (int)Math.Round(month * DaysPerMonth);
                return new CentileLinePoint(day, Round(WhoGrowthStandards.ValueAt(indicator, male, day, line.Z)));
            })])).ToList();
            return new(indicator, [.. points.Select(p => new GrowthPointView(p.m.TakenOn, p.m.AgeDays, Round(p.value), Round(p.z), Math.Round((decimal)WhoGrowthStandards.Centile(p.z), 1)))],
                lines, WhoGrowthStandards.Flags([.. points.Select(p => p.z)]));
        }
        return new(true, signedOff, null, days, [.. Enum.GetValues<GrowthIndicator>().Select(Series)], velocity);
    }

    static decimal Round(double value) => Math.Round((decimal)value, 2, MidpointRounding.AwayFromZero);

    sealed record Context(DateOnly Born, DateOnly Today, ImmunisationSchedule Schedule, TimeZoneInfo Zone, SexAtBirth Sex)
    {
        // The child life stage by age (MDV-P2-032): a pregnant adolescent is still due her childhood doses, so a pregnancy does not hide them.
        public bool Child => LifeStages.Of(Born, Today, openPregnancy: false) == LifeStage.Child;
    }

    async Task<Context> ContextAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken)
    {
        var patient = (await patients.SummariesAsync([patientId], cancellationToken)).GetValueOrDefault(patientId)
            ?? throw new InvalidOperationException("The patient is not in the directory.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById((await facilities.FindAsync(facilityId, cancellationToken) ?? throw new InvalidOperationException("Facility not in this tenant.")).TimeZone);
        var country = await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country.");
        return new(patient.DateOfBirth, DayIn(zone, clock.GetUtcNow()), ImmunisationSchedule.For(country), zone, patient.SexAtBirth);
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

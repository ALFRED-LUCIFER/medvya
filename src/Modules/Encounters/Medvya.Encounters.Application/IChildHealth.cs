using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>Records a scheduled dose: given (date, optional batch and who gave it) or deferred (reason required).</summary>
public sealed record RecordDose(string? Vaccine, int? Dose, DoseOutcome? Outcome, DateOnly? GivenOn, string? Batch, string? GivenBy, string? Reason);

/// <summary>Answers one milestone of the child's current band.</summary>
public sealed record AnswerMilestone(string? Band, string? Milestone, MilestoneAnswer? Answer, MilestoneAnswerer? AnsweredBy);

/// <summary>
/// One scheduled dose on the chart's day. <see cref="By"/>: who gave or deferred it (the named giver, else the recorder's name). A deferral
/// keeps its reason and day, and the dose still turns due and overdue by the date of birth.
/// </summary>
public sealed record DoseView(
    string Vaccine, int Dose, DoseState State, DateOnly DueFrom, DateOnly OverdueFrom, DateOnly? GivenOn, string? Batch, string? By,
    string? DeferReason, DateOnly? DeferredOn);

public sealed record ScheduleBandView(string Key, IReadOnlyList<DoseView> Doses);

public sealed record MilestoneView(string Key, MilestoneAnswer? Answer, MilestoneAnswerer? AnsweredBy, string? RecordedBy, DateTimeOffset? RecordedAt);

public sealed record MilestoneBandView(string Key, IReadOnlyList<MilestoneView> Milestones, bool SignedOff);

/// <summary>
/// Immunisations and milestones of one patient. <see cref="Child"/> false: an adult, and nothing else is filled. <see cref="SignedOff"/> false:
/// the schedule awaits clinical review and the screen says so. <see cref="Milestones"/> null: no band covers the child's age.
/// </summary>
public sealed record ChildHealthView(bool Child, string Schedule, bool SignedOff, IReadOnlyList<ScheduleBandView> Bands, MilestoneBandView? Milestones);

/// <summary>A dose due now or overdue, for the care gaps (MDV-P2-038). <see cref="Deferred"/>: the latest record is a deferral.</summary>
public sealed record DueVaccine(string Vaccine, int Dose, DoseState State, DateOnly DueFrom, bool Deferred);

/// <summary>Records one growth measurement on a day: any of weight, length or height (with how it was measured) and head circumference.</summary>
public sealed record RecordGrowth(DateOnly? TakenOn, decimal? WeightKg, decimal? LengthCm, BodyPosition? Position, decimal? HeadCm);

/// <summary>One day's measurements, newest reading of each kind. <see cref="Position"/> null: from nurse intake, which does not ask.</summary>
public sealed record GrowthMeasurementView(DateOnly TakenOn, int AgeDays, decimal? WeightKg, decimal? LengthCm, BodyPosition? Position, decimal? HeadCm);

/// <summary>A point on a chart: the value as plotted (a length in WHO's position for the age), its z-score and centile.</summary>
public sealed record GrowthPointView(DateOnly TakenOn, int AgeDays, decimal Value, decimal Z, decimal Centile);

/// <summary>One centile line as (age in days, value) at each month from birth to 5 years.</summary>
public sealed record CentileLineView(int Centile, IReadOnlyList<CentileLinePoint> Points);

public sealed record CentileLinePoint(int AgeDays, decimal Value);

public sealed record GrowthSeriesView(GrowthIndicator Indicator, IReadOnlyList<GrowthPointView> Points, IReadOnlyList<CentileLineView> Lines, IReadOnlyList<GrowthAlert> Flags);

/// <summary>The change between the last two weights.</summary>
public sealed record WeightVelocityView(decimal ChangeKg, int Days, DateOnly From, DateOnly To);

/// <summary>
/// A child's growth (MDV-P2-035). <see cref="Child"/> false: an adult, nothing else filled. <see cref="NoCentiles"/>: why there are no series
/// ("over_five": WHO's 0–5 tables end at 5 years; "sex_not_recorded": the tables are by sex), else null. <see cref="SignedOff"/> false: the
/// tables and flags await clinical review and the screen says so.
/// </summary>
public sealed record GrowthView(
    bool Child, bool SignedOff, string? NoCentiles, IReadOnlyList<GrowthMeasurementView> Measurements, IReadOnlyList<GrowthSeriesView> Series, WeightVelocityView? Velocity);

public sealed class ChildHealthValidationException(IReadOnlyList<(string Field, string Code)> errors) : Exception("Child health input is invalid.")
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>"not_a_child" (the patient is 18 or over) or "already_given" (the dose is recorded as given; nothing is overwritten).</summary>
public sealed class ChildHealthConflictException(string code) : Exception("Child health conflict: " + code)
{
    public string Code { get; } = code;
}

/// <summary>
/// Immunisations against the country schedule and developmental milestones (MDV-P2-036, E-01). Reading needs clinical access to the patient at
/// the facility (Patient 360); recording needs vitals entry at Full (doctor or nurse). Every call writes its audit event with the patient. Due and
/// overdue are worked out from the date of birth on today in the facility's time zone.
/// </summary>
public interface IChildHealth
{
    Task<ChildHealthView> GetAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>The doses due now or overdue today, oldest due first; empty for an adult.</summary>
    Task<IReadOnlyList<DueVaccine>> VaccinesDueAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <exception cref="ChildHealthValidationException"/>
    /// <exception cref="ChildHealthConflictException"/>
    Task<ChildHealthView> RecordDoseAsync(Guid patientId, Guid facilityId, RecordDose command, CancellationToken cancellationToken = default);

    /// <exception cref="ChildHealthValidationException"/>
    /// <exception cref="ChildHealthConflictException"/>
    Task<ChildHealthView> AnswerMilestoneAsync(Guid patientId, Guid facilityId, AnswerMilestone command, CancellationToken cancellationToken = default);

    /// <summary>Growth measurements from nurse intake and the chart with WHO centiles from birth to 5 years (MDV-P2-035).</summary>
    Task<GrowthView> GrowthAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <exception cref="ChildHealthValidationException"/>
    /// <exception cref="ChildHealthConflictException"/>
    Task<GrowthView> RecordGrowthAsync(Guid patientId, Guid facilityId, RecordGrowth command, CancellationToken cancellationToken = default);
}

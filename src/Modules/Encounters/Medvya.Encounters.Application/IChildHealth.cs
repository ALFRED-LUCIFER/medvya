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
}

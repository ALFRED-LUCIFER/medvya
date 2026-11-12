namespace Medvya.Encounters.Application;

/// <summary>An older adult review as entered: every score optional, Timed Up and Go in seconds to one decimal (14.2).</summary>
public sealed record RecordGeriatricAssessment(
    int? FallsLast12Months, decimal? TimedUpAndGoSeconds, int? LyingSystolic, int? LyingDiastolic, int? StandingSystolic, int? StandingDiastolic,
    int? ClinicalFrailtyScale, int? MiniCog, int? Adl, int? Iadl, int? Phq2, string? NutritionNote);

/// <summary>
/// One recorded review with what it works out: the standing drop and whether it is orthostatic, and whether the walking test is at the risk line
/// (null when not measured). <see cref="Author"/> is filled by the endpoint from <see cref="RecordedBy"/>.
/// </summary>
public sealed record GeriatricAssessmentView(
    Guid Id, DateTimeOffset AssessedAt, Guid RecordedBy, int? FallsLast12Months, decimal? TimedUpAndGoSeconds, bool? TugAtRisk,
    int? LyingSystolic, int? LyingDiastolic, int? StandingSystolic, int? StandingDiastolic, int? SystolicDrop, int? DiastolicDrop, bool? Orthostatic,
    int? ClinicalFrailtyScale, int? MiniCog, int? Adl, int? Iadl, int? Phq2, string? NutritionNote, string? Author = null);

/// <summary>A score outside its scale, blood pressure without all four readings, an over-long note or nothing at all.</summary>
public sealed class GeriatricValidationException(IReadOnlyList<(string Field, string Code)> errors) : Exception("Invalid geriatric assessment.")
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>
/// The older adult review (MDV-P2-037) of one patient at the session's facility. Reading needs clinical access to the chart; recording needs
/// vitals entry (a nurse or a doctor). Both check the care relationship (a ClinicalAccessDeniedException) and audit with the patient.
/// </summary>
public interface IGeriatricAssessments
{
    /// <summary>The latest <see cref="MaxHistory"/> reviews, newest first.</summary>
    Task<IReadOnlyList<GeriatricAssessmentView>> ListAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <exception cref="GeriatricValidationException"/>
    Task<GeriatricAssessmentView> RecordAsync(Guid patientId, Guid facilityId, RecordGeriatricAssessment command, CancellationToken cancellationToken = default);

    public const int MaxHistory = 10;
}

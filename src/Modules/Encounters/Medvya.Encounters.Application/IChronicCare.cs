using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>
/// One target row of the chart: the measure, its limit (the doctor's or the default), the latest reading and whether it meets the limit
/// (null: no reading). <see cref="SetBy"/> is filled by the endpoint from <see cref="SetById"/>; both null on a default.
/// </summary>
public sealed record CareTargetView(
    ObservationCode Code, string Unit, TargetDirection Direction, decimal Limit, bool IsDefault, decimal? Latest, DateTimeOffset? LatestAt, bool? Met,
    Guid? SetById = null, DateTimeOffset? SetAt = null, string? SetBy = null);

public sealed record CareGoalView(Guid Id, string Text, string? Target, int ProgressPercent, Guid CreatedById, DateTimeOffset CreatedAt, Guid UpdatedById, DateTimeOffset UpdatedAt,
    string? CreatedBy = null, string? UpdatedBy = null);

/// <summary>
/// The patient's chronic care for the chart's right column (MDV-P2-038). <see cref="Chronic"/>: diabetes or hypertension on the active problem
/// list, or a target set for the patient (the targets card shows then). <see cref="RulesSignedOff"/> false: defaults and gap rules are a draft.
/// </summary>
public sealed record ChronicCareView(bool Chronic, IReadOnlyList<CareTargetView> Targets, IReadOnlyList<CareGap> Gaps, IReadOnlyList<CareGoalView> Goals, bool RulesSignedOff);

/// <summary>A target for one measure, as typed in the code's unit (7.5 for HbA1c 7.5 %). The measure travels here, not in the URL.</summary>
public sealed record SetCareTarget(ObservationCode? Code, decimal? Limit);

/// <summary>A diabetic eye or foot check done on a day (today or earlier).</summary>
public sealed record RecordCareCheck(CareCheckKind? Kind, DateOnly? DoneOn);

public sealed record AddCareGoal(string? Text, string? Target, int? ProgressPercent);

public sealed record UpdateCareGoal(int? ProgressPercent);

/// <summary>Input refused: field and code only, never the value.</summary>
public sealed class ChronicCareValidationException(IReadOnlyList<(string Field, string Code)> errors) : Exception("Invalid chronic care input.")
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>
/// Chronic care targets, care gaps and care-plan goals (MDV-P2-038) of one patient at the session's facility. Reading needs clinical access to
/// the chart; every write needs Patient 360 at Full (a doctor). Each checks the care relationship (a ClinicalAccessDeniedException) and audits
/// with the patient. A null answer from a goal update: no such goal of this patient.
/// </summary>
public interface IChronicCare
{
    Task<ChronicCareView> GetAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <exception cref="ChronicCareValidationException"/>
    Task<CareTargetView> SetTargetAsync(Guid patientId, Guid facilityId, SetCareTarget command, CancellationToken cancellationToken = default);

    /// <exception cref="ChronicCareValidationException"/>
    Task RecordCheckAsync(Guid patientId, Guid facilityId, RecordCareCheck command, CancellationToken cancellationToken = default);

    /// <exception cref="ChronicCareValidationException"/>
    Task<CareGoalView> AddGoalAsync(Guid patientId, Guid facilityId, AddCareGoal command, CancellationToken cancellationToken = default);

    /// <exception cref="ChronicCareValidationException"/>
    Task<CareGoalView?> UpdateGoalAsync(Guid patientId, Guid facilityId, Guid goalId, UpdateCareGoal command, CancellationToken cancellationToken = default);

    public const int MaxGoals = 20;
}

/// <summary>
/// The patient's placed investigation orders, for the "ordered but never resulted" gap. Implemented by Prescribing (it owns the orders), read by
/// Encounters through this contract. No access check or audit: the chronic care read has done both.
/// </summary>
public interface IPlacedOrders
{
    Task<IReadOnlyList<PlacedOrderFact>> SinceAsync(Guid patientId, DateTimeOffset since, CancellationToken cancellationToken = default);
}

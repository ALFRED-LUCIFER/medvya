using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>The tenant flag that turns the nurse step on: intake endpoints exist, and a visit leaves the nurse only by the nurse's handoff.</summary>
public static class IntakeFlag
{
    public const string Key = "clinical.nurse-intake";
}

public sealed record VitalReading(VitalKind? Kind, decimal? Value);

/// <summary>The nurse's save: the chief complaint in the patient's words and the readings taken so far. A blank stays blank (unknown, never guessed).</summary>
public sealed record RecordIntake(string? ChiefComplaint, IReadOnlyList<VitalReading>? Vitals);

public sealed record EscalateIntake(string? Reason);

/// <summary>The current reading of one kind, with its flag (a code the UI says in words), the previous one and whether the nurse kept a flagged value.</summary>
public sealed record VitalView(VitalKind Kind, decimal Value, VitalAlert? Flag, decimal? PreviousValue, DateTimeOffset? PreviousAt, bool FlagKept, DateTimeOffset RecordedAt);

/// <summary>
/// A visit's intake. <see cref="Blockers"/> are the stable codes that stop the handoff ("vitals_undecided"); empty when the nurse may hand off.
/// </summary>
public sealed record IntakeView(
    Guid AppointmentId, Guid PatientId, Guid FacilityId, string State, string? ChiefComplaint, IReadOnlyList<VitalView> Vitals,
    bool Escalated, bool HandedOff, IReadOnlyList<string> Blockers);

/// <summary>An open escalation for the doctor's Today (MDV-P2-002): ids and the nurse's reason only; the screen adds the patient's name.</summary>
public sealed record EscalationView(Guid Id, Guid AppointmentId, Guid PatientId, string Reason, DateTimeOffset EscalatedAt);

/// <summary>A reading or text the server refuses. Field/code pairs only, never the entered values.</summary>
public sealed class IntakeValidationException(IReadOnlyList<(string Field, string Code)> errors)
    : Exception("Intake input is invalid: " + string.Join(", ", errors.Select(e => $"{e.Field}:{e.Code}")))
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>The intake cannot do this now: "not_with_nurse" (the visit is not with the nurse) or "handoff_blocked" with the blocker codes.</summary>
public sealed class IntakeConflictException(string code, IReadOnlyList<string>? blockers = null) : Exception("Intake conflict: " + code)
{
    public string Code { get; } = code;
    public IReadOnlyList<string> Blockers { get; } = blockers ?? [];
}

/// <summary>
/// Nurse intake (MDV-P2-007). Every call needs clinical access to the patient for "Vitals and allergies entry" at Full (role, facility, care
/// relationship) and writes its audit event with the patient. Null results: no such visit in this tenant.
/// </summary>
public interface IIntake
{
    Task<IntakeView?> GetAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>Saves the readings (a changed value is a re-measure; the same value again changes nothing) and the chief complaint.</summary>
    /// <exception cref="IntakeValidationException"/>
    /// <exception cref="IntakeConflictException"/>
    Task<IntakeView?> RecordAsync(Guid appointmentId, RecordIntake command, CancellationToken cancellationToken = default);

    /// <summary>The nurse keeps a flagged reading on purpose, after looking at it again.</summary>
    Task<IntakeView?> KeepAsync(Guid appointmentId, VitalKind kind, CancellationToken cancellationToken = default);

    /// <exception cref="IntakeValidationException">"reason: required".</exception>
    Task<IntakeView?> EscalateAsync(Guid appointmentId, EscalateIntake command, CancellationToken cancellationToken = default);

    /// <summary>Hands the patient to the doctor: the visit moves to Ready for doctor. Refused while a blocker stands; repeating it changes nothing.</summary>
    /// <exception cref="IntakeConflictException"/>
    Task<IntakeView?> HandOffAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>Open escalations at the facility for patients the caller may treat (the treating doctor, under "assigned only").</summary>
    Task<IReadOnlyList<EscalationView>> EscalationsAsync(Guid facilityId, CancellationToken cancellationToken = default);
}

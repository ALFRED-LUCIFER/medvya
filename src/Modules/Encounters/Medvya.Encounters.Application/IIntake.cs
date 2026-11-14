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

/// <summary>
/// The current reading of one kind, with its flag (a code the UI says in words), the previous one and whether the nurse kept a flagged value.
/// UsualLow/UsualHigh: for a High or Low flag, the patient's usual range (at or above UsualHigh is high), so the range is said in words (MDV-P2-048).
/// Correction: this reading corrected the one before it after the handoff (MDV-P2-048).
/// </summary>
public sealed record IntakeVital(VitalKind Kind, decimal Value, VitalAlert? Flag, decimal? PreviousValue, DateTimeOffset? PreviousAt, bool FlagKept, DateTimeOffset RecordedAt,
    decimal? UsualLow = null, decimal? UsualHigh = null, VitalCorrection? Correction = null);

/// <summary>What a correction replaced: the earlier value with who recorded it and when, and the nurse's reason.</summary>
public sealed record VitalCorrection(decimal FromValue, Guid? FromBy, DateTimeOffset FromAt, string Reason);

/// <summary>A correction of one reading after the handoff, before the doctor starts: the right value and why (MDV-P2-048).</summary>
public sealed record CorrectVital(decimal? Value, string? Reason);

public sealed record IntakeAllergy(Guid Id, string Substance, string? Reaction, AllergyStatus? Status, ChartSource Source, DateTimeOffset? StatusSetAt);

public sealed record IntakeMedicine(Guid Id, string Name, string? Dose, string? Frequency, bool NeedsClarification, ChartSource Source, DateTimeOffset RecordedAt);

/// <summary>A new allergy with its status chosen by the nurse (a status is required: an allergy is never added "unset").</summary>
public sealed record AddAllergy(string? Substance, string? Reaction, AllergyStatus? Status);

public sealed record SetAllergyStatus(AllergyStatus? Status);

/// <summary>A medicine the patient says they take. <see cref="NeedsClarification"/>: the name could not be identified, so it is kept as said.</summary>
public sealed record AddMedicine(string? Name, string? Dose, string? Frequency, bool NeedsClarification);

/// <summary>
/// A visit's intake. <see cref="Blockers"/> are the stable codes that stop the handoff ("vitals_undecided", "allergy_status_required", "allergies_not_confirmed"); empty when the nurse may hand off.
/// </summary>
public sealed record IntakeView(
    Guid AppointmentId, Guid PatientId, Guid FacilityId, string State, string? ChiefComplaint, IReadOnlyList<IntakeVital> Vitals,
    IReadOnlyList<IntakeAllergy> Allergies, IReadOnlyList<IntakeMedicine> Medicines, bool NoKnownAllergiesConfirmed, bool Escalated, bool HandedOff, IReadOnlyList<string> Blockers);

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

    /// <summary>
    /// The nurse confirms once that every flagged reading still undecided was re-checked (MDV-P2-048): each is kept, as <see cref="KeepAsync"/> would,
    /// with an audit event per reading. A reading re-measured since keeps its own new decision.
    /// </summary>
    /// <exception cref="IntakeValidationException">"kind: not_flagged" when nothing is left to confirm.</exception>
    Task<IntakeView?> KeepAllAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects one reading after the handoff and before the doctor starts the consultation (MDV-P2-048). The earlier reading is kept; the new one
    /// carries the reason, who and when. After the doctor starts, readings are fixed.
    /// </summary>
    /// <exception cref="IntakeValidationException">value or reason missing or invalid; "kind: no_reading".</exception>
    /// <exception cref="IntakeConflictException">"consult_started" once the doctor has started; "not_handed_off" before the handoff (re-measure instead).</exception>
    Task<IntakeView?> CorrectAsync(Guid appointmentId, VitalKind kind, CorrectVital command, CancellationToken cancellationToken = default);

    /// <exception cref="IntakeValidationException">"reason: required".</exception>
    Task<IntakeView?> EscalateAsync(Guid appointmentId, EscalateIntake command, CancellationToken cancellationToken = default);

    /// <summary>Adds an allergy of the patient (source Nurse intake) with a status.</summary>
    /// <exception cref="IntakeValidationException"/>
    Task<IntakeView?> AddAllergyAsync(Guid appointmentId, AddAllergy command, CancellationToken cancellationToken = default);

    /// <summary>Sets the status of one of the visit patient's allergies. Null: no such visit, or the allergy is not this patient's.</summary>
    /// <exception cref="IntakeValidationException"/>
    Task<IntakeView?> SetAllergyStatusAsync(Guid appointmentId, Guid allergyId, SetAllergyStatus command, CancellationToken cancellationToken = default);

    /// <summary>The nurse confirms, on purpose, that the patient has no known allergies. Refused while the patient has any allergy on record.</summary>
    /// <exception cref="IntakeConflictException">"has_allergies".</exception>
    Task<IntakeView?> ConfirmNoKnownAllergiesAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>Records a medicine the patient says they take (patient-reported).</summary>
    /// <exception cref="IntakeValidationException"/>
    Task<IntakeView?> AddMedicineAsync(Guid appointmentId, AddMedicine command, CancellationToken cancellationToken = default);

    /// <summary>Hands the patient to the doctor: the visit moves to Ready for doctor. Refused while a blocker stands; repeating it changes nothing.</summary>
    /// <exception cref="IntakeConflictException"/>
    Task<IntakeView?> HandOffAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>Open escalations at the facility for patients the caller may treat (the treating doctor, under "assigned only").</summary>
    Task<IReadOnlyList<EscalationView>> EscalationsAsync(Guid facilityId, CancellationToken cancellationToken = default);
}

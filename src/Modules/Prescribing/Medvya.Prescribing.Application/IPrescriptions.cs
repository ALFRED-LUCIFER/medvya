using System.Text.Json.Serialization;

namespace Medvya.Prescribing.Application;

/// <summary>Prescribe: a medicine to take. Discontinue: a medicine the doctor stopped in this visit's medication reconciliation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RxLineKind>))]
public enum RxLineKind { Prescribe, Discontinue }

/// <summary>The line's source chip: added in the composer by the caller or by a colleague, or brought in from chart reconciliation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RxSource>))]
public enum RxSource { AddedByYou, AddedByColleague, ChartReconciliation }

/// <summary>
/// One line of the draft prescription. The dose fields are the structured sig (codes, blank where not given yet); <see cref="Sig"/> is the
/// readable instruction in every print language ("en", "hi", "ar"). <see cref="FreeText"/>: not from the formulary, so flagged for review and
/// left out of interaction checks. <see cref="Included"/> false: switched off, kept but not prescribed. A discontinue line has no id (it follows
/// the reconciliation) and carries the doctor's reason.
/// </summary>
public sealed record RxLineView(
    Guid? Id, RxLineKind Kind, RxSource Source, Guid? FormularyItemId, string Medicine, string? Strength, string? Form, bool FreeText, bool Included,
    string? Dose, string? DoseUnit, string? Route, string? Frequency, string? Timing, int? DurationDays, int? Quantity, string? Instructions,
    string? Reason, IReadOnlyDictionary<string, string> Sig);

/// <summary>A medicine added to the draft: a formulary medicine by its id, or, with no id, a free-text name (with optional strength and form).</summary>
public sealed record AddRxLine(Guid? FormularyItemId, string? Medicine, string? Strength, string? Form);

/// <summary>The dose builder's fields, saved as a whole on every autosave. Blank stays blank.</summary>
public sealed record UpdateRxLine(string? Dose, string? DoseUnit, string? Route, string? Frequency, string? Timing, int? DurationDays, int? Quantity, string? Instructions);

public sealed record IncludeRxLine(bool? Included);

/// <summary>Input refused: fields and codes only, never what was typed.</summary>
public sealed class PrescriptionValidationException(IReadOnlyList<(string Field, string Code)> errors)
    : Exception("Prescription input is invalid: " + string.Join(", ", errors.Select(e => $"{e.Field}:{e.Code}")))
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>The call is not possible now: "encounter_ended".</summary>
public sealed class PrescriptionConflictException(string code) : Exception("Prescription conflict: " + code)
{
    public string Code { get; } = code;
}

/// <summary>
/// The draft prescription of a consultation (MDV-P2-015). Every call checks clinical access to the encounter's patient (Prescriptions and
/// orders at Sign, care relationship, facility) and writes its audit event with the patient; writes do it in the transaction of the change.
/// Nothing here authorises, prints or shares a prescription (MDV-P2-017). Every answer is the whole draft. Null: no such encounter or line here.
/// </summary>
public interface IPrescriptions
{
    Task<IReadOnlyList<RxLineView>?> ListAsync(Guid encounterId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RxLineView>?> AddAsync(Guid encounterId, AddRxLine command, CancellationToken cancellationToken = default);

    /// <summary>Autosave of one line's dose builder.</summary>
    Task<IReadOnlyList<RxLineView>?> UpdateAsync(Guid itemId, UpdateRxLine command, CancellationToken cancellationToken = default);

    /// <summary>Switch a line off (kept on the record, not prescribed) or back on.</summary>
    Task<IReadOnlyList<RxLineView>?> IncludeAsync(Guid itemId, IncludeRxLine command, CancellationToken cancellationToken = default);
}

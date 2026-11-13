using System.Text.Json.Serialization;
using Medvya.Prescribing.Domain;

namespace Medvya.Prescribing.Application;

/// <summary>Prescribe: a medicine to take. Discontinue: a medicine the doctor stopped in this visit's medication reconciliation. Change and Taper
/// (MDV-P2-043): a new dose, or steps down to an end date, decided there; like Discontinue they follow the reconciliation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RxLineKind>))]
public enum RxLineKind { Prescribe, Discontinue, Change, Taper }

/// <summary>The line's source chip: added in the composer by the caller or by a colleague, or brought in from chart reconciliation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RxSource>))]
public enum RxSource { AddedByYou, AddedByColleague, ChartReconciliation }

/// <summary>
/// One line of the draft prescription. The dose fields are the structured sig (codes, blank where not given yet); <see cref="Sig"/> is the
/// readable instruction in every print language ("en", "hi", "ta", "ar"). <see cref="FreeText"/>: not from the formulary, so flagged for review and
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

/// <summary>The reason a severe alert was overridden, under the rule set version that raised it.</summary>
public sealed record RxOverrideView(string Key, string Reason, string RuleSetVersion, Guid OverriddenBy, DateTimeOffset OverriddenAt);

/// <summary>
/// The safety checks over the draft (MDV-P2-016): every alert in words with its evidence, what the rules could not check, and the rule set
/// (source and version) that answered. <see cref="Overrides"/> holds the current reason of each overridden alert. <see cref="Unresolved"/> counts
/// severe alerts with no override: authorisation (MDV-P2-017) runs the checks again and must refuse while it is above zero. <see cref="WeightDoses"/>:
/// a child's doses worked out from the weight (MDV-P2-040).
/// </summary>
public sealed record RxCheckView(
    string RuleSet, string RuleSetVersion, ChartAllergies Allergies, IReadOnlyList<SafetyAlert> Alerts, IReadOnlyList<NotChecked> NotChecked,
    IReadOnlyList<RxOverrideView> Overrides, int Unresolved, DateTimeOffset CheckedAt, IReadOnlyList<WeightDose> WeightDoses);

/// <summary>Go ahead despite a severe alert (its key from the latest check), with the doctor's reason.</summary>
public sealed record OverrideRxAlert(string? Key, string? Reason);

/// <summary>
/// The call is not possible now: "encounter_ended"; authorising: "nothing_to_authorise", "unresolved_alerts" (a severe alert has no reason);
/// printing: "not_authorised", "changed" (the draft changed since it was authorised: authorise it again first), "cancelled" (the doctor cancelled it).
/// </summary>
public sealed class PrescriptionConflictException(string code) : Exception("Prescription conflict: " + code)
{
    public string Code { get; } = code;
}

/// <summary>The latest authorisation of a consultation's prescription (MDV-P2-017). Ids only for the doctor; <see cref="Number"/> is printed on the paper.
/// <see cref="Cancellation"/>: the doctor cancelled this version without a replacement.</summary>
public sealed record RxAuthorisationView(int VersionNo, string Number, Guid AuthorisedBy, DateTimeOffset AuthorisedAt, string ContentHash, int Lines, RxCancellationView? Cancellation = null);

public sealed record RxCancellationView(string Reason, Guid CancelledBy, DateTimeOffset CancelledAt);

/// <summary>Cancel the authorised prescription without replacing it, with the doctor's reason.</summary>
public sealed record CancelRx(string? Reason);

/// <summary>Null <see cref="Authorised"/>: never authorised. <see cref="Changed"/>: the draft differs from what was authorised (printing waits for a new authorisation).</summary>
public sealed record RxAuthorisationState(RxAuthorisationView? Authorised, bool Changed);

/// <summary>One printed line: generic name, strength and form, the instruction in every print language, the print requirements (codes) of its class.</summary>
public sealed record RxPrintLine(bool Discontinue, string Medicine, string? Strength, string? Form, IReadOnlyDictionary<string, string> Sig, IReadOnlyList<string> PrintRequirements, string? Reason);

/// <summary>
/// The authorised prescription as the print view and the PDF show it (MDV-P2-017, E-10): clinic, doctor (qualifications, licence number and authority),
/// patient identifiers, the chart's allergies, the lines, and the verify link with its QR code (rows of '1' dark and '0' light, quiet zone included).
/// </summary>
public sealed record RxPrintView(
    RxAuthorisationView Authorisation, string Clinic, string TimeZone, string Doctor, string? Qualifications, string LicenceNumber, string LicenceAuthority,
    SlipPatient Patient, ChartAllergies Allergies, IReadOnlyList<string> AllergySubstances, IReadOnlyList<RxPrintLine> Lines, string VerifyUrl, IReadOnlyList<string> Qr,
    int LayoutVersion, RxAdviceView? Advice = null, int PatientAge = 0, IReadOnlyList<RxDiagnosis>? Diagnoses = null, DateOnly? FollowUpOn = null);

/// <summary>A diagnosis recorded in the consultation; a provisional one prints marked so, never as a confirmed diagnosis.</summary>
public sealed record RxDiagnosis(string Description, bool Provisional);

/// <summary>The advice for the patient (MDV-P2-043). Blank text is none.</summary>
public sealed record SetRxAdvice(string? Text);

/// <summary>
/// The advice as written (null: none) and the patient's preferred language. <see cref="InPatientLanguage"/> false: it prints and is sent in English
/// with a note, because no reviewed translation into that language exists (never a machine translation).
/// </summary>
public sealed record RxAdviceView(string? Text, string? PatientLanguage, bool InPatientLanguage);

/// <summary>The PDF of an authorised prescription as stored: the bytes and their SHA-256 (hex).</summary>
public sealed record RxPdf(byte[] Content, string Sha256);

/// <summary>Valid: the latest authorisation of its consultation. Replaced: a later authorisation took its place. Cancelled: the doctor withdrew it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RxValidity>))]
public enum RxValidity { Valid, Replaced, Cancelled }

/// <summary>What the QR verify page shows (MDV-P2-017): validity, the paper's number and date, who issued it, how many lines it has and the SHA-256 of the authorised lines. No patient data.</summary>
public sealed record RxVerifyView(RxValidity Status, string Number, int VersionNo, DateTimeOffset AuthorisedAt, string TimeZone, string Clinic, string Doctor, int Lines, string ContentHash);

/// <summary>The current print layout's version and when the signed-in doctor approved it as their own (null: not yet).</summary>
public sealed record RxLayoutView(int LayoutVersion, DateTimeOffset? ApprovedAt);

/// <summary>
/// The draft prescription of a consultation (MDV-P2-015). Every call checks clinical access to the encounter's patient (Prescriptions and
/// orders at Sign, care relationship, facility) and writes its audit event with the patient; writes do it in the transaction of the change.
/// Every draft answer is the whole draft. Authorising (MDV-P2-017) is a separate step from signing the note. Null: no such encounter or line here.
/// </summary>
public interface IPrescriptions
{
    Task<IReadOnlyList<RxLineView>?> ListAsync(Guid encounterId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RxLineView>?> AddAsync(Guid encounterId, AddRxLine command, CancellationToken cancellationToken = default);

    /// <summary>Autosave of one line's dose builder.</summary>
    Task<IReadOnlyList<RxLineView>?> UpdateAsync(Guid itemId, UpdateRxLine command, CancellationToken cancellationToken = default);

    /// <summary>Switch a line off (kept on the record, not prescribed) or back on.</summary>
    Task<IReadOnlyList<RxLineView>?> IncludeAsync(Guid itemId, IncludeRxLine command, CancellationToken cancellationToken = default);

    /// <summary>Runs the safety checks over the included lines and the chart (after every change, and again at authorisation); audited with the rule set version and counts.</summary>
    Task<RxCheckView?> CheckAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>Records the reason for going ahead despite a severe alert of the current check. Refused for an alert that is not current or not severe.</summary>
    Task<RxCheckView?> OverrideAsync(Guid encounterId, OverrideRxAlert command, CancellationToken cancellationToken = default);

    /// <summary>The latest authorisation and whether the draft changed since.</summary>
    Task<RxAuthorisationState?> AuthorisationAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorises the draft (MDV-P2-017): Sign on Prescriptions and orders, a care relationship, a re-authentication in the last 5 minutes, every
    /// included medicine with an instruction, and the safety checks run again with no severe alert left without a reason. Freezes the lines as the
    /// next version; the same draft again returns the current authorisation unchanged. Audited as rx.authorise.
    /// </summary>
    /// <exception cref="Exception">ReauthRequiredException (Encounters) when the re-authentication is missing or too old.</exception>
    /// <exception cref="PrescriptionConflictException">nothing_to_authorise, unresolved_alerts.</exception>
    /// <exception cref="PrescriptionValidationException">An included medicine has no instruction (lines: no_instructions).</exception>
    Task<RxAuthorisationState?> AuthoriseAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the latest authorised version without a replacement (MDV-P2-017): the same access and fresh re-authentication as authorising, and a reason.
    /// It no longer prints, opens by link or verifies as valid; cancelling it again returns the state unchanged. Audited as rx.cancel.
    /// </summary>
    /// <exception cref="Exception">ReauthRequiredException (Encounters) when the re-authentication is missing or too old.</exception>
    /// <exception cref="PrescriptionConflictException">not_authorised.</exception>
    /// <exception cref="PrescriptionValidationException">reason: required, too_long.</exception>
    Task<RxAuthorisationState?> CancelAsync(Guid encounterId, CancelRx command, CancellationToken cancellationToken = default);

    /// <summary>The consultation's advice for the patient (MDV-P2-043); audited as rx.advice.read.</summary>
    Task<RxAdviceView?> AdviceAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>Writes (or with blank text clears) the advice while the consultation is open; audited as rx.advice.set. A changed advice needs a new authorisation to print.</summary>
    /// <exception cref="PrescriptionValidationException">text: too_long.</exception>
    /// <exception cref="PrescriptionConflictException">encounter_ended.</exception>
    Task<RxAdviceView?> SetAdviceAsync(Guid encounterId, SetRxAdvice command, CancellationToken cancellationToken = default);

    /// <summary>The authorised prescription for the print view; audited as rx.print.</summary>
    /// <exception cref="PrescriptionConflictException">not_authorised, changed, cancelled.</exception>
    Task<RxPrintView?> PrintAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>Renders the authorised prescription as an English PDF on <paramref name="paper"/> (A4 or A5) and stores that exact copy with its hash; audited as rx.pdf.</summary>
    /// <exception cref="PrescriptionConflictException">not_authorised, changed.</exception>
    /// <exception cref="PrescriptionValidationException">paper: unknown.</exception>
    Task<RxPdf?> PdfAsync(Guid encounterId, string? paper, CancellationToken cancellationToken = default);

    /// <summary>The print layout version and the signed-in doctor's approval of it.</summary>
    Task<RxLayoutView> LayoutAsync(CancellationToken cancellationToken = default);

    /// <summary>Records the signed-in doctor's approval of the current print layout (exit gate 4); approving again keeps the first approval.</summary>
    Task<RxLayoutView> ApproveLayoutAsync(CancellationToken cancellationToken = default);

    /// <summary>Every share of the consultation's prescription, newest first; audited as rx.share.read.</summary>
    Task<IReadOnlyList<RxShareView>?> SharesAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shares the authorised, unchanged prescription (MDV-P2-020): renders and stores its English A4 PDF, makes an expiring link and sends it, with no
    /// clinical detail, to the patient (SMS or email) or to a named pharmacy (email; its dispatch starts as Sent). Opening the link needs a code sent to
    /// the patient's registered mobile, so the patient must have one. Audited as rx.share in the transaction; nothing is kept when the message cannot go.
    /// </summary>
    /// <exception cref="PrescriptionConflictException">not_authorised, changed, no_mobile, no_address, send_failed.</exception>
    /// <exception cref="PrescriptionValidationException">recipient, channel, pharmacyName, pharmacyEmail.</exception>
    Task<IReadOnlyList<RxShareView>?> ShareAsync(Guid encounterId, ShareRx command, CancellationToken cancellationToken = default);

    /// <summary>Records a pharmacy dispatch's next state (Dispensed with its time, or Cancelled) with the signed-in doctor as actor; audited as rx.dispatch.</summary>
    /// <exception cref="PrescriptionConflictException">dispatch_final: it is no longer Sent.</exception>
    /// <exception cref="PrescriptionValidationException">state, dispensedAt, pharmacyRef.</exception>
    Task<IReadOnlyList<RxShareView>?> DispatchAsync(Guid shareId, UpdateDispatch command, CancellationToken cancellationToken = default);
}

/// <summary>Share the prescription with the patient (channel Sms or Email) or with a named pharmacy (by email).</summary>
public sealed record ShareRx(RxShareRecipient? Recipient, RxShareChannel? Channel, string? PharmacyName, string? PharmacyEmail);

/// <summary>A pharmacy dispatch's current state and who recorded it.</summary>
public sealed record DispatchView(DispatchState State, string? PharmacyRef, DateTimeOffset? DispensedAt, Guid ChangedBy, DateTimeOffset ChangedAt);

/// <summary>One share as the doctor sees it, never the address. <see cref="Replaced"/>: a later authorisation replaced the version shared, so the link opens nothing.</summary>
public sealed record RxShareView(
    Guid Id, RxShareRecipient Recipient, RxShareChannel Channel, string? PharmacyName, int VersionNo, bool Replaced, Guid SharedBy, DateTimeOffset SharedAt,
    DateTimeOffset ExpiresAt, DispatchView? Dispatch);

public sealed record UpdateDispatch(DispatchState? State, string? PharmacyRef, DateTimeOffset? DispensedAt);

/// <summary>
/// The public side of a share link (MDV-P2-020), run inside the link's tenant. An unknown or expired link is null (the caller answers both alike);
/// every answer for a known link is audited with the patient and no actor.
/// </summary>
public interface IRxShareLinks
{
    /// <summary>Sends a new one-time code to the patient's registered mobile. False: no such live link.</summary>
    /// <exception cref="PrescriptionConflictException">replaced, cancelled, too_soon (a code went less than a minute ago), code_limit, no_address, send_failed.</exception>
    Task<bool> SendCodeAsync(byte[] linkHash, CancellationToken cancellationToken = default);

    /// <summary>The shared PDF, once the latest code matches; the code is then used up. Null: no such live link.</summary>
    /// <exception cref="PrescriptionConflictException">replaced, cancelled, no_code (none live: ask for a new one), wrong_code.</exception>
    Task<RxPdf?> OpenAsync(byte[] linkHash, string? code, CancellationToken cancellationToken = default);
}

/// <summary>The public QR verify check (MDV-P2-017): runs inside the token's tenant; unknown and malformed tokens are null alike. Audited with the patient.</summary>
public interface IPrescriptionVerification
{
    Task<RxVerifyView?> VerifyAsync(string verifyCode, CancellationToken cancellationToken = default);
}

using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>
/// An autosave. <see cref="BaseVersion"/> is the version the editor last saw (0 before the first save); a save on any other version is refused
/// so a second tab or device never overwrites silently. <see cref="Sections"/> is the whole note: a section left out is cleared, and so is a
/// <see cref="RecordingConsent"/> left out.
/// </summary>
public sealed record SaveNote(string? TemplateKey, bool Manual, int BaseVersion, IReadOnlyDictionary<string, string>? Sections, RecordingConsent? RecordingConsent = null);

/// <summary>The signature of a signed note: who, when and the hash of what was signed. The signer's name is the doctor profile's, else the account's display name (never blank); the attested licence (authority and number) is null without a profile.</summary>
public sealed record NoteSignature(int VersionNo, Guid SignedBy, DateTimeOffset SignedAt, string ContentHash, string? SignerName = null, string? SignerLicence = null);

/// <summary>A signed addendum (MDV-P2-013): version 2 and later, each following the version before it. Ids only for the signer.</summary>
public sealed record NoteAddendum(int VersionNo, int AmendsVersionNo, string Reason, string Text, Guid SignedBy, DateTimeOffset SignedAt, string ContentHash, string? SignerName = null);

/// <summary>
/// Version 0 and no sections: nothing saved yet (the default template is offered). <see cref="Signature"/> is set once signed; <see cref="Addenda"/>
/// are the signed addenda in version order (empty until one is added). <see cref="SignAsName"/> is the name the current user's signature will show (unsigned notes only).
/// <see cref="RecordingConsent"/> and <see cref="UndecidedAi"/> are what the review-and-sign checklist reads (MDV-P2-044); it is never stored.
/// <see cref="SuggestedTemplateKey"/> (MDV-P2-042): on a note with nothing saved, the template picked from the patient's age and problems; the note starts on it.
/// </summary>
public sealed record NoteView(
    Guid EncounterId, string TemplateKey, int TemplateVersion, NoteStatus Status, bool Manual, IReadOnlyDictionary<string, string> Sections, int Version, DateTimeOffset? UpdatedAt,
    NoteSignature? Signature = null, IReadOnlyList<NoteAddendum>? Addenda = null, string? SignAsName = null, RecordingConsent? RecordingConsent = null, IReadOnlyList<string>? UndecidedAi = null,
    string? SuggestedTemplateKey = null, ComplaintSourceView? ChiefComplaintSource = null);

/// <summary>
/// MDV-P1-104: the chief complaint came from nurse intake (and the doctor may have changed it since). <see cref="ByName"/> and <see cref="At"/>
/// are who saved it at intake and when; null when not known (intake saved before this was recorded, or the staff member can no longer be named).
/// </summary>
public sealed record ComplaintSourceView(ComplaintSource Source, string? ByName, DateTimeOffset? At);

/// <summary>The chief complaint the nurse saved at this visit's intake (MDV-P1-104), offered to a blank Chief complaint. Who and when as above.</summary>
public sealed record IntakeComplaint(string Text, string? ByName, DateTimeOffset? At);

/// <summary>What the note may take from this visit's nurse intake: verified readings for Objective, and the chief complaint (null: none saved).</summary>
public sealed record NotePrefill(IReadOnlyList<VerifiedVital> Vitals, IntakeComplaint? ChiefComplaint);

/// <summary>An addendum to a signed note: the text and why it is added. Both are required.</summary>
public sealed record AddNoteAddendum(string? Text, string? Reason);

/// <summary>The PDF of a signed note as stored: the bytes and their SHA-256 (hex).</summary>
public sealed record NotePdf(byte[] Content, string Sha256);

/// <summary>An addendum or a PDF needs a signed note (code note_not_signed): a draft is still edited in place.</summary>
public sealed class NoteNotSignedException() : Exception("The note is not signed.");

/// <summary>The note holds characters no embedded PDF font has, such as an emoji (code pdf_unsupported_text); the browser's print view shows them.</summary>
public sealed class NotePdfUnsupportedTextException() : Exception("The note has text the PDF cannot show.");

/// <summary>Sign the note as the editor last saw it (<see cref="BaseVersion"/>), so text typed in another tab is never signed unseen.</summary>
public sealed record SignNote(int BaseVersion);

/// <summary>The note is signed: it cannot be saved (code note_signed). A correction is an addendum.</summary>
public sealed class NoteSignedException() : Exception("The note is signed.");

/// <summary>Signing needs a re-authentication in the last 5 minutes (code reauth_required).</summary>
public sealed class ReauthRequiredException() : Exception("Re-authentication is required to sign.");

/// <summary>Field/code pairs only, never the entered text.</summary>
public sealed class NoteValidationException(IReadOnlyList<(string Field, string Code)> errors)
    : Exception("Note input is invalid: " + string.Join(", ", errors.Select(e => $"{e.Field}:{e.Code}")))
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>The note moved on since the editor read it. <see cref="Current"/> is the version to reload; the unsaved text stays with the editor.</summary>
public sealed class StaleNoteException(int current) : Exception("The note changed since it was read.")
{
    public int Current { get; } = current;
}

/// <summary>
/// One reading offered to the Objective section (MDV-P2-009): taken and verified by a nurse at this visit's intake. The source says who and when;
/// nothing is derived or filled in. <see cref="RecordedByName"/> is null when the staff member can no longer be named.
/// </summary>
public sealed record VerifiedVital(ObservationCode Code, decimal Value, string Unit, DateTimeOffset TakenAt, ChartSource Source, string? RecordedByName, VitalAlert? Flag);

/// <summary>A signed note of the same patient, offered as snippets (MDV-P2-009). Version 1 only: an addendum is not a note of its own.</summary>
public sealed record PreviousNote(Guid EncounterId, DateTimeOffset SignedAt, string? SignedByName, string TemplateKey, IReadOnlyDictionary<string, string> Sections);

/// <summary>
/// An encounter the signed-in doctor led whose note is not signed (MDV-P2-002, Today's pending notes), or whose consultation is still open
/// after the facility's day it started on ended (MDV audit UX-11: signed but never ended). Ids, times and flags only.
/// </summary>
public sealed record PendingNote(Guid EncounterId, Guid PatientId, DateTimeOffset StartedAt, bool Drafted, bool NoteSigned, bool OpenPastDay);

/// <summary>
/// Consult notes (MDV-P2-009). Clinical access to the encounter's patient at its facility for Patient 360 at Full (role, care relationship,
/// facility), and one audit event with the patient and no note text. Null: no such encounter in this tenant.
/// </summary>
public interface IConsultNotes
{
    Task<NoteView?> GetAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <exception cref="NoteValidationException"/>
    /// <exception cref="StaleNoteException"/>
    /// <exception cref="NoteSignedException"/>
    Task<NoteView?> SaveAsync(Guid encounterId, SaveNote command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs (MDV-P2-012): Sign on Clinical notes with a care relationship, a re-authentication in the last 5 minutes, every required section
    /// filled and no AI draft or suggestion undecided (MDV-P2-044). Freezes version 1. Signing a signed note again returns it unchanged.
    /// </summary>
    /// <exception cref="ReauthRequiredException"/>
    /// <exception cref="NoteValidationException">A required section is blank (code <c>required</c> on <c>sections.&lt;id&gt;</c>) or an AI draft is undecided (<c>undecided</c> on <c>undecidedAi</c>).</exception>
    /// <exception cref="StaleNoteException"/>
    Task<NoteView?> SignAsync(Guid encounterId, SignNote command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs an addendum (MDV-P2-013) as the next version of a signed note, under the same gate as signing (Sign on Clinical notes, care
    /// relationship, re-authentication in the last 5 minutes). Earlier versions are never touched.
    /// </summary>
    /// <exception cref="ReauthRequiredException"/>
    /// <exception cref="NoteNotSignedException"/>
    /// <exception cref="NoteValidationException">Blank or too long (codes <c>required</c>, <c>too_long</c> on <c>text</c> and <c>reason</c>).</exception>
    /// <exception cref="StaleNoteException">Another addendum was signed at the same moment.</exception>
    Task<NoteView?> AddAddendumAsync(Guid encounterId, AddNoteAddendum command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders the signed note with its addenda as an A4 PDF (MDV-P2-013) and stores that exact copy with its hash. Read access as for the note.
    /// </summary>
    /// <exception cref="NoteNotSignedException"/>
    /// <exception cref="NotePdfUnsupportedTextException"/>
    Task<NotePdf?> PdfAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The readings the doctor may add to Objective (MDV-P2-009): this visit's nurse-intake vitals, verified, and (when flagged) kept on purpose by
    /// the nurse; the latest of each kind. An empty list means nothing is verified: the section stays blank. Read access as for the note, audited
    /// as <c>note.prefill</c> with the patient and no values. With it, the chief complaint the nurse saved at this visit's intake (MDV-P1-104).
    /// </summary>
    Task<NotePrefill?> PrefillAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signed notes of the same patient, newest first, at most <see cref="MaxPrevious"/>, for the doctor to copy from (MDV-P2-009). Read access as for
    /// the note, one <c>note.previous</c> audit row per read with the patient and no note text.
    /// </summary>
    Task<IReadOnlyList<PreviousNote>?> PreviousAsync(Guid encounterId, CancellationToken cancellationToken = default);

    public const int MaxPrevious = 5;

    /// <summary>
    /// Today's pending notes (MDV-P2-002): encounters at this facility the signed-in clinician led whose note is not signed, or that are still
    /// open from an earlier facility day (MDV audit UX-11), oldest first, at most <see cref="MaxPending"/>. Only patients the clinician may still treat; one audit event per encounter answered.
    /// </summary>
    /// <returns>The notes, and whether more were left out by the cap.</returns>
    Task<(IReadOnlyList<PendingNote> Notes, bool More)> PendingAsync(Guid facilityId, CancellationToken cancellationToken = default);

    public const int MaxPending = 50;
}

using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>
/// Signed (MDV-P2-012) is final: no save, no edit; a correction is an addendum (MDV-P2-013, ADR-006).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NoteStatus>))]
#pragma warning disable CA1720 // "Signed" is the ticket's and the API's status word, not a type name
public enum NoteStatus { Draft, Signed }
#pragma warning restore CA1720

/// <summary>
/// What the doctor recorded about recording this visit (MDV-P2-044): the patient agreed, declined (the doctor types the note), or withdrew
/// consent while being listened to. Null on a note: not recorded yet, and the note cannot be signed.
/// shortcut: the doctor's word on the note only; the per-encounter consent receipt (who, script, people in the room) is MDV-P3-003.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecordingConsent>))]
public enum RecordingConsent { Agreed, Declined, Withdrawn }

/// <summary>
/// Where the note's chief complaint came from (MDV-P1-104): the nurse's intake text as saved, or that text since changed by the doctor.
/// No source (null): the doctor's own text.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ComplaintSource>))]
public enum ComplaintSource { NurseIntake, EditedByDoctor }

/// <summary>The source of the chief complaint after a save. The server decides it from the text, never from what the client claims.</summary>
public static class ComplaintSourceRule
{
    public const string Section = "chiefComplaint";

    /// <summary>
    /// Blank: no source. Unchanged: the source stays. Filled into a blank field with exactly the intake's text: from nurse intake. Any other change
    /// to text that came from intake: edited by the doctor. Anything else is the doctor's own. Remove a branch and ComplaintSourceRuleTests fails.
    /// </summary>
    public static ComplaintSource? Next(string? previous, ComplaintSource? source, string? text, string? intake) =>
        string.IsNullOrWhiteSpace(text) ? null
        : text == previous ? source
        : string.IsNullOrWhiteSpace(previous) && text == intake ? ComplaintSource.NurseIntake
        : source is null ? null : ComplaintSource.EditedByDoctor;
}

/// <summary>
/// CONSULT NOTE (MDV-P2-009): the one draft note of an encounter, written on a pinned template version. <see cref="Sections"/> are keyed by the
/// template's section ids; a blank is absent, never guessed. The patient is the encounter's, so a patient merge needs nothing here.
/// </summary>
public sealed class ConsultNote
{
    public const int MaxSectionLength = 10_000;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid EncounterId { get; init; }
    [Classification(DataClass.Internal)] public string TemplateKey { get; set; } = "";
    [Classification(DataClass.Internal)] public int TemplateVersion { get; set; }
    [Classification(DataClass.Internal)] public NoteStatus Status { get; set; }
    /// <summary>The doctor typed this note without a recording (consent declined or none made): the manual-note path.</summary>
    [Classification(DataClass.Internal)] public bool Manual { get; set; }
    [Classification(DataClass.SensitiveHealth)] public Dictionary<string, string> Sections { get; set; } = [];
    /// <summary>Null until the doctor records the patient's answer (MDV-P2-044); signing refuses a note without one.</summary>
    [Classification(DataClass.Internal)] public RecordingConsent? RecordingConsent { get; set; }
    /// <summary>
    /// Section ids that hold an AI draft or suggestion the doctor has not accepted or dismissed yet (MDV-P2-044); signing refuses while any is left.
    /// shortcut: nothing writes it before AI drafting (MDV-P3-011, MDV-P3-012 add and resolve entries); a save never changes it.
    /// </summary>
    [Classification(DataClass.Internal)] public string[] UndecidedAi { get; set; } = [];
    /// <summary>MDV-P1-104: where the chief complaint came from and, from intake, who saved it there and when.</summary>
    [Classification(DataClass.Internal)] public ComplaintSource? ChiefComplaintSource { get; set; }
    [Classification(DataClass.Internal)] public Guid? ChiefComplaintBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? ChiefComplaintAt { get; set; }
    /// <summary>Concurrency token and the ETag the editor sends back: 1 on the first save, +1 on every save.</summary>
    [Classification(DataClass.Internal)] public int Version { get; set; }
    [Classification(DataClass.Internal)] public Guid UpdatedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// SIGNED NOTE VERSION (MDV-P2-012, ADR-006): the frozen content of a note at signing, a hash-chained append-only ledger row (the database refuses
/// UPDATE and DELETE for every role). Version 1 is the signature; an addendum (MDV-P2-013) is a later version with its own signature, a reason and a
/// link to the version it follows (<see cref="AmendsVersionId"/>); its text is the one section <see cref="AddendumSection"/>. No version is ever edited.
/// </summary>
public sealed class SignedNoteVersion
{
    /// <summary>A signature needs a re-authentication no older than this (NFR: step-up within the last 5 minutes).</summary>
    public static readonly TimeSpan ReauthWindow = TimeSpan.FromMinutes(5);
    public const string AddendumSection = "addendum";
    public const int MaxReasonLength = 500;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid EncounterId { get; init; }
    [Classification(DataClass.Internal)] public Guid NoteId { get; init; }
    [Classification(DataClass.Internal)] public int VersionNo { get; init; }
    [Classification(DataClass.Internal)] public required string TemplateKey { get; init; }
    [Classification(DataClass.Internal)] public int TemplateVersion { get; init; }
    [Classification(DataClass.Internal)] public bool Manual { get; init; }
    [Classification(DataClass.SensitiveHealth)] public required Dictionary<string, string> Sections { get; init; }
    /// <summary>Hex SHA-256 of <see cref="Hash"/>'s canonical text, so the stored sections can be checked against it later.</summary>
    [Classification(DataClass.Internal)] public required string ContentHash { get; init; }
    [Classification(DataClass.Internal)] public Guid SignedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset SignedAt { get; init; }
    /// <summary>An addendum: the version it follows (the previous one), null on version 1.</summary>
    [Classification(DataClass.Internal)] public Guid? AmendsVersionId { get; init; }
    /// <summary>An addendum: why it was added, null on version 1.</summary>
    [Classification(DataClass.SensitiveHealth)] public string? Reason { get; init; }
    /// <summary>MDV-P1-104: the chief complaint's source as signed (version 1 only), covered by <see cref="ContentHash"/>.</summary>
    [Classification(DataClass.Internal)] public ComplaintSource? ChiefComplaintSource { get; init; }
    [Classification(DataClass.Internal)] public Guid? ChiefComplaintBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? ChiefComplaintAt { get; init; }
    /// <summary>MDV-P2-049: the visit record at signing (version 1 only), covered by <see cref="ContentHash"/>; null on an addendum and on notes signed before it.</summary>
    [Classification(DataClass.SensitiveHealth)] public NoteAtSigning? AtSigning { get; init; }

    /// <summary>
    /// Canonical text: template key and version, then the sections in key order (JSON escapes are fixed, so the bytes are stable); an addendum
    /// adds its amends link and reason, a chief complaint from intake its source (MDV-P1-104), and version 1 its record at signing (MDV-P2-049).
    /// These columns came after the ledger chain was set up, so the chain does not cover them: this hash does. A note without them hashes as before.
    /// </summary>
    public static string Hash(string templateKey, int templateVersion, IReadOnlyDictionary<string, string> sections, Guid? amendsVersionId = null, string? reason = null,
        (ComplaintSource Source, Guid? By, DateTimeOffset? At)? complaintSource = null, NoteAtSigning? atSigning = null) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{templateKey}@{templateVersion}|{JsonSerializer.Serialize(new SortedDictionary<string, string>(sections.ToDictionary(s => s.Key, s => s.Value), StringComparer.Ordinal))}"
            + (amendsVersionId is null ? "" : $"|amends:{amendsVersionId}|reason:{JsonSerializer.Serialize(reason)}")
            + (complaintSource is var (source, by, at) ? $"|complaintSource:{source}:{by}:{at?.ToUnixTimeMilliseconds()}" : "")
            + (atSigning is null ? "" : $"|atSigning:{JsonSerializer.Serialize(atSigning)}"))));
}

/// <summary>
/// MDV-P2-049: what the visit record held when the note was signed, kept with version 1 so the print and the PDF show it as it was, never a later
/// value. Vitals are the readings the note could take into Objective (this visit's nurse intake, verified; the latest of each kind). Allergies are
/// the chart's at that moment: none recorded (<see cref="AllergiesRecorded"/> false), no known allergies (true, empty list) or the list. Diagnoses
/// are the problems recorded in this consultation; medicines the lines of the prescriptions authorised in it before signing. Values only, no
/// derived wording; a later change is shown only by an addendum. Ints and strings only, so the JSON (and its hash) is the same after a round trip.
/// </summary>
public sealed record NoteAtSigning(
    [property: Classification(DataClass.SensitiveHealth)] IReadOnlyList<VitalAtSigning> Vitals,
    [property: Classification(DataClass.SensitiveHealth)] bool AllergiesRecorded,
    [property: Classification(DataClass.SensitiveHealth)] IReadOnlyList<AllergyAtSigning> Allergies,
    [property: Classification(DataClass.SensitiveHealth)] IReadOnlyList<DiagnosisAtSigning> Diagnoses,
    [property: Classification(DataClass.SensitiveHealth)] IReadOnlyList<MedicineAtSigning> Medicines);

/// <summary>One reading at signing, in hundredths of its fixed unit, with who took it (their name then; null when unknown) and when.</summary>
public sealed record VitalAtSigning(
    [property: Classification(DataClass.SensitiveHealth)] ObservationCode Code, [property: Classification(DataClass.SensitiveHealth)] int ValueHundredths,
    [property: Classification(DataClass.SensitiveHealth)] DateTimeOffset TakenAt, [property: Classification(DataClass.Internal)] ChartSource Source,
    [property: Classification(DataClass.Personal)] string? RecordedByName, [property: Classification(DataClass.Internal)] VitalAlert? Flag);

public sealed record AllergyAtSigning(
    [property: Classification(DataClass.SensitiveHealth)] string Substance, [property: Classification(DataClass.SensitiveHealth)] string? Reaction,
    [property: Classification(DataClass.SensitiveHealth)] AllergyStatus Status, [property: Classification(DataClass.Internal)] ChartSource Source);

public sealed record DiagnosisAtSigning(
    [property: Classification(DataClass.SensitiveHealth)] string? Icd10Code, [property: Classification(DataClass.SensitiveHealth)] string Description,
    [property: Classification(DataClass.SensitiveHealth)] ProblemVerification Verification, [property: Classification(DataClass.SensitiveHealth)] ProblemStatus Status,
    [property: Classification(DataClass.SensitiveHealth)] DateOnly? OnsetOn, [property: Classification(DataClass.Internal)] ChartSource Source);

/// <summary>A medicine line of a prescription authorised in this visit, by reference to that prescription version.</summary>
public sealed record MedicineAtSigning(
    [property: Classification(DataClass.SensitiveHealth)] string Name, [property: Classification(DataClass.SensitiveHealth)] string? Dose,
    [property: Classification(DataClass.Internal)] Guid PrescriptionId);

/// <summary>
/// SIGNED NOTE PDF (MDV-P2-013): every PDF of a signed note the server rendered, byte for byte with its SHA-256, in an append-only ledger table, so
/// what was handed out can be shown later. <see cref="VersionNo"/> is the newest version it shows.
/// shortcut: the bytes are kept in PostgreSQL (a text-only PDF is a few kilobytes); move them to Blob when the PDF carries images (logo, signature).
/// </summary>
public sealed class SignedNotePdf
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid NoteId { get; init; }
    [Classification(DataClass.Internal)] public int VersionNo { get; init; }
    [Classification(DataClass.SensitiveHealth)] public required byte[] Content { get; init; }
    [Classification(DataClass.Internal)] public required string Sha256 { get; init; }
    [Classification(DataClass.Internal)] public Guid RenderedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RenderedAt { get; init; }
}

/// <summary>
/// Transactional outbox of the Encounters module (MDV-P2-012): an event is written in the same transaction as the change it announces
/// (NoteSigned.v1). Payload is opaque ids and instants only, never note text or names.
/// shortcut: nothing subscribes yet, so there is no dispatcher and PublishedAt stays null; add one (as Scheduling's OutboxDispatcher) with the first subscriber.
/// </summary>
public sealed class OutboxEvent
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    /// <summary>Versioned event name, e.g. "NoteSigned.v1".</summary>
    [Classification(DataClass.Internal)] public required string Type { get; init; }
    [Classification(DataClass.Internal)] public Guid AggregateId { get; init; }
    [Classification(DataClass.Internal)] public required string Payload { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset OccurredAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? PublishedAt { get; set; }
}

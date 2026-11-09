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
    /// <summary>Concurrency token and the ETag the editor sends back: 1 on the first save, +1 on every save.</summary>
    [Classification(DataClass.Internal)] public int Version { get; set; }
    [Classification(DataClass.Internal)] public Guid UpdatedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// SIGNED NOTE VERSION (MDV-P2-012, ADR-006): the frozen content of a note at signing, a hash-chained append-only ledger row (the database refuses
/// UPDATE and DELETE for every role). Version 1 is the signature; addenda (MDV-P2-013) will add later versions with an amends link, not edit this row.
/// </summary>
public sealed class SignedNoteVersion
{
    /// <summary>A signature needs a re-authentication no older than this (NFR: step-up within the last 5 minutes).</summary>
    public static readonly TimeSpan ReauthWindow = TimeSpan.FromMinutes(5);

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

    /// <summary>Canonical text: template key and version, then the sections in key order (JSON escapes are fixed, so the bytes are stable).</summary>
    public static string Hash(string templateKey, int templateVersion, IReadOnlyDictionary<string, string> sections) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{templateKey}@{templateVersion}|{JsonSerializer.Serialize(new SortedDictionary<string, string>(sections.ToDictionary(s => s.Key, s => s.Value), StringComparer.Ordinal))}")));
}

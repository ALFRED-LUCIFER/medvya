using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>
/// Draft only today. MDV-P2-012 adds Signed (immutable, amended by addendum, ADR-006) and must refuse every save on a note that is not a draft.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NoteStatus>))]
public enum NoteStatus { Draft }

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

using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>One section of a note template. <see cref="Id"/> is also the i18n key part (noteTemplates.section.&lt;id&gt;); the server holds structure, never display text.</summary>
public sealed record NoteSection(
    [property: Classification(DataClass.Internal)] string Id, [property: Classification(DataClass.Internal)] bool Required);

/// <summary>A published template version. Immutable once released: a change is a new <see cref="Version"/> and notes keep the one they were written on (MDV-P2-025).</summary>
public sealed record NoteTemplate(
    [property: Classification(DataClass.Internal)] string Key, [property: Classification(DataClass.Internal)] int Version, [property: Classification(DataClass.Internal)] IReadOnlyList<NoteSection> Sections, [property: Classification(DataClass.Internal)] IReadOnlyList<string> QuickPhrases)
{
    /// <summary>Stable text of the structure, pinned by a test so a released version cannot be edited in place.</summary>
    [Classification(DataClass.Internal)] public string Fingerprint => $"{Key}@{Version}|{string.Join(',', Sections.Select(s => s.Id + (s.Required ? "!" : "")))}|{string.Join(',', QuickPhrases)}";
}

/// <summary>
/// The template catalogue: the six designed templates (F-04 step 5). Every version ever released stays in <see cref="History"/>.
/// shortcut: the catalogue is code, so a new version ships with a release; a clinic-editable template store when a pilot clinic needs its own wording.
/// </summary>
public static class NoteTemplates
{
    public const string Default = "general-opd";

    static NoteSection S(string id, bool required = false) => new(id, required);

    // Chief complaint, Subjective, the template's own sections, Objective, Assessment, Plan.
    static NoteSection[] Soap(params NoteSection[] extra) => [S("chiefComplaint", true), S("subjective"), .. extra, S("objective"), S("assessment", true), S("plan", true)];

    public static readonly IReadOnlyList<NoteTemplate> History =
    [
        new("general-opd", 1, Soap(), ["noRedFlags", "reviewIfWorse", "adviceFluidsRest"]),
        new("diabetes-review", 1, Soap(S("selfMonitoring"), S("footAndEyeCare")), ["adherenceDiscussed", "dietAdvice", "hba1cDue"]),
        new("hypertension", 1, Soap(S("homeBloodPressure")), ["saltAdvice", "adherenceDiscussed", "reviewBpIn2Weeks"]),
        new("paediatrics", 1, Soap(S("growthAndDevelopment"), S("immunisations")), ["feedingDiscussed", "immunisationsUpToDate", "guardianUnderstands"]),
        new("antenatal", 1, Soap(S("obstetricHistory"), S("fetalWellbeing")), ["fetalMovementsFelt", "supplementsContinued", "dangerSignsExplained"]),
        new("teleconsult", 1, Soap(S("remoteContext")), ["identityConfirmed", "limitsOfRemoteExam", "inPersonIfWorse"]),
    ];

    public static IReadOnlyList<string> Keys => [.. History.Select(t => t.Key).Distinct()];

    public static NoteTemplate? Latest(string key) => History.Where(t => t.Key == key).MaxBy(t => t.Version);

    public static NoteTemplate? Find(string key, int version) => History.FirstOrDefault(t => t.Key == key && t.Version == version);
}

/// <summary>The templates a doctor chose to work with (MDV-P2-025), one row per doctor. No row means General OPD only.</summary>
public sealed class DoctorNoteTemplate
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid UserId { get; init; }
    [Classification(DataClass.Internal)] public string[] TemplateKeys { get; set; } = [];
}

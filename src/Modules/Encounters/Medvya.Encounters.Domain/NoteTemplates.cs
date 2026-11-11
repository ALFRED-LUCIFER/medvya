using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>Free text, a review of systems (each symptom yes or no) or an examination by body system (MDV-P2-042); see <see cref="StructuredAnswers"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SectionKind>))]
public enum SectionKind { Text, ReviewOfSystems, Examination }

/// <summary>
/// One section of a note template. <see cref="Id"/> is also the i18n key part (noteTemplates.section.&lt;id&gt;); the server holds structure, never display text.
/// A structured section lists its <see cref="Items"/> (symptoms or body systems, i18n noteTemplates.item.&lt;id&gt;) in the order they are asked and printed.
/// </summary>
public sealed record NoteSection(
    [property: Classification(DataClass.Internal)] string Id, [property: Classification(DataClass.Internal)] bool Required,
    [property: Classification(DataClass.Internal)] SectionKind Kind = SectionKind.Text, [property: Classification(DataClass.Internal)] IReadOnlyList<string>? Items = null);

/// <summary>A published template version. Immutable once released: a change is a new <see cref="Version"/> and notes keep the one they were written on (MDV-P2-025).</summary>
public sealed record NoteTemplate(
    [property: Classification(DataClass.Internal)] string Key, [property: Classification(DataClass.Internal)] int Version, [property: Classification(DataClass.Internal)] IReadOnlyList<NoteSection> Sections, [property: Classification(DataClass.Internal)] IReadOnlyList<string> QuickPhrases)
{
    /// <summary>Stable text of the structure, pinned by a test so a released version cannot be edited in place.</summary>
    [Classification(DataClass.Internal)] public string Fingerprint => $"{Key}@{Version}|{string.Join(',', Sections.Select(Part))}|{string.Join(',', QuickPhrases)}";

    // A text section prints as before, so the fingerprints of the versions released before MDV-P2-042 are unchanged.
    static string Part(NoteSection s) => s.Id + (s.Required ? "!" : "") + (s.Kind == SectionKind.Text ? "" : $"[{s.Kind}:{string.Join(';', s.Items ?? [])}]");
}

/// <summary>
/// The template catalogue: the six designed templates (F-04 step 5) and the older-adult review (MDV-P2-042). Every version ever released stays in <see cref="History"/>.
/// shortcut: the catalogue is code, so a new version ships with a release; a clinic-editable template store when a pilot clinic needs its own wording.
/// </summary>
public static class NoteTemplates
{
    public const string Default = "general-opd";

    static NoteSection S(string id, bool required = false) => new(id, required);

    // Chief complaint, Subjective, the template's own sections, Objective, Assessment, Plan.
    static NoteSection[] Soap(params NoteSection[] extra) => [S("chiefComplaint", true), S("subjective"), .. extra, S("objective"), S("assessment", true), S("plan", true)];

    public const string ReviewOfSystems = "reviewOfSystems", Examination = "examination";

    // Version 2 (MDV-P2-042): the review of systems after the history, the template's own sections, the examination by system before Objective.
    // The symptom and system lists are the design's (Medvya Web.dc.html, studioVals ros and exam per patient).
    static NoteSection[] Structured(string[] symptoms, string[] systems, params NoteSection[] extra) =>
        [S("chiefComplaint", true), S("subjective"), new(ReviewOfSystems, false, SectionKind.ReviewOfSystems, symptoms), .. extra,
            new(Examination, false, SectionKind.Examination, systems), S("objective"), S("assessment", true), S("plan", true)];

    public static readonly IReadOnlyList<NoteTemplate> History =
    [
        new("general-opd", 1, Soap(), ["noRedFlags", "reviewIfWorse", "adviceFluidsRest"]),
        new("diabetes-review", 1, Soap(S("selfMonitoring"), S("footAndEyeCare")), ["adherenceDiscussed", "dietAdvice", "hba1cDue"]),
        new("hypertension", 1, Soap(S("homeBloodPressure")), ["saltAdvice", "adherenceDiscussed", "reviewBpIn2Weeks"]),
        new("paediatrics", 1, Soap(S("growthAndDevelopment"), S("immunisations")), ["feedingDiscussed", "immunisationsUpToDate", "guardianUnderstands"]),
        new("antenatal", 1, Soap(S("obstetricHistory"), S("fetalWellbeing")), ["fetalMovementsFelt", "supplementsContinued", "dangerSignsExplained"]),
        new("teleconsult", 1, Soap(S("remoteContext")), ["identityConfirmed", "limitsOfRemoteExam", "inPersonIfWorse"]),
        new("diabetes-review", 2, Structured(
            ["tiredness", "visionChange", "chestPain", "breathless", "abdominalPain", "nightUrination", "tinglingFeet", "footSores"],
            ["general", "heart", "lungs", "abdomen", "feet", "nervousSystem"], S("selfMonitoring"), S("footAndEyeCare")), ["adherenceDiscussed", "dietAdvice", "hba1cDue"]),
        new("paediatrics", 2, Structured(
            ["fever", "runnyNose", "coughWheeze", "earPain", "vomitingDiarrhoea", "lessUrine", "rash", "fitsOrDrowsy"],
            ["general", "ears", "throat", "lungs", "heart", "skin"], S("growthAndDevelopment"), S("immunisations")), ["feedingDiscussed", "immunisationsUpToDate", "guardianUnderstands"]),
        new("antenatal", 2, Structured(
            ["fever", "headacheOrVision", "palpitations", "breathless", "constipation", "burningUrine", "swellingFaceHands", "vaginalBleeding"],
            ["general", "heart", "lungs", "abdomen", "legs", "breast"], S("obstetricHistory"), S("fetalWellbeing")), ["fetalMovementsFelt", "supplementsContinued", "dangerSignsExplained"]),
        new("older-adult-review", 1, Structured(
            ["weightLoss", "dizzyOnStanding", "breathless", "indigestion", "nightUrination", "kneePain", "weakness", "lowMood"],
            ["general", "heart", "lungs", "joints", "nervousSystem", "feet"], S("standingBloodPressure"), S("walkingTest"), S("frailty"), S("homeSafety")),
            ["homeSafetyAdvice", "medicineReviewDone", "advanceCarePlanDiscussed"]),
    ];

    public static IReadOnlyList<string> Keys => [.. History.Select(t => t.Key).Distinct()];

    public static NoteTemplate? Latest(string key) => History.Where(t => t.Key == key).MaxBy(t => t.Version);

    public static NoteTemplate? Find(string key, int version) => History.FirstOrDefault(t => t.Key == key && t.Version == version);

    // shortcut: the age bands repeat LifeStages (MDV-P2-032, PR #1072) and pregnancy is an active O or Z33/Z34 problem until the pregnancy record
    // (MDV-P2-034); call LifeStages.Of once it is on main.
    public const int ChildUnderYears = 18, OlderAdultFromYears = 65;

    /// <summary>
    /// The template a new note starts on (MDV-P2-042): antenatal for a pregnancy, paediatrics for a child, the older-adult review from 65, diabetes
    /// review for an adult with active diabetes (E10–E14), else null (General OPD). Only a note with nothing saved yet gets it; the doctor's choice wins.
    /// </summary>
    public static string? Suggest(DateOnly dateOfBirth, DateOnly today, IEnumerable<string?> activeProblemCodes)
    {
        var codes = activeProblemCodes.OfType<string>().Select(c => c.Trim().ToUpperInvariant()).ToList();
        if (codes.Exists(c => c.StartsWith('O') || c.StartsWith("Z33", StringComparison.Ordinal) || c.StartsWith("Z34", StringComparison.Ordinal))) return "antenatal";
        if (dateOfBirth.AddYears(ChildUnderYears) > today) return "paediatrics";
        if (dateOfBirth.AddYears(OlderAdultFromYears) <= today) return "older-adult-review";
        return codes.Exists(c => c is ['E', '1', >= '0' and <= '4', ..]) ? "diabetes-review" : null;
    }
}

/// <summary>The templates a doctor chose to work with (MDV-P2-025), one row per doctor. No row means General OPD only.</summary>
public sealed class DoctorNoteTemplate
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid UserId { get; init; }
    [Classification(DataClass.Internal)] public string[] TemplateKeys { get; set; } = [];
}

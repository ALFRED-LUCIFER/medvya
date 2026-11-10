using System.Globalization;
using System.Text.RegularExpressions;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Prescribing.Domain;

/// <summary>
/// One medicine on a consultation's draft prescription (MDV-P2-015). Composed and autosaved while the consultation is open; it is a draft
/// only: authorisation (and print) is a separate step (MDV-P2-017), and allergy and interaction checks (MDV-P2-016) read the formulary link.
/// A line with no <see cref="FormularyItemId"/> is a free-text medicine: flagged for review and left out of interaction checks.
/// The dose fields are the structured sig; every one may stay blank while the doctor is still composing (unknowns are never filled in).
/// </summary>
public sealed class PrescriptionItem
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    /// <summary>Set while a patient merge holds this line on the surviving record: the record it came from.</summary>
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    /// <summary>Opaque id of the encounter in the Encounters module; a line belongs to exactly one.</summary>
    [Classification(DataClass.Internal)] public Guid EncounterId { get; init; }
    /// <summary>The formulary medicine chosen; null for a free-text medicine.</summary>
    [Classification(DataClass.Internal)] public Guid? FormularyItemId { get; init; }
    /// <summary>The generic name from the formulary, or the name as the doctor typed it.</summary>
    [Classification(DataClass.SensitiveHealth)] public string Medicine { get; init; } = "";
    [Classification(DataClass.SensitiveHealth)] public string? Strength { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Form { get; init; }
    /// <summary>A positive number as text ("1", "0.5"): stored as written, never as a floating-point value.</summary>
    [Classification(DataClass.SensitiveHealth)] public string? Dose { get; set; }
    [Classification(DataClass.SensitiveHealth)] public string? DoseUnit { get; set; }
    [Classification(DataClass.SensitiveHealth)] public string? Route { get; set; }
    [Classification(DataClass.SensitiveHealth)] public string? Frequency { get; set; }
    [Classification(DataClass.SensitiveHealth)] public string? Timing { get; set; }
    [Classification(DataClass.SensitiveHealth)] public int? DurationDays { get; set; }
    [Classification(DataClass.SensitiveHealth)] public int? Quantity { get; set; }
    /// <summary>The doctor's own words for the patient, printed as written (not translated).</summary>
    [Classification(DataClass.SensitiveHealth)] public string? Instructions { get; set; }
    /// <summary>Switched off by the doctor: kept on the record (nothing clinical is deleted) but not part of the prescription.</summary>
    [Classification(DataClass.Internal)] public bool Included { get; set; } = true;
    [Classification(DataClass.Internal)] public Guid CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The structured sig of a line: codes from <see cref="Sig"/>'s lists, blank where the doctor has not said.</summary>
public sealed record SigParts(
    [property: Classification(DataClass.SensitiveHealth)] string? Dose, [property: Classification(DataClass.SensitiveHealth)] string? DoseUnit,
    [property: Classification(DataClass.SensitiveHealth)] string? Route, [property: Classification(DataClass.SensitiveHealth)] string? Frequency,
    [property: Classification(DataClass.SensitiveHealth)] string? Timing, [property: Classification(DataClass.SensitiveHealth)] int? DurationDays,
    [property: Classification(DataClass.SensitiveHealth)] int? Quantity, [property: Classification(DataClass.SensitiveHealth)] string? Instructions);

/// <summary>
/// The dose builder's vocabulary and the human-readable instruction it makes in each print language (MDV-P2-015).
/// shortcut: the Hindi and Arabic wording is a draft (simple singular/plural only) and needs a clinical translator's sign-off before the printed Rx (MDV-P2-017); Tamil is added there with the print languages.
/// </summary>
public static class Sig
{
    public const int MaxInstructions = 200, MaxDuration = 365, MaxQuantity = 10_000;
    // Up to five ASCII digits and three decimals ("1", "0.5"); more than zero is checked apart.
    static readonly Regex DoseShape = new(@"^[0-9]{1,5}(\.[0-9]{1,3})?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public static readonly IReadOnlyList<string> Languages = ["en", "hi", "ar"];

    sealed record Words(
        IReadOnlyDictionary<string, (string One, string Many)> Units, IReadOnlyDictionary<string, string> Routes, IReadOnlyDictionary<string, string> Frequencies,
        IReadOnlyDictionary<string, string> Timings, string Take, string Use, (string One, string Many) Days, string Quantity, string Stop, Func<string, string, string> Sentence);

    static readonly Dictionary<string, Words> ByLanguage = new(StringComparer.Ordinal)
    {
        ["en"] = new(
            new Dictionary<string, (string, string)>
            {
                ["tablet"] = ("tablet", "tablets"), ["capsule"] = ("capsule", "capsules"), ["ml"] = ("ml", "ml"), ["mg"] = ("mg", "mg"), ["drop"] = ("drop", "drops"),
                ["puff"] = ("puff", "puffs"), ["unit"] = ("unit", "units"), ["sachet"] = ("sachet", "sachets"), ["application"] = ("application", "applications"),
            },
            new Dictionary<string, string>
            {
                ["oral"] = "by mouth", ["sublingual"] = "under the tongue", ["topical"] = "on the skin", ["inhaled"] = "by inhalation", ["eye"] = "in the eye",
                ["ear"] = "in the ear", ["nasal"] = "in the nose", ["rectal"] = "rectally", ["subcutaneous"] = "as an injection under the skin",
                ["intramuscular"] = "as an injection into the muscle", ["intravenous"] = "as an injection into a vein",
            },
            new Dictionary<string, string>
            {
                ["OD"] = "once a day", ["BD"] = "twice a day", ["TDS"] = "three times a day", ["QID"] = "four times a day", ["SOS"] = "when needed",
                ["STAT"] = "once, now", ["WEEKLY"] = "once a week",
            },
            new Dictionary<string, string>
            {
                ["before_food"] = "before food", ["after_food"] = "after food", ["with_food"] = "with food", ["empty_stomach"] = "on an empty stomach",
                ["morning"] = "in the morning", ["with_breakfast"] = "with breakfast", ["with_lunch"] = "with lunch", ["with_dinner"] = "with dinner", ["bedtime"] = "at bedtime",
            },
            "Take", "Use", ("for {0} day", "for {0} days"), "Quantity: {0}.", "Stop this medicine.", (verb, rest) => $"{verb} {rest}."),
        ["hi"] = new(
            new Dictionary<string, (string, string)>
            {
                ["tablet"] = ("गोली", "गोलियाँ"), ["capsule"] = ("कैप्सूल", "कैप्सूल"), ["ml"] = ("मि.ली.", "मि.ली."), ["mg"] = ("मि.ग्रा.", "मि.ग्रा."), ["drop"] = ("बूँद", "बूँदें"),
                ["puff"] = ("पफ़", "पफ़"), ["unit"] = ("यूनिट", "यूनिट"), ["sachet"] = ("पाउच", "पाउच"), ["application"] = ("बार", "बार"),
            },
            new Dictionary<string, string>
            {
                ["oral"] = "मुँह से", ["sublingual"] = "जीभ के नीचे", ["topical"] = "त्वचा पर", ["inhaled"] = "साँस से", ["eye"] = "आँख में", ["ear"] = "कान में",
                ["nasal"] = "नाक में", ["rectal"] = "गुदा मार्ग से", ["subcutaneous"] = "त्वचा के नीचे इंजेक्शन से", ["intramuscular"] = "मांसपेशी में इंजेक्शन से",
                ["intravenous"] = "नस में इंजेक्शन से",
            },
            new Dictionary<string, string>
            {
                ["OD"] = "दिन में एक बार", ["BD"] = "दिन में दो बार", ["TDS"] = "दिन में तीन बार", ["QID"] = "दिन में चार बार", ["SOS"] = "ज़रूरत होने पर",
                ["STAT"] = "अभी एक बार", ["WEEKLY"] = "हफ़्ते में एक बार",
            },
            new Dictionary<string, string>
            {
                ["before_food"] = "खाने से पहले", ["after_food"] = "खाने के बाद", ["with_food"] = "खाने के साथ", ["empty_stomach"] = "खाली पेट", ["morning"] = "सुबह",
                ["with_breakfast"] = "नाश्ते के साथ", ["with_lunch"] = "दोपहर के खाने के साथ", ["with_dinner"] = "रात के खाने के साथ", ["bedtime"] = "सोते समय",
            },
            "लें", "इस्तेमाल करें", ("{0} दिन तक", "{0} दिन तक"), "मात्रा: {0}।", "यह दवा बंद करें।", (verb, rest) => $"{rest} {verb}।"),
        ["ar"] = new(
            new Dictionary<string, (string, string)>
            {
                ["tablet"] = ("قرص", "أقراص"), ["capsule"] = ("كبسولة", "كبسولات"), ["ml"] = ("مل", "مل"), ["mg"] = ("ملغ", "ملغ"), ["drop"] = ("قطرة", "قطرات"),
                ["puff"] = ("بخة", "بخات"), ["unit"] = ("وحدة", "وحدات"), ["sachet"] = ("كيس", "أكياس"), ["application"] = ("مرة", "مرات"),
            },
            new Dictionary<string, string>
            {
                ["oral"] = "عن طريق الفم", ["sublingual"] = "تحت اللسان", ["topical"] = "على الجلد", ["inhaled"] = "بالاستنشاق", ["eye"] = "في العين", ["ear"] = "في الأذن",
                ["nasal"] = "في الأنف", ["rectal"] = "عن طريق الشرج", ["subcutaneous"] = "بالحقن تحت الجلد", ["intramuscular"] = "بالحقن في العضل", ["intravenous"] = "بالحقن في الوريد",
            },
            new Dictionary<string, string>
            {
                ["OD"] = "مرة واحدة يوميًا", ["BD"] = "مرتين يوميًا", ["TDS"] = "ثلاث مرات يوميًا", ["QID"] = "أربع مرات يوميًا", ["SOS"] = "عند الحاجة",
                ["STAT"] = "مرة واحدة الآن", ["WEEKLY"] = "مرة واحدة أسبوعيًا",
            },
            new Dictionary<string, string>
            {
                ["before_food"] = "قبل الأكل", ["after_food"] = "بعد الأكل", ["with_food"] = "مع الأكل", ["empty_stomach"] = "على معدة فارغة", ["morning"] = "في الصباح",
                ["with_breakfast"] = "مع الفطور", ["with_lunch"] = "مع الغداء", ["with_dinner"] = "مع العشاء", ["bedtime"] = "عند النوم",
            },
            "تناول", "استخدم", ("لمدة {0} يوم", "لمدة {0} أيام"), "الكمية: {0}.", "أوقف هذا الدواء.", (verb, rest) => $"{verb} {rest}."),
    };

    // English holds the code lists every language translates.
    static readonly Words En = ByLanguage["en"];

    /// <summary>Error codes for a structured sig the composer would refuse (empty: fine). Blank fields are allowed in a draft.</summary>
    public static IReadOnlyList<(string Field, string Code)> Validate(SigParts s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var errors = new List<(string, string)>();
        if (s.Dose is { } dose && (!DoseShape.IsMatch(dose) || !dose.Any(c => c is >= '1' and <= '9'))) errors.Add(("dose", "invalid"));
        if (s.DoseUnit is { } unit && !En.Units.ContainsKey(unit)) errors.Add(("doseUnit", "unknown"));
        if (s.Route is { } route && !En.Routes.ContainsKey(route)) errors.Add(("route", "unknown"));
        if (s.Frequency is { } frequency && !En.Frequencies.ContainsKey(frequency)) errors.Add(("frequency", "unknown"));
        if (s.Timing is { } timing && !En.Timings.ContainsKey(timing)) errors.Add(("timing", "unknown"));
        if (s.DurationDays is < 1 or > MaxDuration) errors.Add(("durationDays", "out_of_range"));
        if (s.Quantity is < 1 or > MaxQuantity) errors.Add(("quantity", "out_of_range"));
        if (s.Instructions?.Length > MaxInstructions) errors.Add(("instructions", "too_long"));
        return errors;
    }

    /// <summary>
    /// The patient-facing instruction in <paramref name="language"/>, built only from what the doctor gave (a blank field adds nothing), for
    /// example "Take 1 tablet by mouth twice a day after food for 5 days. Quantity: 10." Empty when nothing is given yet. Expects a valid sig.
    /// </summary>
    public static string Render(SigParts s, string language)
    {
        ArgumentNullException.ThrowIfNull(s);
        var w = ByLanguage[language];
        var parts = new List<string>();
        if (s.Dose is { } dose)
            parts.Add(s.DoseUnit is { } unit ? $"{dose} {(dose == "1" ? w.Units[unit].One : w.Units[unit].Many)}" : dose);
        else if (s.DoseUnit is { } unitOnly) parts.Add(w.Units[unitOnly].Many);
        if (s.Route is { } route) parts.Add(w.Routes[route]);
        if (s.Frequency is { } frequency) parts.Add(w.Frequencies[frequency]);
        if (s.Timing is { } timing) parts.Add(w.Timings[timing]);
        if (s.DurationDays is { } days) parts.Add(string.Format(CultureInfo.InvariantCulture, days == 1 ? w.Days.One : w.Days.Many, days));
        var text = new List<string>();
        if (parts.Count > 0) text.Add(w.Sentence(s.Route is null or "oral" or "sublingual" ? w.Take : w.Use, string.Join(' ', parts)));
        if (s.Quantity is { } quantity) text.Add(string.Format(CultureInfo.InvariantCulture, w.Quantity, quantity));
        if (s.Instructions is { } instructions) text.Add(instructions);
        return string.Join(' ', text);
    }

    /// <summary>The discontinue line's instruction in <paramref name="language"/>.</summary>
    public static string Stop(string language) => ByLanguage[language].Stop;
}

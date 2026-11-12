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
/// shortcut: the Hindi, Tamil and Arabic wording is a draft (simple singular/plural only) and needs a clinical translator's sign-off before a pilot clinic hands it to patients.
/// </summary>
public static class Sig
{
    public const int MaxInstructions = 200, MaxDuration = 365, MaxQuantity = 10_000;
    // Up to five ASCII digits and three decimals ("1", "0.5"); more than zero is checked apart.
    static readonly Regex DoseShape = new(@"^[0-9]{1,5}(\.[0-9]{1,3})?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public static readonly IReadOnlyList<string> Languages = ["en", "hi", "ta", "ar"];

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
                ["STAT"] = "once, now", ["WEEKLY"] = "once a week", ["MORNING"] = "once a day in the morning", ["NIGHT"] = "once a day at night",
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
                ["STAT"] = "अभी एक बार", ["WEEKLY"] = "हफ़्ते में एक बार", ["MORNING"] = "दिन में एक बार सुबह", ["NIGHT"] = "दिन में एक बार रात को",
            },
            new Dictionary<string, string>
            {
                ["before_food"] = "खाने से पहले", ["after_food"] = "खाने के बाद", ["with_food"] = "खाने के साथ", ["empty_stomach"] = "खाली पेट", ["morning"] = "सुबह",
                ["with_breakfast"] = "नाश्ते के साथ", ["with_lunch"] = "दोपहर के खाने के साथ", ["with_dinner"] = "रात के खाने के साथ", ["bedtime"] = "सोते समय",
            },
            "लें", "इस्तेमाल करें", ("{0} दिन तक", "{0} दिन तक"), "मात्रा: {0}।", "यह दवा बंद करें।", (verb, rest) => $"{rest} {verb}।"),
        ["ta"] = new(
            new Dictionary<string, (string, string)>
            {
                ["tablet"] = ("மாத்திரை", "மாத்திரைகள்"), ["capsule"] = ("கேப்ஸ்யூல்", "கேப்ஸ்யூல்கள்"), ["ml"] = ("மி.லி.", "மி.லி."), ["mg"] = ("மி.கி.", "மி.கி."),
                ["drop"] = ("சொட்டு", "சொட்டுகள்"), ["puff"] = ("பஃப்", "பஃப்கள்"), ["unit"] = ("யூனிட்", "யூனிட்கள்"), ["sachet"] = ("பாக்கெட்", "பாக்கெட்டுகள்"),
                ["application"] = ("முறை", "முறை"),
            },
            new Dictionary<string, string>
            {
                ["oral"] = "வாய் வழியாக", ["sublingual"] = "நாக்கின் அடியில்", ["topical"] = "தோலின் மேல்", ["inhaled"] = "உள்ளிழுத்து", ["eye"] = "கண்ணில்", ["ear"] = "காதில்",
                ["nasal"] = "மூக்கில்", ["rectal"] = "ஆசனவாய் வழியாக", ["subcutaneous"] = "தோலுக்கு அடியில் ஊசி மூலம்", ["intramuscular"] = "தசையில் ஊசி மூலம்",
                ["intravenous"] = "நரம்பில் ஊசி மூலம்",
            },
            new Dictionary<string, string>
            {
                ["OD"] = "தினமும் ஒரு முறை", ["BD"] = "தினமும் இரண்டு முறை", ["TDS"] = "தினமும் மூன்று முறை", ["QID"] = "தினமும் நான்கு முறை", ["SOS"] = "தேவைப்படும்போது",
                ["STAT"] = "இப்போது ஒரு முறை", ["WEEKLY"] = "வாரம் ஒரு முறை", ["MORNING"] = "தினமும் காலையில் ஒரு முறை", ["NIGHT"] = "தினமும் இரவில் ஒரு முறை",
            },
            new Dictionary<string, string>
            {
                ["before_food"] = "உணவுக்கு முன்", ["after_food"] = "உணவுக்குப் பின்", ["with_food"] = "உணவுடன்", ["empty_stomach"] = "வெறும் வயிற்றில்", ["morning"] = "காலையில்",
                ["with_breakfast"] = "காலை உணவுடன்", ["with_lunch"] = "மதிய உணவுடன்", ["with_dinner"] = "இரவு உணவுடன்", ["bedtime"] = "தூங்கும் முன்",
            },
            "எடுத்துக்கொள்ளவும்", "பயன்படுத்தவும்", ("{0} நாளுக்கு", "{0} நாட்களுக்கு"), "அளவு: {0}.", "இந்த மருந்தை நிறுத்தவும்.", (verb, rest) => $"{rest} {verb}."),
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
                ["STAT"] = "مرة واحدة الآن", ["WEEKLY"] = "مرة واحدة أسبوعيًا", ["MORNING"] = "مرة واحدة يوميًا صباحًا", ["NIGHT"] = "مرة واحدة يوميًا ليلًا",
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

    // MDV-P2-043: a change or taper from the reconciliation. The doses are the doctor's own words, printed as written in every language.
    static readonly Dictionary<string, (string ChangeFrom, string ChangeTo, string Taper, string Then)> Reconciled = new(StringComparer.Ordinal)
    {
        ["en"] = ("Change the dose from {0} to {1}.", "Change the dose to {0}.", "Reduce the dose step by step: {0}. Last dose on {1}.", ", then "),
        ["hi"] = ("खुराक {0} से बदलकर {1} करें।", "खुराक बदलकर {0} करें।", "खुराक धीरे-धीरे कम करें: {0}। आख़िरी खुराक {1} को।", ", फिर "),
        ["ta"] = ("அளவை {0} இலிருந்து {1} ஆக மாற்றவும்.", "அளவை {0} ஆக மாற்றவும்.", "அளவைப் படிப்படியாகக் குறைக்கவும்: {0}. கடைசி அளவு {1} அன்று.", ", பிறகு "),
        ["ar"] = ("غيّر الجرعة من {0} إلى {1}.", "غيّر الجرعة إلى {0}.", "خفّض الجرعة تدريجيًا: {0}. آخر جرعة في {1}.", "، ثم "),
    };

    /// <summary>A change line's instruction: "Change the dose from 500 mg to 1000 mg." (without "from" when the chart had no dose).</summary>
    public static string Change(string language, string? fromDose, string newDose) => string.IsNullOrEmpty(fromDose)
        ? string.Format(CultureInfo.InvariantCulture, Reconciled[language].ChangeTo, newDose)
        : string.Format(CultureInfo.InvariantCulture, Reconciled[language].ChangeFrom, fromDose, newDose);

    /// <summary>A taper line's instruction: "Reduce the dose step by step: 20 mg for 5 days, then 10 mg for 5 days. Last dose on 05 Dec 2026."</summary>
    public static string Taper(string language, IEnumerable<(string Dose, int Days)> steps, DateOnly endsOn)
    {
        var w = ByLanguage[language];
        var text = string.Join(Reconciled[language].Then, steps.Select(s => $"{s.Dose} {string.Format(CultureInfo.InvariantCulture, s.Days == 1 ? w.Days.One : w.Days.Many, s.Days)}"));
        return string.Format(CultureInfo.InvariantCulture, Reconciled[language].Taper, text, endsOn.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// The doctor's reason for going ahead despite a severe safety alert (MDV-P2-016), for one finding of one consultation's draft (its
/// <see cref="SafetyAlert.Key"/>) under the rule set version that raised it. Append-only: a new reason is a new row and the latest counts.
/// Authorisation (MDV-P2-017) refuses while a severe alert has none.
/// </summary>
public sealed class RxAlertOverride
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid EncounterId { get; init; }
    [Classification(DataClass.Internal)] public string AlertKey { get; init; } = "";
    [Classification(DataClass.SensitiveHealth)] public RxAlertKind Kind { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string Reason { get; init; } = "";
    [Classification(DataClass.Internal)] public string RuleSetVersion { get; init; } = "";
    [Classification(DataClass.Internal)] public Guid OverriddenBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset OverriddenAt { get; init; }
}

/// <summary>One line as authorised and printed: a frozen copy of the draft line (MDV-P2-017), with its instruction in every print language and the
/// print requirements of its regulatory class (codes, see <see cref="FormularyRules.PrintRequirements"/>). A discontinue line carries the reason.</summary>
public sealed record AuthorisedLine(
    [property: Classification(DataClass.SensitiveHealth)] bool Discontinue, [property: Classification(DataClass.SensitiveHealth)] string Medicine,
    [property: Classification(DataClass.SensitiveHealth)] string? Strength, [property: Classification(DataClass.SensitiveHealth)] string? Form,
    [property: Classification(DataClass.SensitiveHealth)] bool FreeText, [property: Classification(DataClass.SensitiveHealth)] IReadOnlyDictionary<string, string> Sig,
    [property: Classification(DataClass.SensitiveHealth)] IReadOnlyList<string> PrintRequirements, [property: Classification(DataClass.SensitiveHealth)] string? Reason);

/// <summary>
/// AUTHORISED PRESCRIPTION (MDV-P2-017): the doctor's authorisation of a consultation's prescription, separate from signing the note. A hash-chained
/// append-only ledger row (the database refuses UPDATE and DELETE for every role): an authorised prescription is never edited. Authorising a changed
/// draft adds the next version, which replaces the one before (the older one then verifies as replaced). Ids only for the patient: the encounter names them,
/// so a patient merge never has to touch this row. <see cref="VerifyCode"/> is the random half of the QR code's token (the other half is the tenant).
/// </summary>
public sealed class AuthorisedPrescription
{
    /// <summary>Authorising needs a re-authentication no older than this, as signing does (NFR: step-up within the last 5 minutes).</summary>
    public static readonly TimeSpan ReauthWindow = TimeSpan.FromMinutes(5);

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid EncounterId { get; init; }
    [Classification(DataClass.Internal)] public int VersionNo { get; init; }
    [Classification(DataClass.SensitiveHealth)] public required IReadOnlyList<AuthorisedLine> Lines { get; init; }
    /// <summary>SHA-256 (hex) of the lines' canonical JSON: the same draft authorised again is recognised, not stored twice.</summary>
    [Classification(DataClass.Internal)] public required string ContentHash { get; init; }
    [Classification(DataClass.Internal)] public required string RuleSetVersion { get; init; }
    [Classification(DataClass.Internal)] public required string VerifyCode { get; init; }
    [Classification(DataClass.Internal)] public Guid AuthorisedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset AuthorisedAt { get; init; }

    /// <summary>The advice for the patient as authorised with the lines (MDV-P2-043); null: none. Covered by <see cref="ContentHash"/>.</summary>
    [Classification(DataClass.SensitiveHealth)] public string? Advice { get; init; }

    /// <summary>Advice joins the hash only when there is some, so every authorisation made before it existed keeps its hash.</summary>
    public static string Hash(IReadOnlyList<AuthorisedLine> lines, string? advice = null)
    {
        byte[] json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            lines.Select(l => l with { Sig = new SortedDictionary<string, string>(l.Sig.ToDictionary(), StringComparer.Ordinal) }).ToList());
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(advice is null ? json : [.. json, .. System.Text.Encoding.UTF8.GetBytes("\nadvice:" + advice)]));
    }

    /// <summary>The number printed on the paper ("RX-" and eight letters of the id's random part): a reference to read out, not a credential.</summary>
    public string Number() => "RX-" + Id.ToString("N")[^8..].ToUpperInvariant();
}

/// <summary>
/// The advice for the patient in a consultation's plan (MDV-P2-043): written once, by the doctor, in English; one row per consultation, changed only
/// while it is open. Authorising freezes it with the prescription, which prints it and shares it. Ids only: the encounter names the patient.
/// </summary>
public sealed class RxAdvice
{
    public const int MaxText = 1000;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid EncounterId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string Text { get; set; } = "";
    [Classification(DataClass.Internal)] public Guid UpdatedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// The doctor's cancellation of an authorised prescription without a replacement (MDV-P2-017): a hash-chained append-only row, at most one per version.
/// A cancelled version never prints or opens by link, and verifies as cancelled; authorising the draft again adds the next version.
/// </summary>
public sealed class PrescriptionCancellation
{
    public const int MaxReason = 200;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PrescriptionId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public required string Reason { get; init; }
    [Classification(DataClass.Internal)] public Guid CancelledBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CancelledAt { get; init; }
}

/// <summary>PRESCRIPTION PDF (MDV-P2-017): every PDF of an authorised prescription the server rendered, byte for byte with its SHA-256, append-only.
/// shortcut: the bytes are kept in PostgreSQL (a text-only PDF is a few kilobytes); move them to Blob when the PDF carries images (logo, signature).</summary>
public sealed class PrescriptionPdf
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PrescriptionId { get; init; }
    [Classification(DataClass.Internal)] public required string Paper { get; init; }
    /// <summary>The bytes, only on rows made before the copies moved to Blob (MDV-P2-017); newer rows have <see cref="BlobKey"/> instead.</summary>
    [Classification(DataClass.SensitiveHealth)] public byte[]? Content { get; init; }
    /// <summary>The copy in the private clinical-documents container, written once. <see cref="Sha256"/> (hash-chained) proves the bytes on every read.</summary>
    [Classification(DataClass.Internal)] public string? BlobKey { get; init; }
    [Classification(DataClass.Internal)] public required string Sha256 { get; init; }
    [Classification(DataClass.Internal)] public Guid RenderedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RenderedAt { get; init; }
}

/// <summary>A doctor's approval of the printed prescription's layout version as their own (MDV-P2-017, exit gate 4). Append-only: the record of who approved what, when.</summary>
public sealed class RxLayoutApproval
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid UserId { get; init; }
    [Classification(DataClass.Internal)] public int LayoutVersion { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ApprovedAt { get; init; }
}

/// <summary>
/// The QR code's token (MDV-P2-017): the tenant id and 16 random bytes, base64url. The tenant half lets the public verify page look the code up inside
/// that tenant (row-level security stays on); the random half is <see cref="AuthorisedPrescription.VerifyCode"/>. It reveals no patient data.
/// </summary>
public static class RxVerifyToken
{
    const int TenantBytes = 16, RandomBytes = 16, Length = TenantBytes + RandomBytes;

    /// <summary>A new random code (stored) for the tenant.</summary>
    public static string NewCode() => System.Buffers.Text.Base64Url.EncodeToString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(RandomBytes));

    public static string Token(Guid tenantId, string code)
    {
        var bytes = new byte[Length];
        tenantId.TryWriteBytes(bytes);
        System.Buffers.Text.Base64Url.DecodeFromChars(code, bytes.AsSpan(TenantBytes));
        return System.Buffers.Text.Base64Url.EncodeToString(bytes);
    }

    /// <summary>The tenant and code of a token, or null when it is not a well-formed token.</summary>
    public static (Guid TenantId, string Code)? Read(string? token)
    {
        if (token is null || token.Length > 64 || !System.Buffers.Text.Base64Url.IsValid(token, out var length) || length != Length) return null;
        Span<byte> bytes = stackalloc byte[Length];
        if (!System.Buffers.Text.Base64Url.TryDecodeFromChars(token, bytes, out var written) || written != Length) return null;
        return (new Guid(bytes[..TenantBytes]), System.Buffers.Text.Base64Url.EncodeToString(bytes[TenantBytes..]));
    }
}

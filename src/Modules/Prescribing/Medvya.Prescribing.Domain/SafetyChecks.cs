using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Prescribing.Domain;

/// <summary>Severe: authorising needs an override reason (MDV-P2-017 refuses without one). Moderate: shown, no reason needed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RxSeverity>))]
public enum RxSeverity { Severe, Moderate }

[JsonConverter(typeof(JsonStringEnumConverter<RxAlertKind>))]
public enum RxAlertKind { Allergy, CrossSensitivity, Interaction, Duplicate, DoseAboveMaximum, AllergiesNotRecorded }

/// <summary>The chart's allergy state as the checks read it: "not recorded" is never "no known allergies".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChartAllergies>))]
public enum ChartAllergies { NotRecorded, NoKnownAllergies, Listed }

/// <summary>
/// Why a medicine was not (fully) checked. FreeText: not from the formulary. NoIngredientCode: a formulary medicine without an ATC code.
/// NotOnFormulary: a chart medicine whose name matches no formulary medicine. DoseNotChecked: no maximum in the rule set, or the dose cannot be
/// added up to a day (missing fields, "when needed", weekly, or a unit such as ml).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotCheckedReason>))]
public enum NotCheckedReason { FreeText, NoIngredientCode, NotOnFormulary, DoseNotChecked }

/// <summary>A medicine to check: a prescription line (with its sig) or an active chart medicine (no sig). A null ATC code means not covered.</summary>
public sealed record CheckedMedicine(
    [property: Classification(DataClass.Internal)] Guid Id, [property: Classification(DataClass.SensitiveHealth)] string Name,
    [property: Classification(DataClass.Internal)] string? Atc, [property: Classification(DataClass.SensitiveHealth)] string? Strength,
    [property: Classification(DataClass.SensitiveHealth)] SigParts? Sig, [property: Classification(DataClass.Internal)] bool OnChart,
    [property: Classification(DataClass.Internal)] bool FreeText);

/// <summary>A listed allergy; <see cref="Status"/> is the chart's status code (Confirmed, PatientReported or Unknown), shown as the evidence.</summary>
public sealed record CheckedAllergy(
    [property: Classification(DataClass.Internal)] Guid Id, [property: Classification(DataClass.SensitiveHealth)] string Substance,
    [property: Classification(DataClass.SensitiveHealth)] string Status);

/// <summary>
/// One finding. <see cref="Key"/> names it stably (kind, the ids involved and, for a dose, the daily amount), so an override covers exactly
/// this finding and a changed dose needs a new one. <see cref="Medicine"/> is the prescribed line; <see cref="Other"/> the second medicine of a
/// pair (on the chart when <see cref="OtherOnChart"/>). <see cref="Code"/>: the allergy class (or "ingredient"), the interaction's effect,
/// or "same_medicine" / "same_class" for a duplicate. Amounts are micrograms a day.
/// </summary>
public sealed record SafetyAlert(
    [property: Classification(DataClass.Internal)] string Key, [property: Classification(DataClass.SensitiveHealth)] RxAlertKind Kind,
    [property: Classification(DataClass.SensitiveHealth)] RxSeverity Severity, [property: Classification(DataClass.Internal)] Guid? ItemId,
    [property: Classification(DataClass.SensitiveHealth)] string? Medicine, [property: Classification(DataClass.SensitiveHealth)] string? Other,
    [property: Classification(DataClass.Internal)] bool OtherOnChart, [property: Classification(DataClass.SensitiveHealth)] string? Code,
    [property: Classification(DataClass.SensitiveHealth)] string? Substance, [property: Classification(DataClass.SensitiveHealth)] string? AllergyStatus,
    [property: Classification(DataClass.SensitiveHealth)] long? DailyMicrograms, [property: Classification(DataClass.Internal)] long? MaxDailyMicrograms);

public sealed record NotChecked(
    [property: Classification(DataClass.Internal)] Guid Id, [property: Classification(DataClass.SensitiveHealth)] string Medicine,
    [property: Classification(DataClass.Internal)] bool OnChart, [property: Classification(DataClass.Internal)] NotCheckedReason Reason);

public sealed record SafetyResult([property: Classification(DataClass.SensitiveHealth)] IReadOnlyList<SafetyAlert> Alerts, [property: Classification(DataClass.SensitiveHealth)] IReadOnlyList<NotChecked> NotChecked);

/// <summary>An allergy class: words that name it in an allergy entry, the ATC prefixes it covers, and the prefixes with a known cross-sensitivity.</summary>
public sealed record AllergyClass(
    [property: Classification(DataClass.Internal)] string Id, [property: Classification(DataClass.Internal)] IReadOnlyList<string> Terms,
    [property: Classification(DataClass.Internal)] IReadOnlyList<string> Atc, [property: Classification(DataClass.Internal)] IReadOnlyList<string> Cross);

/// <summary>An interacting pair of ATC prefixes, its severity and the effect (a code the screens translate).</summary>
public sealed record InteractionRule(
    [property: Classification(DataClass.Internal)] string Id, [property: Classification(DataClass.Internal)] IReadOnlyList<string> A,
    [property: Classification(DataClass.Internal)] IReadOnlyList<string> B, [property: Classification(DataClass.Internal)] RxSeverity Severity,
    [property: Classification(DataClass.Internal)] string Effect);

/// <summary>
/// The prescription safety checks (MDV-P2-016): allergy and allergy-class matches, cross-sensitivity, drug-drug interactions, duplicate therapy
/// and the adult maximum daily dose, over the included prescription lines and the chart's active medicines. Pure: the same input always gives
/// the same findings. Medicines are matched by ATC code, never by a guess at a name; what the rules do not cover is reported as not checked.
/// shortcut: a small curated rule set, written for the pilot and NOT a licensed drug-interaction database, and not yet signed off by a clinical
/// safety reviewer (needed before the R2 exit, MDV-P2-028). No age, weight or renal dose adjustment (the chart holds no eGFR). Replace the
/// tables with a licensed knowledge base (and bump <see cref="Version"/>) when its source and licence are chosen.
/// </summary>
public static class SafetyRules
{
    /// <summary>The rule set's name (a code the screens translate) and version, recorded with every check and override.</summary>
    public const string Source = "medvya-curated", Version = "2026.11-draft-1";

    public static readonly IReadOnlyList<AllergyClass> Classes =
    [
        new("penicillins", ["penicillin", "penicillins", "amoxicillin", "amoxycillin", "ampicillin", "amoxiclav", "cloxacillin", "flucloxacillin"], ["J01C"], ["J01DB", "J01DC", "J01DD", "J01DE", "J01DH"]),
        new("cephalosporins", ["cephalosporin", "cephalosporins", "cefalexin", "cephalexin", "cefuroxime", "ceftriaxone", "cefixime"], ["J01DB", "J01DC", "J01DD", "J01DE"], ["J01C", "J01DH"]),
        new("sulfonamides", ["sulfa", "sulpha", "sulfonamide", "sulfonamides", "sulphonamide", "sulfamethoxazole", "cotrimoxazole", "trimoxazole"], ["J01E"], []),
        new("fluoroquinolones", ["quinolone", "quinolones", "fluoroquinolone", "fluoroquinolones", "ciprofloxacin", "levofloxacin", "ofloxacin", "norfloxacin"], ["J01MA"], []),
        new("macrolides", ["macrolide", "macrolides", "azithromycin", "erythromycin", "clarithromycin"], ["J01FA"], []),
        new("nsaids", ["nsaid", "nsaids", "ibuprofen", "diclofenac", "naproxen", "aspirin", "mefenamic"], ["M01A", "N02BA", "B01AC06"], []),
        new("opioids", ["opioid", "opioids", "opiate", "opiates", "morphine", "codeine", "tramadol"], ["N02A"], []),
    ];

    public static readonly IReadOnlyList<InteractionRule> Interactions =
    [
        new("opioid_benzodiazepine", ["N02A"], ["N05BA", "N05CD"], RxSeverity.Severe, "cns_depression"),
        new("tramadol_antidepressant", ["N02AX02"], ["N06AB", "N06AX", "N06AA"], RxSeverity.Severe, "serotonin_syndrome"),
        new("clopidogrel_omeprazole", ["B01AC04"], ["A02BC01"], RxSeverity.Moderate, "reduced_antiplatelet"),
        new("antiplatelet_nsaid", ["B01AC"], ["M01A"], RxSeverity.Moderate, "bleeding"),
        new("raas_nsaid", ["C09"], ["M01A"], RxSeverity.Moderate, "renal_function"),
        new("betablocker_beta2agonist", ["C07"], ["R03AC"], RxSeverity.Moderate, "reduced_bronchodilation"),
        new("sulfonylurea_fluoroquinolone", ["A10BB"], ["J01MA"], RxSeverity.Moderate, "dysglycaemia"),
    ];

    /// <summary>Adult maximum daily dose by ATC code, in micrograms.</summary>
    public static readonly IReadOnlyDictionary<string, long> MaxDailyMicrograms = new Dictionary<string, long>(StringComparer.Ordinal)
    {
        ["N02BE01"] = 4_000_000, ["M01AE01"] = 3_200_000, ["R06AX27"] = 10_000, ["J01CA04"] = 3_000_000, ["J01FA10"] = 1_000_000,
        ["J01MA02"] = 1_500_000, ["A10BA02"] = 2_550_000, ["A10BB09"] = 320_000, ["C10AA05"] = 80_000, ["C08CA01"] = 10_000,
        ["C09CA01"] = 100_000, ["C07AB02"] = 400_000, ["B01AC06"] = 4_000_000, ["B01AC04"] = 600_000, ["A02BC01"] = 80_000,
        ["A02BC02"] = 80_000, ["H03AA01"] = 300, ["N02AX02"] = 400_000, ["N05BA12"] = 4_000, ["N06BA04"] = 60_000,
    };

    static readonly Regex StrengthShape = new(@"^\s*([0-9]{1,6}(?:\.[0-9]{1,3})?)\s*(mg|mcg|g)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
    static readonly Regex Words = new(@"[^\p{L}]+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static SafetyResult Evaluate(IReadOnlyList<CheckedMedicine> lines, IReadOnlyList<CheckedMedicine> chart, ChartAllergies state, IReadOnlyList<CheckedAllergy> allergies)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(allergies);
        var alerts = new List<SafetyAlert>();
        var notChecked = new List<NotChecked>();
        // Unknown is not "none": without a recorded allergy state the allergy check cannot run, and says so.
        if (state == ChartAllergies.NotRecorded && lines.Count > 0)
            alerts.Add(new("allergies:not_recorded", RxAlertKind.AllergiesNotRecorded, RxSeverity.Moderate, null, null, null, false, null, null, null, null, null));

        foreach (var line in lines)
        {
            if (line.FreeText || line.Atc is null)
            {
                notChecked.Add(new(line.Id, line.Name, false, line.FreeText ? NotCheckedReason.FreeText : NotCheckedReason.NoIngredientCode));
                continue;
            }
            foreach (var allergy in allergies) AllergyAlert(line, allergy, alerts);
            if (MaxDailyMicrograms.TryGetValue(line.Atc, out var max) && DailyMicrograms(line) is { } daily)
            {
                if (daily > max)
                    alerts.Add(new($"dose:{line.Id}:{daily.ToString(CultureInfo.InvariantCulture)}", RxAlertKind.DoseAboveMaximum, RxSeverity.Severe, line.Id, line.Name, null, false, null, null, null, daily, max));
            }
            else notChecked.Add(new(line.Id, line.Name, false, NotCheckedReason.DoseNotChecked));
        }
        notChecked.AddRange(chart.Where(c => c.Atc is null).Select(c => new NotChecked(c.Id, c.Name, true, NotCheckedReason.NotOnFormulary)));

        // Pairs with at least one prescribed line: what the chart already holds together is not this prescription's finding.
        var covered = lines.Where(l => !l.FreeText && l.Atc is not null).ToList();
        var others = covered.Concat(chart.Where(c => c.Atc is not null)).ToList();
        for (var i = 0; i < covered.Count; i++)
            for (var j = i + 1; j < others.Count; j++) PairAlerts(covered[i], others[j], alerts);
        return new(alerts, notChecked);
    }

    static void AllergyAlert(CheckedMedicine line, CheckedAllergy allergy, List<SafetyAlert> alerts)
    {
        var said = Tokens(allergy.Substance);
        SafetyAlert Alert(RxAlertKind kind, RxSeverity severity, string code) =>
            new($"{(kind == RxAlertKind.Allergy ? "allergy" : "cross")}:{line.Id}:{allergy.Id}", kind, severity, line.Id, line.Name, null, false, code, allergy.Substance, allergy.Status, null, null);
        var name = Tokens(line.Name);
        if (name.Count > 0 && name.All(said.Contains)) alerts.Add(Alert(RxAlertKind.Allergy, RxSeverity.Severe, "ingredient"));
        else if (Classes.FirstOrDefault(c => c.Terms.Any(said.Contains) && Starts(line.Atc!, c.Atc)) is { } cls) alerts.Add(Alert(RxAlertKind.Allergy, RxSeverity.Severe, cls.Id));
        else if (Classes.FirstOrDefault(c => c.Terms.Any(said.Contains) && Starts(line.Atc!, c.Cross)) is { } cross) alerts.Add(Alert(RxAlertKind.CrossSensitivity, RxSeverity.Moderate, cross.Id));
    }

    static void PairAlerts(CheckedMedicine line, CheckedMedicine other, List<SafetyAlert> alerts)
    {
        var (a, b) = (line.Atc!, other.Atc!);
        var ids = string.Join(':', new[] { line.Id, other.Id }.Order());
        if (Interactions.FirstOrDefault(r => (Starts(a, r.A) && Starts(b, r.B)) || (Starts(b, r.A) && Starts(a, r.B))) is { } rule)
            alerts.Add(new($"interaction:{rule.Id}:{ids}", RxAlertKind.Interaction, rule.Severity, line.Id, line.Name, other.Name, other.OnChart, rule.Effect, null, null, null, null));
        // The same medicine as on the chart is a continuation, not a duplicate; a second line of it, or another of the same class, is.
        var code = a == b ? (other.OnChart ? null : "same_medicine") : a.Length >= 5 && b.Length >= 5 && a[..5] == b[..5] ? "same_class" : null;
        if (code is not null)
            alerts.Add(new($"duplicate:{ids}", RxAlertKind.Duplicate, RxSeverity.Moderate, line.Id, line.Name, other.Name, other.OnChart, code, null, null, null, null));
    }

    /// <summary>
    /// The line's total a day in micrograms, or null when it cannot be added up: a missing dose or frequency, "when needed" or weekly, or a unit
    /// other than mg or a counted tablet, capsule or puff of a known strength.
    /// </summary>
    public static long? DailyMicrograms(CheckedMedicine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Sig is not { Dose: { } doseText, Frequency: { } frequency } sig) return null;
        int? perDay = frequency switch { "OD" or "STAT" => 1, "BD" => 2, "TDS" => 3, "QID" => 4, _ => null };
        if (perDay is null || !decimal.TryParse(doseText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var dose)) return null;
        decimal? each = sig.DoseUnit switch
        {
            "mg" => 1000m,
            "tablet" or "capsule" or "puff" => StrengthMicrograms(line.Strength),
            _ => null,
        };
        return each is { } e ? (long)Math.Round(dose * e * perDay.Value, MidpointRounding.AwayFromZero) : null;
    }

    static decimal? StrengthMicrograms(string? strength)
    {
        if (strength is null || StrengthShape.Match(strength) is not { Success: true } m) return null;
        var amount = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return m.Groups[2].Value.ToLowerInvariant() switch { "g" => amount * 1_000_000m, "mg" => amount * 1000m, _ => amount };
    }

    /// <summary>
    /// The formulary generic a chart medicine's name starts with ("Metformin 500 mg" is Metformin), the longest first; null when none does.
    /// A name is never matched on part of a word.
    /// </summary>
    public static string? MatchGeneric(string name, IEnumerable<string> generics)
    {
        ArgumentNullException.ThrowIfNull(name);
        var n = name.Trim();
        return generics.OrderByDescending(g => g.Length)
            .FirstOrDefault(g => n.Equals(g, StringComparison.OrdinalIgnoreCase) || (n.StartsWith(g, StringComparison.OrdinalIgnoreCase) && !char.IsLetter(n[g.Length])));
    }

    static bool Starts(string atc, IReadOnlyList<string> prefixes) => prefixes.Any(p => atc.StartsWith(p, StringComparison.Ordinal));

    static HashSet<string> Tokens(string text) => [.. Words.Split(text.ToLowerInvariant()).Where(w => w.Length > 0)];
}

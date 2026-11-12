using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Prescribing.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<ReviewRule>))]
public enum ReviewRule { Benzodiazepine, LongTermPpi, SystemicNsaid, RaasLowEgfrHighPotassium }

/// <summary>Why a chart medicine was not reviewed: its name matches no single formulary medicine, or a RAAS blocker without the eGFR or potassium the rule needs.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReviewGap>))]
public enum ReviewGap { NotOnFormulary, NoEgfr, NoPotassium }

/// <summary>An active chart medicine with its ATC code (null: not matched) and when it was first recorded on the chart.</summary>
public sealed record ReviewMedicine(
    [property: Classification(DataClass.Internal)] Guid Id, [property: Classification(DataClass.SensitiveHealth)] string Name,
    [property: Classification(DataClass.Internal)] string? Atc, [property: Classification(DataClass.SensitiveHealth)] DateTimeOffset RecordedAt);

/// <summary>
/// One suggestion for the doctor to decide on. The evidence it rests on: <see cref="WeeksOnChart"/> for a PPI, <see cref="Egfr"/> (mL/min/1.73 m²)
/// and <see cref="PotassiumHundredths"/> (4.9 mmol/L is 490) for a RAAS blocker.
/// </summary>
public sealed record ReviewSuggestion(
    [property: Classification(DataClass.Internal)] Guid MedicineId, [property: Classification(DataClass.SensitiveHealth)] string Medicine,
    [property: Classification(DataClass.SensitiveHealth)] ReviewRule Rule, [property: Classification(DataClass.SensitiveHealth)] int? WeeksOnChart = null,
    [property: Classification(DataClass.SensitiveHealth)] int? Egfr = null, [property: Classification(DataClass.SensitiveHealth)] int? PotassiumHundredths = null);

public sealed record ReviewNotChecked(
    [property: Classification(DataClass.Internal)] Guid MedicineId, [property: Classification(DataClass.SensitiveHealth)] string Medicine,
    [property: Classification(DataClass.Internal)] ReviewGap Reason);

public sealed record MedicineReviewResult(
    [property: Classification(DataClass.SensitiveHealth)] IReadOnlyList<ReviewSuggestion> Suggestions,
    [property: Classification(DataClass.SensitiveHealth)] IReadOnlyList<ReviewNotChecked> NotChecked);

/// <summary>
/// The older adult medicine review (MDV-P2-037): suggestions only, the doctor decides; nothing here changes a medicine. From
/// <see cref="SafetyRules.OlderAdultAge"/>, over the active chart medicines, matched by ATC code with the prescription checks' own prefix lists.
/// AGS Beers Criteria 2023: benzodiazepines (falls, confusion), a proton pump inhibitor beyond 8 weeks, chronic systemic NSAIDs. A RAAS blocker
/// (ACE inhibitor or ARB) with eGFR under 60 (KDIGO 2012 G3 and below) and potassium above 5.0 mmol/L (above the chart's reference range,
/// MDV-P2-031): hyperkalaemia risk.
/// shortcut: a PPI's weeks count from when it was first recorded on the chart (the start date is not recorded), the eGFR and potassium are the
/// latest at any age, and the rule set is a draft; record a start date when MDV-P2-039 writes one, and set <see cref="SignOff"/> when the
/// clinical safety reviewer signs the rules (MDV-P2-028).
/// </summary>
public static class MedicineReview
{
    /// <summary>Null: the rule set is a draft awaiting clinical sign-off.</summary>
    public static readonly ClinicalSignOff? SignOff; // set to new(name, registration, date) when signed

    public const int PpiWeeks = 8, EgfrBelow = 60, PotassiumAboveHundredths = 500;

    public static MedicineReviewResult Evaluate(IReadOnlyList<ReviewMedicine> chart, PatientContext patient, int? potassiumHundredths, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(patient);
        var suggestions = new List<ReviewSuggestion>();
        var notChecked = new List<ReviewNotChecked>();
        if (patient.AgeYears < SafetyRules.OlderAdultAge) return new(suggestions, notChecked);
        foreach (var m in chart)
        {
            if (m.Atc is not { } atc) notChecked.Add(new(m.Id, m.Name, ReviewGap.NotOnFormulary));
            else if (SafetyRules.Starts(atc, SafetyRules.Benzodiazepine)) suggestions.Add(new(m.Id, m.Name, ReviewRule.Benzodiazepine));
            else if (SafetyRules.Starts(atc, SafetyRules.Nsaid)) suggestions.Add(new(m.Id, m.Name, ReviewRule.SystemicNsaid));
            else if (SafetyRules.Starts(atc, SafetyRules.Ppi))
            {
                var weeks = (int)((now - m.RecordedAt).TotalDays / 7);
                if (now - m.RecordedAt > TimeSpan.FromDays(PpiWeeks * 7)) suggestions.Add(new(m.Id, m.Name, ReviewRule.LongTermPpi, WeeksOnChart: weeks));
            }
            else if (SafetyRules.Starts(atc, SafetyRules.Raas))
            {
                if (patient.Egfr is not { } egfr) notChecked.Add(new(m.Id, m.Name, ReviewGap.NoEgfr));
                else if (egfr >= EgfrBelow) continue;
                else if (potassiumHundredths is not { } k) notChecked.Add(new(m.Id, m.Name, ReviewGap.NoPotassium));
                else if (k > PotassiumAboveHundredths) suggestions.Add(new(m.Id, m.Name, ReviewRule.RaasLowEgfrHighPotassium, Egfr: egfr, PotassiumHundredths: k));
            }
        }
        return new(suggestions, notChecked);
    }
}

using System.Globalization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>
/// A reading's usual adult range, in hundredths of the code's unit. Both bounds: the range includes both ("135–145"). Only an upper bound:
/// the value must be below it ("&lt; 100"). Only a lower bound: at least it ("≥ 60"). That is how the labs write them.
/// </summary>
public sealed record ReferenceRange([property: Classification(DataClass.Internal)] int? MinHundredths, [property: Classification(DataClass.Internal)] int? MaxHundredths)
{
    public VitalAlert? Flag(int hundredths) =>
        hundredths < MinHundredths ? VitalAlert.Low
        : MaxHundredths is { } max && (MinHundredths is null ? hundredths >= max : hundredths > max) ? VitalAlert.High
        : null;

    /// <summary>The range as the chart says it, without the unit: "3.5–5.0", "&lt; 100", "≥ 60". One decimal shown for both bounds when either has one.</summary>
    [Classification(DataClass.Internal)]
    public string Text
    {
        get
        {
            var format = MinHundredths % 100 is > 0 || MaxHundredths % 100 is > 0 ? "0.0#" : "0.##";
            string F(int h) => (h / 100m).ToString(format, CultureInfo.InvariantCulture);
            return (MinHundredths, MaxHundredths) switch
            {
                ({ } a, { } b) => $"{F(a)}–{F(b)}",
                (null, { } b) => $"< {F(b)}",
                ({ } a, null) => $"≥ {F(a)}",
                _ => "",
            };
        }
    }
}

/// <summary>
/// The adult reference range of each observation code (MDV-P2-031), used to flag the chart's latest readings High or Low. The vital ranges
/// are the adult band of <see cref="VitalRules"/> written as the chart says them (a test keeps the two equal). Labs: LDL &lt; 100 mg/dL (NCEP
/// ATP III optimal); eGFR ≥ 60 (KDIGO 2012, below is G3); urine ACR &lt; 30 mg/g (KDIGO A1); haemoglobin 12.0–17.5 g/dL; ferritin ≥ 15 ng/mL
/// (WHO 2020 iron deficiency line for adults); potassium 3.5–5.0 and sodium 135–145 mmol/L; TSH 0.4–4.0 mIU/L; 25-OH vitamin D ≥ 20 ng/mL
/// (Endocrine Society deficiency line); platelets 150–400 ×10³/µL; HbA1c &lt; 5.7 % (ADA, below the prediabetes line); fetal heart rate 110–160
/// /min (FIGO 2015 normal baseline); pain &lt; 4 of 10 (none or mild). Height and fundal height have no adult range (fundal height follows the
/// weeks of gestation, MDV-P2-034), so they are never flagged.
/// shortcut: one adult set for every patient (no sex-specific haemoglobin or ferritin, no pregnancy or child ranges; children's vitals keep the
/// age bands of #956 at intake) and every facility's laboratory; sex, pregnancy and lab-specific ranges when the clinical reviewer chooses them.
/// </summary>
public static class ReferenceRanges
{
    /// <summary>Null: the ranges are a draft awaiting clinical sign-off, like <see cref="VitalRules.SignOff"/>.</summary>
    public static readonly ClinicalSignOff? SignOff; // set to new(name, registration, date) when signed

    /// <summary>Null: the code has no adult range and is never flagged.</summary>
    public static ReferenceRange? For(ObservationCode code) => code switch
    {
        ObservationCode.BloodPressureSystolic or ObservationCode.BloodPressureStandingSystolic => new(9000, 13900),
        ObservationCode.BloodPressureDiastolic or ObservationCode.BloodPressureStandingDiastolic => new(6000, 8900),
        ObservationCode.Pulse => new(5000, 10000),
        ObservationCode.Temperature => new(3600, 3790),
        ObservationCode.OxygenSaturation => new(9400, null),
        ObservationCode.RespiratoryRate => new(1200, 2000),
        ObservationCode.PainScore => new(null, 400),
        ObservationCode.FetalHeartRate => new(11000, 16000),
        ObservationCode.Weight or ObservationCode.Height or ObservationCode.FundalHeight or ObservationCode.Length or ObservationCode.HeadCircumference => null,
        ObservationCode.HbA1c => new(null, 570),
        ObservationCode.LdlCholesterol => new(null, 10000),
        ObservationCode.Egfr => new(6000, null),
        ObservationCode.UrineAlbuminCreatinineRatio => new(null, 3000),
        ObservationCode.Haemoglobin => new(1200, 1750),
        ObservationCode.Ferritin => new(1500, null),
        ObservationCode.Potassium => new(350, 500),
        ObservationCode.Sodium => new(13500, 14500),
        ObservationCode.Tsh => new(40, 400),
        ObservationCode.VitaminD => new(2000, null),
        ObservationCode.Platelets => new(15000, 40000),
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };

    /// <summary>Body mass index (kg/m²) to one decimal, from a weight in hundredths of a kg and a height in hundredths of a cm. Worked out, never stored.</summary>
    public static int BmiTenths(int weightHundredths, int heightHundredths) =>
        (int)decimal.Round(weightHundredths / 100m / (heightHundredths / 10000m * (heightHundredths / 10000m)) * 10, MidpointRounding.AwayFromZero);
}

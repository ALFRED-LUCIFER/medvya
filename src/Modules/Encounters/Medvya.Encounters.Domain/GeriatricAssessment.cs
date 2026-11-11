using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>
/// GERIATRIC_ASSESSMENT (MDV-P2-037): one older adult review, recorded by a nurse or a doctor at a facility. Every score is optional (a patient
/// may not manage the walking test), but a row holds at least one. A new assessment is a new row: a recorded one is never edited.
/// Timed Up and Go is in tenths of a second (14.2 s is 142); blood pressure in mmHg, lying then standing.
/// </summary>
public sealed class GeriatricAssessment
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? FallsLast12Months { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? TimedUpAndGoTenths { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? LyingSystolic { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? LyingDiastolic { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? StandingSystolic { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? StandingDiastolic { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? ClinicalFrailtyScale { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? MiniCog { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? Adl { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? Iadl { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? Phq2 { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? NutritionNote { get; init; }
    [Classification(DataClass.Internal)] public Guid RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset AssessedAt { get; init; }
}

/// <summary>
/// The scales and flag lines of the older adult review. Scales: Clinical Frailty Scale 1–9 (Rockwood, CFS v2.0 2020), Mini-Cog 0–5 (Borson
/// 2000), Katz ADL 0–6, Lawton IADL 0–8, PHQ-2 0–6 (Kroenke 2003). Flag lines: orthostatic hypotension is a fall of at least 20 mmHg systolic
/// or 10 mmHg diastolic on standing (consensus statement, Freeman et al., Clin Auton Res 2011); Timed Up and Go of 12 s or more is a higher
/// fall risk (CDC STEADI). Blood pressure readings must be possible at all (<see cref="VitalRules.Possible"/>).
/// shortcut: no cut-offs are flagged for frailty, Mini-Cog, ADL, IADL or PHQ-2 (the screen shows the score on its scale), and the two flag
/// lines are a draft; add cut-offs and set <see cref="SignOff"/> when the clinical safety reviewer signs them (MDV-P2-028).
/// </summary>
public static class GeriatricRules
{
    /// <summary>Null: the flag lines are a draft awaiting clinical sign-off.</summary>
    public static readonly ClinicalSignOff? SignOff; // set to new(name, registration, date) when signed

    public const int OrthostaticSystolicDrop = 20, OrthostaticDiastolicDrop = 10, TugAtRiskTenths = 120, MaxNutritionNote = 500;

    /// <summary>The allowed range of each score; a value outside it is refused, never stored.</summary>
    public static readonly IReadOnlyDictionary<string, (int Min, int Max)> Scales = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
    {
        ["fallsLast12Months"] = (0, 99), ["timedUpAndGoSeconds"] = (10, 1200), ["clinicalFrailtyScale"] = (1, 9), ["miniCog"] = (0, 5),
        ["adl"] = (0, 6), ["iadl"] = (0, 8), ["phq2"] = (0, 6),
    };

    /// <summary>The fall from lying to standing (positive is a fall), and whether it is orthostatic: at the flag line counts.</summary>
    public static (int Systolic, int Diastolic, bool Orthostatic) Drop(int lyingSystolic, int lyingDiastolic, int standingSystolic, int standingDiastolic)
    {
        var (s, d) = (lyingSystolic - standingSystolic, lyingDiastolic - standingDiastolic);
        return (s, d, s >= OrthostaticSystolicDrop || d >= OrthostaticDiastolicDrop);
    }

    public static bool TugAtRisk(int tenths) => tenths >= TugAtRiskTenths;

    /// <summary>The (field, code) errors of an assessment: "out_of_scale", "incomplete" (blood pressure needs all four), "too_long" or "empty".</summary>
    public static List<(string Field, string Code)> Validate(GeriatricAssessment a)
    {
        ArgumentNullException.ThrowIfNull(a);
        var errors = new List<(string, string)>();
        void Scale(string field, int? value)
        {
            if (value is { } v && (v < Scales[field].Min || v > Scales[field].Max)) errors.Add((field, "out_of_scale"));
        }
        Scale("fallsLast12Months", a.FallsLast12Months);
        Scale("timedUpAndGoSeconds", a.TimedUpAndGoTenths);
        Scale("clinicalFrailtyScale", a.ClinicalFrailtyScale);
        Scale("miniCog", a.MiniCog);
        Scale("adl", a.Adl);
        Scale("iadl", a.Iadl);
        Scale("phq2", a.Phq2);
        int?[] bp = [a.LyingSystolic, a.LyingDiastolic, a.StandingSystolic, a.StandingDiastolic];
        if (bp.Any(v => v is not null) && bp.Any(v => v is null)) errors.Add(("bloodPressure", "incomplete"));
        void Pressure(string field, int? value, VitalKind kind)
        {
            if (value is { } v && (v < VitalRules.Possible(kind).Min || v > VitalRules.Possible(kind).Max)) errors.Add((field, "out_of_scale"));
        }
        Pressure("lyingSystolic", a.LyingSystolic, VitalKind.SystolicBp);
        Pressure("lyingDiastolic", a.LyingDiastolic, VitalKind.DiastolicBp);
        Pressure("standingSystolic", a.StandingSystolic, VitalKind.SystolicBp);
        Pressure("standingDiastolic", a.StandingDiastolic, VitalKind.DiastolicBp);
        if (a.NutritionNote?.Length > MaxNutritionNote) errors.Add(("nutritionNote", "too_long"));
        if (errors.Count == 0 && bp[0] is null && new[] { a.FallsLast12Months, a.TimedUpAndGoTenths, a.ClinicalFrailtyScale, a.MiniCog, a.Adl, a.Iadl, a.Phq2 }.All(v => v is null)
            && string.IsNullOrWhiteSpace(a.NutritionNote)) errors.Add(("assessment", "empty"));
        return errors;
    }
}

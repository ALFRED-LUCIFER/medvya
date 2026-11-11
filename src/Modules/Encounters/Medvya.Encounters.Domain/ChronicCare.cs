using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>
/// CARE_TARGET (MDV-P2-038): the doctor's target for one measure of one patient, overriding <see cref="CareTargets.Defaults"/>. A change is a
/// new row (the newest per code is current), so nothing is overwritten and a patient merge cannot collide. The limit is in hundredths of the
/// code's unit, like <see cref="Observation.ValueHundredths"/>.
/// </summary>
public sealed class CareTarget
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public ObservationCode Code { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int LimitHundredths { get; init; }
    [Classification(DataClass.Internal)] public Guid SetBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset SetAt { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<CareCheckKind>))]
public enum CareCheckKind { DiabeticEyeExam, DiabeticFootExam }

/// <summary>
/// CARE_CHECK (MDV-P2-038): the doctor's record that a diabetic eye or foot check was done on a day (here or elsewhere). It is the only thing
/// that closes those two gaps: no record, no "done". A record is never edited; a later check is a new row.
/// shortcut: the structured foot examination of a signed note (MDV-P2-042) does not close the foot gap yet; read it here once that is on main.
/// </summary>
public sealed class CareCheck
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public CareCheckKind Kind { get; init; }
    [Classification(DataClass.SensitiveHealth)] public DateOnly DoneOn { get; init; }
    [Classification(DataClass.Internal)] public Guid RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RecordedAt { get; init; }
}

/// <summary>CARE_GOAL (MDV-P2-038): a care-plan goal in words, its target in words and its progress 0–100 %, with who set it and who last moved it.</summary>
public sealed class CareGoal
{
    public const int MaxText = 200, MaxTarget = 200;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public required string Text { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Target { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int ProgressPercent { get; set; }
    [Classification(DataClass.Internal)] public Guid CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid UpdatedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Whether a reading meets its target below the limit (HbA1c &lt; 7 %) or at or above it (eGFR ≥ 60).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TargetDirection>))]
public enum TargetDirection { Below, AtLeast }

/// <summary>
/// The chronic care targets (MDV-P2-038). Defaults, for an adult with diabetes: HbA1c below 7.0 % (ADA Standards of Care 2024, §6.4); blood
/// pressure below 130/80 mmHg (ADA 2024, §10.4); LDL cholesterol below 100 mg/dL (ADA 2024, §10.18, no cardiovascular disease); eGFR at least
/// 60 mL/min/1.73 m² and urine albumin-creatinine ratio below 30 mg/g (KDIGO 2012, G1–G2 and A1: the lines of chronic kidney disease, watched
/// rather than treated to). A doctor sets another limit per patient within <see cref="Allowed"/> (a range of what can be typed, not clinical).
/// shortcut: the defaults are a draft; set <see cref="SignOff"/> when the clinical safety reviewer signs them (MDV-P2-028).
/// </summary>
public static class CareTargets
{
    /// <summary>Null: the defaults and the gap rules are a draft awaiting clinical sign-off.</summary>
    public static readonly ClinicalSignOff? SignOff; // set to new(name, registration, date) when signed

    public static readonly IReadOnlyDictionary<ObservationCode, (TargetDirection Direction, int DefaultHundredths, int Min, int Max)> Defaults =
        new Dictionary<ObservationCode, (TargetDirection, int, int, int)>
        {
            [ObservationCode.HbA1c] = (TargetDirection.Below, 700, 500, 1000),
            [ObservationCode.BloodPressureSystolic] = (TargetDirection.Below, 13000, 10000, 18000),
            [ObservationCode.BloodPressureDiastolic] = (TargetDirection.Below, 8000, 6000, 11000),
            [ObservationCode.LdlCholesterol] = (TargetDirection.Below, 10000, 3000, 20000),
            [ObservationCode.Egfr] = (TargetDirection.AtLeast, 6000, 1500, 9000),
            [ObservationCode.UrineAlbuminCreatinineRatio] = (TargetDirection.Below, 3000, 1000, 30000),
        };

    public static bool Allowed(ObservationCode code, int hundredths) => Defaults.TryGetValue(code, out var d) && hundredths >= d.Min && hundredths <= d.Max;

    public static bool Met(TargetDirection direction, int valueHundredths, int limitHundredths) =>
        direction == TargetDirection.Below ? valueHundredths < limitHundredths : valueHundredths >= limitHundredths;
}

/// <summary>What closes or opens a gap, and what its action puts in the open consultation: a line in the plan, a draft order or a referral.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CareGapRule>))]
public enum CareGapRule { DiabeticEyeExam, DiabeticFootExam, UrineAlbumin, OrderNotResulted, VaccineDue, GlucoseToleranceTest, FallsReview }

[JsonConverter(typeof(JsonStringEnumConverter<CareGapAction>))]
public enum CareGapAction { AddToVisit, Order, Refer }

/// <summary>
/// One gap due today. <see cref="Item"/> is the English text the action writes into the record (the plan line, the order's test, the
/// referral's reason), the language of the printed papers. <see cref="LastDone"/> is the last record that would have closed it (null: never
/// recorded); <see cref="Since"/> is when the order was placed or the dose fell due.
/// </summary>
public sealed record CareGap(
    [property: Classification(DataClass.SensitiveHealth)] CareGapRule Rule, [property: Classification(DataClass.Internal)] CareGapAction Action, [property: Classification(DataClass.SensitiveHealth)] string Item, [property: Classification(DataClass.SensitiveHealth)] DateOnly? LastDone = null,
    [property: Classification(DataClass.SensitiveHealth)] DateOnly? Since = null, [property: Classification(DataClass.SensitiveHealth)] string? Vaccine = null, [property: Classification(DataClass.SensitiveHealth)] int? Dose = null);

/// <summary>A placed order, read from Prescribing through <c>IPlacedOrders</c>.</summary>
public sealed record PlacedOrderFact([property: Classification(DataClass.SensitiveHealth)] string Name, [property: Classification(DataClass.Internal)] DateTimeOffset OrderedAt);

/// <summary>One older adult review as the falls rule needs it: falls reported, and whether the walking test and lying/standing pressure were measured.</summary>
public sealed record FallsFact(
    [property: Classification(DataClass.Internal)] DateTimeOffset AssessedAt, [property: Classification(DataClass.SensitiveHealth)] int? Falls, [property: Classification(DataClass.SensitiveHealth)] bool TugMeasured, [property: Classification(DataClass.SensitiveHealth)] bool StandingDropMeasured);

/// <summary>A vaccine dose due now or overdue (from MDV-P2-036).</summary>
public sealed record DueDoseFact([property: Classification(DataClass.SensitiveHealth)] string Vaccine, [property: Classification(DataClass.SensitiveHealth)] int Dose, [property: Classification(DataClass.SensitiveHealth)] DateOnly DueFrom);

/// <summary>
/// What the gap rules read. <see cref="LatestTaken"/>: when each observation code was last taken. <see cref="GestationDays"/>: today's
/// gestation of an open pregnancy (null: no open pregnancy); <see cref="GlucoseTestOrderedInPregnancy"/>: an OGTT order placed since it began.
/// </summary>
public sealed record CareFacts(
    [property: Classification(DataClass.SensitiveHealth)] bool Diabetes, [property: Classification(DataClass.SensitiveHealth)] DateOnly? LastEyeExam, [property: Classification(DataClass.SensitiveHealth)] DateOnly? LastFootExam,
    [property: Classification(DataClass.SensitiveHealth)] IReadOnlyDictionary<ObservationCode, DateTimeOffset> LatestTaken, [property: Classification(DataClass.SensitiveHealth)] IReadOnlyList<PlacedOrderFact> PlacedOrders,
    [property: Classification(DataClass.SensitiveHealth)] IReadOnlyList<DueDoseFact> DueDoses, [property: Classification(DataClass.SensitiveHealth)] int? GestationDays, [property: Classification(DataClass.SensitiveHealth)] bool GlucoseTestOrderedInPregnancy,
    [property: Classification(DataClass.SensitiveHealth)] IReadOnlyList<FallsFact> Assessments);

/// <summary>
/// The care gap rules (MDV-P2-038), the design's list. Unknown is due: a check is done only when a record says so.
/// - Diabetes is an active problem coded E10–E14 (ICD-10), whatever its verification: a missed prompt is worse than an extra one.
/// - Eye and foot checks: a <see cref="CareCheck"/> on or after the same day a year ago.
/// - Urine albumin: a UACR reading in the last year.
/// - A test ordered but never resulted: an order placed <see cref="ResultWaitDays"/> days ago or more, in the last year, with no reading of its
///   test taken since. Only the tests in <see cref="ResultCodes"/> can be matched to a reading; any other is due until results arrive with
///   their order (MDV-P4). A re-ordered test counts once, by its latest order.
/// - Vaccines due: every dose due now or overdue (the country schedule, MDV-P2-036).
/// - Glucose tolerance test: 24 weeks 0 days to 28 weeks 6 days of an open pregnancy, until one is ordered (the order rule then follows it).
/// - Falls review: a fall reported in a review of the last year, unless that review or a later one measured both the walking test and the
///   lying/standing pressure.
/// shortcut: the list and its intervals wait for clinical sign-off (<see cref="CareTargets.SignOff"/>); which ones the pilot doctors want is theirs to choose.
/// </summary>
public static class CareGapRules
{
    public const int ResultWaitDays = 14, OgttFromDays = 24 * 7, OgttUntilDays = 28 * 7 + 6;
    public const string GlucoseTestName = "Oral glucose tolerance test (75 g)", UacrName = "Urine albumin-creatinine ratio";

    /// <summary>Order names (case and spaces ignored) the clinic's lab results can be matched to.</summary>
    public static readonly IReadOnlyDictionary<string, ObservationCode> ResultCodes = new Dictionary<string, ObservationCode>(StringComparer.OrdinalIgnoreCase)
    {
        ["HbA1c"] = ObservationCode.HbA1c, ["Lipid profile"] = ObservationCode.LdlCholesterol, ["LDL cholesterol"] = ObservationCode.LdlCholesterol,
        ["eGFR"] = ObservationCode.Egfr, ["Kidney function"] = ObservationCode.Egfr, [UacrName] = ObservationCode.UrineAlbuminCreatinineRatio,
        ["UACR"] = ObservationCode.UrineAlbuminCreatinineRatio, ["Haemoglobin"] = ObservationCode.Haemoglobin, ["Ferritin"] = ObservationCode.Ferritin,
        ["Potassium"] = ObservationCode.Potassium, ["Sodium"] = ObservationCode.Sodium, ["TSH"] = ObservationCode.Tsh, ["Vitamin D"] = ObservationCode.VitaminD,
        ["Platelets"] = ObservationCode.Platelets,
    };

    /// <summary>E10–E14 (type 1, type 2, malnutrition-related, other and unspecified diabetes), with or without a subcode.</summary>
    public static bool IsDiabetes(string? icd10) =>
        icd10?.Trim().ToUpperInvariant() is { Length: >= 3 } c && c[0] == 'E' && c[1] == '1' && c[2] is >= '0' and <= '4' && (c.Length == 3 || c[3] == '.' || char.IsDigit(c[3]));

    /// <summary>I10–I15 (hypertensive diseases): with diabetes, what shows the targets card.</summary>
    public static bool IsHypertension(string? icd10) =>
        icd10?.Trim().ToUpperInvariant() is { Length: >= 3 } c && c[0] == 'I' && c[1] == '1' && c[2] is >= '0' and <= '5' && (c.Length == 3 || c[3] == '.' || char.IsDigit(c[3]));

    public static bool IsGlucoseTest(string name) => name.Contains("glucose tolerance", StringComparison.OrdinalIgnoreCase) || name.Contains("OGTT", StringComparison.OrdinalIgnoreCase);

    /// <summary>The gaps due at <paramref name="now"/>; days are the facility's (<paramref name="zone"/>).</summary>
    public static IReadOnlyList<CareGap> Due(CareFacts f, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(f);
        DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
        var today = Day(now);
        var gaps = new List<CareGap>();
        var yearAgo = today.AddYears(-1);
        var yearAgoAt = now.AddYears(-1);
        if (f.Diabetes)
        {
            if (f.LastEyeExam is not { } eye || eye < yearAgo) gaps.Add(new(CareGapRule.DiabeticEyeExam, CareGapAction.Refer, "Annual diabetic eye check", f.LastEyeExam));
            if (f.LastFootExam is not { } foot || foot < yearAgo) gaps.Add(new(CareGapRule.DiabeticFootExam, CareGapAction.AddToVisit, "Annual diabetic foot check", f.LastFootExam));
            var uacr = f.LatestTaken.GetValueOrDefault(ObservationCode.UrineAlbuminCreatinineRatio);
            if (uacr == default || uacr < yearAgoAt) gaps.Add(new(CareGapRule.UrineAlbumin, CareGapAction.Order, UacrName, uacr == default ? null : Day(uacr)));
        }
        foreach (var o in f.PlacedOrders.Where(o => o.OrderedAt >= yearAgoAt).GroupBy(o => o.Name.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.MaxBy(o => o.OrderedAt)!))
        {
            if (o.OrderedAt > now.AddDays(-ResultWaitDays)) continue;
            var resulted = ResultCodes.TryGetValue(o.Name.Trim(), out var code) && f.LatestTaken.GetValueOrDefault(code) >= o.OrderedAt;
            if (!resulted) gaps.Add(new(CareGapRule.OrderNotResulted, CareGapAction.Order, o.Name.Trim(), Since: Day(o.OrderedAt)));
        }
        gaps.AddRange(f.DueDoses.Select(d => new CareGap(CareGapRule.VaccineDue, CareGapAction.AddToVisit, $"{d.Vaccine} dose {d.Dose}", Since: d.DueFrom, Vaccine: d.Vaccine, Dose: d.Dose)));
        if (f.GestationDays is >= OgttFromDays and <= OgttUntilDays && !f.GlucoseTestOrderedInPregnancy)
            gaps.Add(new(CareGapRule.GlucoseToleranceTest, CareGapAction.Order, GlucoseTestName));
        var recent = f.Assessments.Where(a => a.AssessedAt >= yearAgoAt).ToList();
        if (recent.Where(a => a.Falls > 0).MaxBy(a => a.AssessedAt) is { } fall
            && !recent.Any(a => a.AssessedAt >= fall.AssessedAt && a.TugMeasured && a.StandingDropMeasured))
            gaps.Add(new(CareGapRule.FallsReview, CareGapAction.AddToVisit, "Falls risk review (walking test, lying and standing blood pressure)", Since: Day(fall.AssessedAt)));
        return gaps;
    }
}

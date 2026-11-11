using System.Globalization;
using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<GrowthIndicator>))]
public enum GrowthIndicator { WeightForAge, LengthForAge, BmiForAge }

/// <summary>How a length or height was measured. Recorded as <see cref="ObservationCode.Length"/> (lying) or <see cref="ObservationCode.Height"/> (standing).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BodyPosition>))]
public enum BodyPosition { Lying, Standing }

[JsonConverter(typeof(JsonStringEnumConverter<GrowthAlert>))]
public enum GrowthAlert { BelowThird, CrossedDown, CrossedUp }

/// <summary>
/// WHO Child Growth Standards 0–5 years (MDV-P2-035): weight-for-age, length/height-for-age and BMI-for-age by sex and age in days, by the
/// LMS method, z = ((X/M)^L − 1)/(L·S), with WHO's restricted method beyond ±3 SD for weight and BMI (the distance between SD2 and SD3 is the
/// unit). The tables are WHO's expanded daily tables, unchanged (Growth/who-lms-0-5y.csv names the files and the download date).
/// shortcut: 0–5 y WHO only; IAP 2015 (5–18 y, India) and the UAE national charts when the clinical reviewer chooses them. The tables, the
/// centile lines and the two flags (below the 3rd, crossing two major lines) wait for <see cref="SignOff"/>, and the screen says so.
/// </summary>
public static class WhoGrowthStandards
{
    /// <summary>Null: the growth references and flag lines are a draft awaiting clinical sign-off, like <see cref="VitalRules.SignOff"/>.</summary>
    public static readonly ClinicalSignOff? SignOff; // set to new(name, registration, date) when signed

    /// <summary>The oldest age in the tables (5 years); an older child gets no centiles.</summary>
    public const int LastDay = 1856;
    /// <summary>WHO measures length lying up to this day and height standing after it; a measurement in the other position moves 0.7 cm.</summary>
    public const int LastLyingDay = 730;
    const double PositionCm = 0.7;

    /// <summary>The major centile lines the chart draws, with their z (standard normal quantiles, not clinical data).</summary>
    public static readonly IReadOnlyList<(int Centile, double Z)> Lines = [(3, -1.880794), (15, -1.036433), (50, 0), (85, 1.036433), (97, 1.880794)];

    // Reference data, not a stored measurement: double is what the LMS powers need, and nothing here is money or a patient value.
    static readonly Dictionary<(GrowthIndicator, bool), (double L, double M, double S)[]> Tables = Load();

    static (double L, double M, double S) LmsOf(GrowthIndicator indicator, bool male, int day)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(day);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, LastDay);
        return Tables[(indicator, male)][day];
    }

    public static double ZScore(GrowthIndicator indicator, bool male, int day, double value)
    {
        var lms = LmsOf(indicator, male, day);
        var z = (Math.Pow(value / lms.M, lms.L) - 1) / (lms.L * lms.S);
        // Length is near-normal (L = 1) and is not restricted; weight and BMI are skewed, so WHO extends past ±3 SD in SD3−SD2 steps.
        if (indicator == GrowthIndicator.LengthForAge || Math.Abs(z) <= 3) return z;
        var sign = Math.Sign(z);
        var (sd3, sd2) = (ValueAt(lms, 3 * sign), ValueAt(lms, 2 * sign));
        return 3 * sign + (value - sd3) / Math.Abs(sd3 - sd2);
    }

    /// <summary>The measurement at a z (within ±3 SD, where no restriction applies): the centile lines.</summary>
    public static double ValueAt(GrowthIndicator indicator, bool male, int day, double z) => ValueAt(LmsOf(indicator, male, day), z);

    static double ValueAt((double L, double M, double S) lms, double z) => lms.M * Math.Pow(1 + lms.L * lms.S * z, 1 / lms.L);

    /// <summary>The length or height in WHO's position for the age. No position (nurse intake does not ask): read as measured in that position.</summary>
    public static double LengthForTables(double cm, int day, BodyPosition? position) => (position, day <= LastLyingDay) switch
    {
        (BodyPosition.Lying, false) => cm - PositionCm,
        (BodyPosition.Standing, true) => cm + PositionCm,
        _ => cm,
    };

    /// <summary>The centile of a z, 0–100.</summary>
    public static double Centile(double z) => 50 * (1 + Erf(z / Math.Sqrt(2)));

    /// <summary>
    /// BelowThird when the latest point is under the 3rd centile line; CrossedDown or CrossedUp when the latest point is two or more major lines
    /// (3rd, 15th, 50th, 85th, 97th) away from an earlier point. Points oldest first.
    /// </summary>
    public static IReadOnlyList<GrowthAlert> Flags(IReadOnlyList<double> zs)
    {
        ArgumentNullException.ThrowIfNull(zs);
        if (zs.Count == 0) return [];
        var latest = zs[^1];
        List<GrowthAlert> flags = [];
        if (latest < Lines[0].Z) flags.Add(GrowthAlert.BelowThird);
        var moved = zs.Take(zs.Count - 1).Select(z => Band(latest) - Band(z)).DefaultIfEmpty(0).MaxBy(Math.Abs);
        if (moved <= -2) flags.Add(GrowthAlert.CrossedDown);
        else if (moved >= 2) flags.Add(GrowthAlert.CrossedUp);
        return flags;
    }

    /// <summary>How many major lines a z is on or above (0–5).</summary>
    static int Band(double z) => Lines.Count(l => z >= l.Z);

    // Abramowitz and Stegun 7.1.26 (error under 1.5e-7): ample for a centile shown to a whole number.
    static double Erf(double x)
    {
        var t = 1 / (1 + 0.3275911 * Math.Abs(x));
        var y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return Math.Sign(x) * y;
    }

    static Dictionary<(GrowthIndicator, bool), (double L, double M, double S)[]> Load()
    {
        using var stream = typeof(WhoGrowthStandards).Assembly.GetManifestResourceStream("Medvya.Encounters.Domain.Growth.who-lms-0-5y.csv")
            ?? throw new InvalidOperationException("The WHO growth tables are not embedded.");
        using var reader = new StreamReader(stream);
        var tables = new Dictionary<(GrowthIndicator, bool), (double L, double M, double S)[]>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith('#') || line.StartsWith("indicator", StringComparison.Ordinal) || line.Length == 0) continue;
            var f = line.Split(',');
            var indicator = f[0] switch { "wfa" => GrowthIndicator.WeightForAge, "lhfa" => GrowthIndicator.LengthForAge, "bfa" => GrowthIndicator.BmiForAge, _ => throw new InvalidDataException(f[0]) };
            var key = (indicator, f[1] == "M");
            if (!tables.TryGetValue(key, out var rows)) tables[key] = rows = new (double, double, double)[LastDay + 1];
            rows[int.Parse(f[2], CultureInfo.InvariantCulture)] = (Num(f[3]), Num(f[4]), Num(f[5]));
        }
        // Every indicator, both sexes, every day: a gap would read as L = M = S = 0.
        if (tables.Count != 6 || tables.Values.Any(t => t.Any(r => r.M == 0))) throw new InvalidDataException("The WHO growth tables are incomplete.");
        return tables;
    }

    static double Num(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}

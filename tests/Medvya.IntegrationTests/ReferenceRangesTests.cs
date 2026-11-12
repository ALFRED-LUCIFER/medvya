using Medvya.Encounters.Domain;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-031: every observation code has one unit and (where one exists) an adult reference range; a reading outside it is High or Low,
/// with the range in words. Pure rules, no database.
/// SAFETY, mutation check: make <see cref="ReferenceRange.Flag"/> treat a two-sided upper bound as exclusive, or drop the Low branch, and
/// <see cref="AReadingIsFlaggedAgainstItsRangeAtTheEdges"/> fails.
/// </summary>
public sealed class ReferenceRangesTests
{
    [Theory]
    [InlineData(ObservationCode.Potassium, 350, null)] // both bounds are inside "3.5–5.0"
    [InlineData(ObservationCode.Potassium, 349, VitalAlert.Low)]
    [InlineData(ObservationCode.Potassium, 500, null)]
    [InlineData(ObservationCode.Potassium, 501, VitalAlert.High)]
    [InlineData(ObservationCode.Sodium, 13300, VitalAlert.Low)]
    [InlineData(ObservationCode.LdlCholesterol, 9999, null)] // "< 100": below the upper bound
    [InlineData(ObservationCode.LdlCholesterol, 10000, VitalAlert.High)]
    [InlineData(ObservationCode.Egfr, 6000, null)] // "≥ 60": at least the lower bound
    [InlineData(ObservationCode.Egfr, 5999, VitalAlert.Low)]
    [InlineData(ObservationCode.UrineAlbuminCreatinineRatio, 3000, VitalAlert.High)]
    [InlineData(ObservationCode.Haemoglobin, 1080, VitalAlert.Low)]
    [InlineData(ObservationCode.Ferritin, 1400, VitalAlert.Low)]
    [InlineData(ObservationCode.Tsh, 39, VitalAlert.Low)]
    [InlineData(ObservationCode.VitaminD, 1800, VitalAlert.Low)]
    [InlineData(ObservationCode.Platelets, 40100, VitalAlert.High)]
    [InlineData(ObservationCode.HbA1c, 790, VitalAlert.High)]
    [InlineData(ObservationCode.FetalHeartRate, 10900, VitalAlert.Low)]
    [InlineData(ObservationCode.FetalHeartRate, 16000, null)]
    [InlineData(ObservationCode.PainScore, 300, null)]
    [InlineData(ObservationCode.PainScore, 400, VitalAlert.High)]
    [InlineData(ObservationCode.RespiratoryRate, 2100, VitalAlert.High)]
    [InlineData(ObservationCode.BloodPressureStandingSystolic, 8800, VitalAlert.Low)]
    public void AReadingIsFlaggedAgainstItsRangeAtTheEdges(ObservationCode code, int hundredths, VitalAlert? expected) =>
        Assert.Equal(expected, ReferenceRanges.For(code)!.Flag(hundredths));

    [Theory]
    [InlineData(ObservationCode.Potassium, "3.5–5.0")]
    [InlineData(ObservationCode.Sodium, "135–145")]
    [InlineData(ObservationCode.Tsh, "0.4–4.0")]
    [InlineData(ObservationCode.Temperature, "36.0–37.9")]
    [InlineData(ObservationCode.LdlCholesterol, "< 100")]
    [InlineData(ObservationCode.HbA1c, "< 5.7")]
    [InlineData(ObservationCode.Egfr, "≥ 60")]
    [InlineData(ObservationCode.Platelets, "150–400")]
    public void TheRangeIsSaidAsTheLabsWriteIt(ObservationCode code, string text) => Assert.Equal(text, ReferenceRanges.For(code)!.Text);

    [Fact]
    public void EveryCodeHasAUnitAndOnlyMeasuresWithoutAnAdultRangeHaveNone()
    {
        Assert.All(Enum.GetValues<ObservationCode>(), c => Assert.False(string.IsNullOrEmpty(Observation.Units(c))));
        Assert.Equal([ObservationCode.Weight, ObservationCode.Height, ObservationCode.FundalHeight, ObservationCode.Length, ObservationCode.HeadCircumference], Enum.GetValues<ObservationCode>().Where(c => ReferenceRanges.For(c) is null));
        Assert.All(Enum.GetValues<ObservationCode>(), c => Assert.True(c.ToString().Length <= 30)); // the column holds 30 characters
    }

    /// <summary>The chart's adult vital ranges say the same as nurse intake's adult band on every whole reading (tenths for temperature).</summary>
    [Theory]
    [InlineData(VitalKind.SystolicBp)]
    [InlineData(VitalKind.DiastolicBp)]
    [InlineData(VitalKind.Pulse)]
    [InlineData(VitalKind.Temperature)]
    [InlineData(VitalKind.OxygenSaturation)]
    [InlineData(VitalKind.RespiratoryRate)]
    [InlineData(VitalKind.Height)]
    public void TheChartsAdultVitalRangeMatchesIntakesAdultBand(VitalKind kind)
    {
        var (min, max) = VitalRules.Possible(kind);
        var step = kind == VitalKind.Temperature ? 0.1m : 1m;
        var range = ReferenceRanges.For(VitalRules.Code(kind));
        for (var v = Math.Ceiling(min); v <= max; v += step)
            Assert.True(VitalRules.Flag(kind, v, null, 18 * 12) == range?.Flag((int)(v * 100)), $"{kind} {v}");
    }

    [Theory]
    [InlineData(7000, 17500, 229)] // 70 kg, 175 cm: 22.86
    [InlineData(5600, 15200, 242)] // 56 kg, 152 cm: 24.24
    [InlineData(1600, 10100, 157)] // a child's 16 kg, 101 cm: 15.68
    public void BmiIsWorkedOutFromWeightAndHeight(int weightHundredths, int heightHundredths, int tenths) =>
        Assert.Equal(tenths, ReferenceRanges.BmiTenths(weightHundredths, heightHundredths));
}

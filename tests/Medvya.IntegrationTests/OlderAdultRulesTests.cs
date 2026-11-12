using Medvya.Encounters.Domain;
using Medvya.Prescribing.Domain;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-037, pure rules: every score is refused outside its scale, the orthostatic drop is tested at its flag line, and each medicine review
/// rule fires on its own medicine and not just below its line. SAFETY mutation checks are named on the tests.
/// </summary>
public sealed class OlderAdultRulesTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 12, 4, 30, 0, TimeSpan.Zero);

    static GeriatricAssessment Row(int? falls = null, int? tug = null, int? cfs = null, int? miniCog = null, int? adl = null, int? iadl = null, int? phq2 = null,
        (int, int, int, int)? bp = null, string? note = null) => new()
    {
        FallsLast12Months = falls, TimedUpAndGoTenths = tug, ClinicalFrailtyScale = cfs, MiniCog = miniCog, Adl = adl, Iadl = iadl, Phq2 = phq2, NutritionNote = note,
        LyingSystolic = bp?.Item1, LyingDiastolic = bp?.Item2, StandingSystolic = bp?.Item3, StandingDiastolic = bp?.Item4,
    };

    /// <summary>SAFETY (mutation check: widen or drop any one scale in GeriatricRules.Scales, and this fails).</summary>
    [Theory]
    [InlineData("clinicalFrailtyScale", 1, 9)]
    [InlineData("miniCog", 0, 5)]
    [InlineData("adl", 0, 6)]
    [InlineData("iadl", 0, 8)]
    [InlineData("phq2", 0, 6)]
    [InlineData("fallsLast12Months", 0, 99)]
    [InlineData("timedUpAndGoSeconds", 10, 1200)]
    public void EachScoreIsAcceptedOnItsScaleAndRefusedOneOutside(string field, int min, int max)
    {
        GeriatricAssessment With(int v) => field switch
        {
            "clinicalFrailtyScale" => Row(cfs: v), "miniCog" => Row(miniCog: v), "adl" => Row(adl: v), "iadl" => Row(iadl: v), "phq2" => Row(phq2: v),
            "fallsLast12Months" => Row(falls: v), _ => Row(tug: v),
        };
        Assert.Empty(GeriatricRules.Validate(With(min)));
        Assert.Empty(GeriatricRules.Validate(With(max)));
        Assert.Equal([(field, "out_of_scale")], GeriatricRules.Validate(With(min - 1)));
        Assert.Equal([(field, "out_of_scale")], GeriatricRules.Validate(With(max + 1)));
    }

    [Fact]
    public void BloodPressureNeedsAllFourPossibleReadingsAndAnEmptyReviewOrLongNoteIsRefused()
    {
        Assert.Equal([("bloodPressure", "incomplete")], GeriatricRules.Validate(new GeriatricAssessment { LyingSystolic = 140, LyingDiastolic = 80 }));
        Assert.Equal([("standingSystolic", "out_of_scale")], GeriatricRules.Validate(Row(bp: (140, 80, 400, 70))));
        Assert.Equal([("assessment", "empty")], GeriatricRules.Validate(Row(note: "  ")));
        Assert.Equal([("nutritionNote", "too_long")], GeriatricRules.Validate(Row(note: new string('x', GeriatricRules.MaxNutritionNote + 1))));
        Assert.Empty(GeriatricRules.Validate(Row(note: "Lost 3.5 kg in a year (synthetic)")));
    }

    /// <summary>SAFETY (mutation check: change either line, use &gt; for &gt;=, or join the two with "and", and this fails).</summary>
    [Fact]
    public void TheStandingDropIsOrthostaticAt20SystolicOr10DiastolicAndNotJustBelow()
    {
        Assert.Equal((20, 0, true), GeriatricRules.Drop(140, 80, 120, 80));
        Assert.Equal((19, 9, false), GeriatricRules.Drop(140, 80, 121, 71));
        Assert.Equal((0, 10, true), GeriatricRules.Drop(140, 80, 140, 70));
        Assert.Equal((24, 8, true), GeriatricRules.Drop(146, 80, 122, 72)); // the design's example
        Assert.Equal((-6, -4, false), GeriatricRules.Drop(120, 70, 126, 74)); // a rise is not a drop
    }

    /// <summary>SAFETY (mutation check: move the 12 s line or use &gt;, and this fails).</summary>
    [Fact]
    public void TimedUpAndGoIsAtRiskFrom12Seconds()
    {
        Assert.True(GeriatricRules.TugAtRisk(120));
        Assert.False(GeriatricRules.TugAtRisk(119));
    }

    [Fact]
    public void TheFlagLinesAndTheReviewRulesAreADraftAwaitingSignOff()
    {
        Assert.Null(GeriatricRules.SignOff);
        Assert.Null(MedicineReview.SignOff);
    }

    static int next;
    static ReviewMedicine Med(string? atc, int daysOnChart = 0, string name = "Synthetic medicine") =>
        new(new Guid(Interlocked.Increment(ref next), 0, 0, new byte[8]), name, atc, Now.AddDays(-daysOnChart));

    static PatientContext Older(int? egfr = 90, int age = 76) => new(age, true, null, egfr, null);

    static MedicineReviewResult Review(PatientContext patient, int? potassium, params ReviewMedicine[] chart) => MedicineReview.Evaluate(chart, patient, potassium, Now);

    /// <summary>SAFETY (mutation check: drop the benzodiazepine rule, or a prefix from the shared list, and this fails).</summary>
    [Fact]
    public void ABenzodiazepineIsSuggestedForReview()
    {
        var alprazolam = Med("N05BA12");
        var midazolam = Med("N05CD08");
        Assert.Equal([(alprazolam.Id, ReviewRule.Benzodiazepine), (midazolam.Id, ReviewRule.Benzodiazepine)],
            Review(Older(), null, alprazolam, midazolam).Suggestions.Select(s => (s.MedicineId, s.Rule)));
    }

    /// <summary>SAFETY (mutation check: drop the NSAID rule, or catch a topical gel M02A, and this fails).</summary>
    [Fact]
    public void AnOralNsaidIsSuggestedButAGelIsNot()
    {
        var ibuprofen = Med("M01AE01");
        Assert.Equal([(ibuprofen.Id, ReviewRule.SystemicNsaid)], Review(Older(), null, ibuprofen, Med("M02AA15")).Suggestions.Select(s => (s.MedicineId, s.Rule)));
    }

    /// <summary>SAFETY (mutation check: drop the PPI rule, change the 8 weeks, or count from now, and this fails).</summary>
    [Fact]
    public void APpiIsSuggestedOnlyAfterMoreThanEightWeeksOnTheChart()
    {
        var longTerm = Med("A02BC02", daysOnChart: 57);
        var suggestion = Assert.Single(Review(Older(), null, longTerm, Med("A02BC01", daysOnChart: 56)).Suggestions);
        Assert.Equal((longTerm.Id, ReviewRule.LongTermPpi, (int?)8), (suggestion.MedicineId, suggestion.Rule, suggestion.WeeksOnChart));
    }

    /// <summary>
    /// SAFETY (mutation check: drop the RAAS rule, move the eGFR or potassium line, join them with "or", or read a missing value as normal, and
    /// this fails). Both must be past their line; a missing value is reported, never treated as normal.
    /// </summary>
    [Fact]
    public void ARaasBlockerIsSuggestedWithEgfrUnder60AndPotassiumAbove5AndMissingLabsAreNotChecked()
    {
        var losartan = Med("C09CA01");
        var hit = Assert.Single(Review(Older(egfr: 59), 501, losartan).Suggestions);
        Assert.Equal((ReviewRule.RaasLowEgfrHighPotassium, (int?)59, (int?)501), (hit.Rule, hit.Egfr, hit.PotassiumHundredths));
        Assert.Empty(Review(Older(egfr: 60), 600, losartan).Suggestions);
        Assert.Empty(Review(Older(egfr: 52), 500, losartan).Suggestions);
        Assert.Equal(ReviewGap.NoEgfr, Assert.Single(Review(Older(egfr: null), 600, losartan).NotChecked).Reason);
        Assert.Equal(ReviewGap.NoPotassium, Assert.Single(Review(Older(egfr: 52), null, losartan).NotChecked).Reason);
        Assert.Empty(Review(Older(egfr: 75), null, losartan).NotChecked); // a normal eGFR needs no potassium
    }

    [Fact]
    public void AnUnmatchedMedicineIsNotCheckedAndAYoungerPatientGetsNoReview()
    {
        var unknown = Med(null, name: "Synthetic tonic");
        Assert.Equal((unknown.Id, ReviewGap.NotOnFormulary), (Assert.Single(Review(Older(), null, unknown).NotChecked).MedicineId, Review(Older(), null, unknown).NotChecked[0].Reason));
        var young = Review(Older(age: SafetyRules.OlderAdultAge - 1), null, Med("N05BA12"), unknown);
        Assert.Empty(young.Suggestions);
        Assert.Empty(young.NotChecked);
        Assert.Single(Review(Older(age: SafetyRules.OlderAdultAge), null, Med("N05BA12")).Suggestions);
    }
}

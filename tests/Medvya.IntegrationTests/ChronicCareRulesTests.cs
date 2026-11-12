using Medvya.Encounters.Domain;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-038, pure rules: every care gap rule is shown due, not due and done; unknown is due; targets meet on the right side of their limit.
/// SAFETY mutation checks are named on the tests.
/// </summary>
public sealed class ChronicCareRulesTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 12, 4, 30, 0, TimeSpan.Zero);
    static readonly TimeZoneInfo India = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, India).DateTime);

    static CareFacts Facts(bool diabetes = false, DateOnly? eye = null, DateOnly? foot = null, Dictionary<ObservationCode, DateTimeOffset>? taken = null,
        PlacedOrderFact[]? orders = null, DueDoseFact[]? doses = null, int? gestation = null, bool ogttOrdered = false, FallsFact[]? falls = null) =>
        new(diabetes, eye, foot, taken ?? [], orders ?? [], doses ?? [], gestation, ogttOrdered, falls ?? []);

    static CareGapRule[] Rules(CareFacts f) => [.. CareGapRules.Due(f, Now, India).Select(g => g.Rule)];

    [Theory]
    [InlineData("E11", true)]
    [InlineData("E11.42", true)]
    [InlineData("e10.9", true)]
    [InlineData("E14", true)]
    [InlineData("E15", false)]
    [InlineData("E09", false)]
    [InlineData("E1", false)]
    [InlineData("I10", false)]
    [InlineData(null, false)]
    public void DiabetesIsE10ToE14(string? code, bool diabetes) => Assert.Equal(diabetes, CareGapRules.IsDiabetes(code));

    [Theory]
    [InlineData("I10", true)]
    [InlineData("I15.0", true)]
    [InlineData("I16", false)]
    [InlineData("E11", false)]
    public void HypertensionIsI10ToI15(string code, bool hypertension) => Assert.Equal(hypertension, CareGapRules.IsHypertension(code));

    /// <summary>SAFETY (mutation check: close the eye or foot gap without a record, or widen the year, and this fails).</summary>
    [Fact]
    public void EyeAndFootChecksAreDueForDiabetesWithoutARecordFromTheLastYear()
    {
        Assert.DoesNotContain(CareGapRule.DiabeticEyeExam, Rules(Facts(diabetes: false))); // not due: no diabetes
        Assert.Equal([CareGapRule.DiabeticEyeExam, CareGapRule.DiabeticFootExam], Rules(Facts(diabetes: true)).Take(2)); // unknown is due
        var overdue = CareGapRules.Due(Facts(diabetes: true, eye: Today.AddYears(-1).AddDays(-1), foot: Today.AddYears(-1).AddDays(-1)), Now, India);
        Assert.Equal((CareGapAction.Refer, Today.AddYears(-1).AddDays(-1)), (overdue[0].Action, overdue[0].LastDone));
        Assert.Equal(CareGapAction.AddToVisit, overdue[1].Action);
        // done: a check exactly a year ago still counts
        Assert.DoesNotContain(CareGapRule.DiabeticEyeExam, Rules(Facts(diabetes: true, eye: Today.AddYears(-1))));
        Assert.DoesNotContain(CareGapRule.DiabeticFootExam, Rules(Facts(diabetes: true, foot: Today.AddDays(-3))));
    }

    /// <summary>SAFETY (mutation check: count "not examined" or a text examination as a foot check, and this fails).</summary>
    [Theory]
    [InlineData("""{"feet":{"answer":"normal"}}""", true)]
    [InlineData("""{"heart":{"answer":"normal"},"feet":{"answer":"abnormal","note":"Reduced monofilament sensation (synthetic)"}}""", true)]
    [InlineData("""{"feet":{"answer":"notExamined"}}""", false)]
    [InlineData("""{"heart":{"answer":"normal"}}""", false)]
    [InlineData("Feet normal (a version 1 free-text note)", false)]
    [InlineData(null, false)]
    public void ASignedExaminationOfTheFeetIsAFootCheck(string? examination, bool examined) => Assert.Equal(examined, CareGapRules.FeetExamined(examination));

    /// <summary>SAFETY (mutation check: count a UACR older than a year, or ask for none, and this fails).</summary>
    [Fact]
    public void UrineAlbuminIsDueOnceAYearForDiabetes()
    {
        Assert.DoesNotContain(CareGapRule.UrineAlbumin, Rules(Facts(diabetes: false)));
        var due = Assert.Single(CareGapRules.Due(Facts(diabetes: true, eye: Today, foot: Today), Now, India));
        Assert.Equal((CareGapRule.UrineAlbumin, CareGapAction.Order, CareGapRules.UacrName, (DateOnly?)null), (due.Rule, due.Action, due.Item, due.LastDone));
        Assert.Contains(CareGapRule.UrineAlbumin, Rules(Facts(diabetes: true, taken: new() { [ObservationCode.UrineAlbuminCreatinineRatio] = Now.AddYears(-1).AddMinutes(-1) })));
        Assert.DoesNotContain(CareGapRule.UrineAlbumin, Rules(Facts(diabetes: true, taken: new() { [ObservationCode.UrineAlbuminCreatinineRatio] = Now.AddMonths(-11) })));
    }

    /// <summary>SAFETY (mutation check: count an order as resulted without a reading taken after it, or skip unmatched tests, and this fails).</summary>
    [Fact]
    public void ATestOrderedButNeverResultedIsDueAfterTheWait()
    {
        var hba1c = new PlacedOrderFact("HbA1c", Now.AddDays(-CareGapRules.ResultWaitDays));
        var gap = Assert.Single(CareGapRules.Due(Facts(orders: [hba1c]), Now, India));
        Assert.Equal((CareGapRule.OrderNotResulted, CareGapAction.Order, "HbA1c"), (gap.Rule, gap.Action, gap.Item));
        // not due: still inside the wait
        Assert.Empty(Rules(Facts(orders: [hba1c with { OrderedAt = Now.AddDays(-CareGapRules.ResultWaitDays).AddMinutes(1) }])));
        // a reading taken before the order does not result it; one taken after does (case and spaces in the name ignored)
        Assert.Single(Rules(Facts(orders: [hba1c], taken: new() { [ObservationCode.HbA1c] = hba1c.OrderedAt.AddMinutes(-1) })));
        Assert.Empty(Rules(Facts(orders: [hba1c with { Name = " hba1c " }], taken: new() { [ObservationCode.HbA1c] = hba1c.OrderedAt.AddDays(3) })));
        // a test no reading can be matched to stays due: unknown is due
        Assert.Single(Rules(Facts(orders: [new("Chest X-ray", Now.AddDays(-30))], taken: new() { [ObservationCode.HbA1c] = Now })));
        // a re-order counts once, by the latest order; an order older than a year has dropped off
        Assert.Empty(Rules(Facts(orders: [hba1c, hba1c with { OrderedAt = Now.AddDays(-2) }])));
        Assert.Empty(Rules(Facts(orders: [hba1c with { OrderedAt = Now.AddYears(-1).AddDays(-1) }])));
    }

    [Fact]
    public void EveryVaccineDoseDueIsAGapAndNoneWhenNoneIsDue()
    {
        Assert.Empty(Rules(Facts()));
        var gaps = CareGapRules.Due(Facts(doses: [new("DTP", 5, Today.AddDays(-10)), new("MMR", 3, Today)]), Now, India);
        Assert.Equal([("DTP", 5, CareGapAction.AddToVisit), ("MMR", 3, CareGapAction.AddToVisit)], gaps.Select(g => (g.Vaccine!, g.Dose!.Value, g.Action)));
        Assert.Equal("DTP dose 5", gaps[0].Item);
    }

    /// <summary>SAFETY (mutation check: move either edge of the 24–28 week window, and this fails).</summary>
    [Theory]
    [InlineData(null, false, false)]
    [InlineData(24 * 7 - 1, false, false)]
    [InlineData(24 * 7, false, true)]
    [InlineData(28 * 7 + 6, false, true)]
    [InlineData(29 * 7, false, false)]
    [InlineData(26 * 7, true, false)] // done: ordered in this pregnancy (the order rule follows it)
    public void TheGlucoseToleranceTestIsDueFrom24To28Weeks(int? gestationDays, bool ordered, bool due) =>
        Assert.Equal(due, Rules(Facts(gestation: gestationDays, ogttOrdered: ordered)).Contains(CareGapRule.GlucoseToleranceTest));

    /// <summary>SAFETY (mutation check: close the falls gap with only one of the two measures, or on a review before the fall, and this fails).</summary>
    [Fact]
    public void AFallNeedsAReviewWithTheWalkingTestAndTheStandingDrop()
    {
        Assert.Empty(Rules(Facts(falls: [new(Now.AddDays(-5), 0, false, false)]))); // no fall
        Assert.Empty(Rules(Facts(falls: [new(Now.AddDays(-5), null, true, true)]))); // falls not asked: no fall known
        var fall = new FallsFact(Now.AddDays(-20), 1, TugMeasured: true, StandingDropMeasured: false);
        Assert.Equal([CareGapRule.FallsReview], Rules(Facts(falls: [fall])));
        Assert.Equal([CareGapRule.FallsReview], Rules(Facts(falls: [fall, new(Now.AddDays(-40), 0, true, true)]))); // a review before the fall
        Assert.Empty(Rules(Facts(falls: [fall, new(Now.AddDays(-2), 0, true, true)]))); // done later
        Assert.Empty(Rules(Facts(falls: [fall with { StandingDropMeasured = true }]))); // done in the same review
        Assert.Empty(Rules(Facts(falls: [fall with { AssessedAt = Now.AddYears(-1).AddDays(-1) }]))); // older than a year
    }

    /// <summary>SAFETY (mutation check: flip a direction, or meet at the limit for a "below" target, and this fails).</summary>
    [Fact]
    public void TargetsMeetOnTheirSideOfTheLimitAndAnOverrideStaysInItsRange()
    {
        var (hba1c, egfr) = (CareTargets.Defaults[ObservationCode.HbA1c], CareTargets.Defaults[ObservationCode.Egfr]);
        Assert.Equal((TargetDirection.Below, 700, TargetDirection.AtLeast, 6000), (hba1c.Direction, hba1c.DefaultHundredths, egfr.Direction, egfr.DefaultHundredths));
        Assert.True(CareTargets.Met(hba1c.Direction, 699, 700));
        Assert.False(CareTargets.Met(hba1c.Direction, 700, 700));
        Assert.True(CareTargets.Met(egfr.Direction, 6000, 6000));
        Assert.False(CareTargets.Met(egfr.Direction, 5999, 6000));
        Assert.True(CareTargets.Allowed(ObservationCode.HbA1c, 750));
        Assert.False(CareTargets.Allowed(ObservationCode.HbA1c, 1100));
        Assert.False(CareTargets.Allowed(ObservationCode.Pulse, 6000));
        Assert.Equal(6, CareTargets.Defaults.Count);
    }
}

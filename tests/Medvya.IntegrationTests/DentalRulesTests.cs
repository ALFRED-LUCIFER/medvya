using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P8-011 and MDV-P8-012, pure rules: FDI teeth (permanent and primary, nothing else), surfaces in canonical order and only on the conditions that
/// take them, the plan's status moves (Done is final), the estimate total without declined items, and the acceptance key that tells a changed plan.
/// SAFETY mutation checks are named on the tests.
/// </summary>
public sealed class DentalRulesTests
{
    /// <summary>SAFETY (mutation check: add a tooth such as "19" or "56" to the FDI lists, or drop a quadrant, and this fails).</summary>
    [Fact]
    public void OnlyFdiTeethAreTeeth()
    {
        Assert.Equal(32, DentalRules.PermanentTeeth.Count);
        Assert.Equal(20, DentalRules.PrimaryTeeth.Count);
        foreach (var t in new[] { "11", "18", "21", "28", "31", "38", "41", "48", "51", "55", "61", "65", "71", "75", "81", "85" }) Assert.True(DentalRules.IsTooth(t), t);
        foreach (var t in new[] { "10", "19", "29", "49", "50", "56", "66", "76", "86", "91", "1", "036", "3 6", "", null }) Assert.False(DentalRules.IsTooth(t), t ?? "null");
    }

    [Theory]
    [InlineData("OD", "DO")]
    [InlineData("lbodm", "MDOBL")]
    [InlineData(" mo ", "MO")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SurfacesAreStoredInCanonicalOrder(string? text, string expected) => Assert.Equal(expected, DentalRules.Surfaces(text));

    [Theory]
    [InlineData("OO")]
    [InlineData("X")]
    [InlineData("M,D")]
    public void ARepeatedOrUnknownSurfaceIsNotASurface(string text) => Assert.Null(DentalRules.Surfaces(text));

    /// <summary>SAFETY (mutation check: take Caries or Filling out of SurfaceConditions, or stop checking surfaces against the condition, and this fails).</summary>
    [Fact]
    public void CariesAndFillingsNeedASurfaceAndWholeToothConditionsTakeNone()
    {
        Assert.Empty(DentalRules.Validate("36", ToothCondition.Caries, "OD", null));
        Assert.Equal([("surfaces", "required")], DentalRules.Validate("36", ToothCondition.Filling, "", null));
        Assert.Equal([("surfaces", "not_allowed")], DentalRules.Validate("46", ToothCondition.Missing, "O", null));
        foreach (var whole in Enum.GetValues<ToothCondition>().Except([ToothCondition.Caries, ToothCondition.Filling]))
        {
            Assert.Empty(DentalRules.Validate("21", whole, null, null));
            Assert.Equal([("surfaces", "not_allowed")], DentalRules.Validate("21", whole, "M", null));
        }
    }

    [Fact]
    public void EveryFieldErrorIsReportedAtOnce()
    {
        var errors = DentalRules.Validate("99", (ToothCondition)42, "QQ", new string('x', DentalRules.MaxNote + 1));
        Assert.Equal([("condition", "invalid"), ("note", "too_long"), ("surfaces", "invalid"), ("tooth", "invalid")], errors.OrderBy(e => e.Field));
        Assert.Equal([("condition", "invalid")], DentalRules.Validate("36", null, null, null));
    }

    [Fact]
    public void APlanItemTakesAnOptionalToothAPhaseFromOneToNineAndSurfacesOnlyWithATooth()
    {
        Assert.Empty(DentalRules.ValidatePlanItem(null, null, 1));
        Assert.Empty(DentalRules.ValidatePlanItem("36", "MO", 9));
        Assert.Equal([("phase", "range")], DentalRules.ValidatePlanItem("36", null, 0));
        Assert.Equal([("phase", "range")], DentalRules.ValidatePlanItem("36", null, 10));
        Assert.Equal([("phase", "range")], DentalRules.ValidatePlanItem("36", null, null));
        Assert.Equal([("surfaces", "needs_tooth")], DentalRules.ValidatePlanItem(null, "O", 1));
        Assert.Equal([("tooth", "invalid")], DentalRules.ValidatePlanItem("59", null, 1));
    }

    /// <summary>SAFETY (mutation check: allow any move out of Done, or Planned straight to Done, in DentalRules.CanMove, and this fails).</summary>
    [Fact]
    public void DoneIsFinalAndAnItemIsStartedBeforeItIsDone()
    {
        var all = Enum.GetValues<DentalPlanStatus>();
        var allowed = all.SelectMany(from => all.Where(to => DentalRules.CanMove(from, to)).Select(to => (from, to))).ToList();
        Assert.Equal(
        [
            (DentalPlanStatus.Planned, DentalPlanStatus.InProgress), (DentalPlanStatus.Planned, DentalPlanStatus.Declined),
            (DentalPlanStatus.InProgress, DentalPlanStatus.Done), (DentalPlanStatus.InProgress, DentalPlanStatus.Declined),
            (DentalPlanStatus.Declined, DentalPlanStatus.Planned),
        ], allowed);
    }

    static DentalPlanItem Item(long minor, DentalPlanStatus status, Guid? id = null) => new()
    {
        Id = id ?? Guid.CreateVersion7(),
        ServiceName = "Synthetic",
        Currency = "INR",
        EstimateMinor = minor,
        Status = status,
        Phase = 1,
    };

    [Fact]
    public void TheEstimateLeavesDeclinedItemsOut()
    {
        Assert.Equal(650_000 + 1_200_000, DentalRules.EstimateTotal([Item(650_000, DentalPlanStatus.Planned), Item(1_200_000, DentalPlanStatus.Done), Item(150_000, DentalPlanStatus.Declined)]));
        Assert.Equal(0, DentalRules.EstimateTotal([]));
    }

    /// <summary>SAFETY (mutation check: leave the price or the declined filter out of DentalRules.EstimateKey, and this fails: a changed plan would look accepted).</summary>
    [Fact]
    public void TheAcceptanceKeyChangesWhenAnEstimatedItemOrItsPriceChangesButNotWithStatusAlone()
    {
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var key = DentalRules.EstimateKey([Item(650_000, DentalPlanStatus.Planned, a), Item(1_200_000, DentalPlanStatus.Planned, b)]);
        Assert.Equal(64, key.Length);
        Assert.Equal(key, DentalRules.EstimateKey([Item(1_200_000, DentalPlanStatus.Done, b), Item(650_000, DentalPlanStatus.InProgress, a)])); // order and progress: same estimate
        Assert.NotEqual(key, DentalRules.EstimateKey([Item(650_000, DentalPlanStatus.Planned, a), Item(1_300_000, DentalPlanStatus.Planned, b)])); // a price changed
        Assert.NotEqual(key, DentalRules.EstimateKey([Item(650_000, DentalPlanStatus.Planned, a), Item(1_200_000, DentalPlanStatus.Declined, b)])); // an item declined
        Assert.NotEqual(key, DentalRules.EstimateKey([Item(650_000, DentalPlanStatus.Planned, a)])); // an item fewer
    }

    [Fact]
    public void TheChartRulesAreADraftUntilADentistSignsThem() => Assert.Null(DentalRules.SignOff);
    /// <summary>SAFETY (mutation check: swap the M and D zones, or stop mirroring the lower left quadrant, and this fails: the printed caries would be on the wrong side).</summary>
    [Fact]
    public void ThePrintedChartPaintsOnlyTheRecordedSurfacesOnTheirSide()
    {
        var caries = new ToothFindingView(Guid.NewGuid(), "36", ToothCondition.Caries, "DO", null, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        var svg = System.Xml.Linq.XElement.Parse(Medvya.Encounters.Infrastructure.DentalChartPdf.Glyph("36", caries));
        System.Xml.Linq.XNamespace ns = "http://www.w3.org/2000/svg";
        var painted = svg.Descendants(ns + "polygon").Select((p, i) => (Surface: "BMLDO"[i], Fill: (string?)p.Attribute("fill"))).Where(z => z.Fill == "url(#p)").Select(z => z.Surface);
        Assert.Equal("DO", string.Concat(painted.Order()));
        Assert.Matches(@"scale\(-[\d.]+ -[\d.]+\)", (string?)svg.Element(ns + "g")!.Attribute("transform")); // lower left: mirrored both ways
        Assert.Contains("patternTransform=\"rotate(45)\"", svg.ToString(System.Xml.Linq.SaveOptions.DisableFormatting), StringComparison.Ordinal); // hatched, not colour alone

        var blank = Medvya.Encounters.Infrastructure.DentalChartPdf.Glyph("54", null);
        Assert.Contains("stroke-dasharray", blank, StringComparison.Ordinal); // unrecorded is dashed, never drawn as healthy
        Assert.Equal(Outline(Medvya.Encounters.Infrastructure.DentalChartPdf.Glyph("16", null)), Outline(blank)); // primary 4s and 5s are molars
        static string Outline(string glyph) => System.Text.RegularExpressions.Regex.Match(glyph, "<clipPath id=\"c\"><path d=\"([^\"]+)\"").Groups[1].Value;
    }
}

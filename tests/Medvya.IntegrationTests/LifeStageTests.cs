using System.Globalization;
using Medvya.Encounters.Domain;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-032: the life stage rule at its edges and the history sections each stage offers. Pure rules, no database.
/// SAFETY, mutation check: compare ages with &gt;= instead of &gt; for the child line (or &lt; for the older-adult line), or let the age win
/// over an open pregnancy, and <see cref="TheLifeStageFollowsAgeAndAnOpenPregnancy"/> fails.
/// </summary>
public sealed class LifeStageTests
{
    [Theory]
    [InlineData("2008-11-13", "2026-11-12", false, LifeStage.Child)] // 17 y 364 d
    [InlineData("2008-11-12", "2026-11-12", false, LifeStage.Adult)] // the 18th birthday
    [InlineData("2026-11-01", "2026-11-12", false, LifeStage.Child)] // a newborn
    [InlineData("1961-11-13", "2026-11-12", false, LifeStage.Adult)] // 64 y 364 d
    [InlineData("1961-11-12", "2026-11-12", false, LifeStage.OlderAdult)] // the 65th birthday
    [InlineData("1990-05-01", "2026-11-12", true, LifeStage.Pregnancy)] // an open pregnancy wins over adult
    [InlineData("2010-05-01", "2026-11-12", true, LifeStage.Pregnancy)] // and over child: the pregnancy module is never hidden
    [InlineData("2008-02-29", "2026-02-28", false, LifeStage.Adult)] // born on 29 Feb: adult on 28 Feb in a common year
    public void TheLifeStageFollowsAgeAndAnOpenPregnancy(string born, string today, bool openPregnancy, LifeStage expected) =>
        Assert.Equal(expected, LifeStages.Of(DateOnly.Parse(born, CultureInfo.InvariantCulture), DateOnly.Parse(today, CultureInfo.InvariantCulture), openPregnancy));

    [Fact]
    public void TheSectionsOfferedDependOnTheLifeStage()
    {
        Assert.Contains(HistorySection.Birth, HistoryRules.Sections(LifeStage.Child, female: true));
        Assert.DoesNotContain(HistorySection.Function, HistoryRules.Sections(LifeStage.Child, female: true));
        Assert.Contains(HistorySection.Function, HistoryRules.Sections(LifeStage.OlderAdult, female: false));
        Assert.Contains(HistorySection.Falls, HistoryRules.Sections(LifeStage.OlderAdult, female: false));
        Assert.DoesNotContain(HistorySection.Birth, HistoryRules.Sections(LifeStage.Adult, female: true));
        Assert.Contains(HistorySection.Obstetric, HistoryRules.Sections(LifeStage.Pregnancy, female: true));
        Assert.Contains(HistorySection.WomensHealth, HistoryRules.Sections(LifeStage.Adult, female: true));
        Assert.DoesNotContain(HistorySection.WomensHealth, HistoryRules.Sections(LifeStage.Adult, female: false));
        Assert.All(Enum.GetValues<LifeStage>(), s => Assert.DoesNotContain(HistorySection.BloodGroup, HistoryRules.Sections(s, female: true))); // a banner field
    }
}

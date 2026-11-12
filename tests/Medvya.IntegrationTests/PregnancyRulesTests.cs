using Medvya.Encounters.Domain;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-034: the pregnancy dates and the contact plan. Pure rules, no database. SAFETY, mutation checks: change 280 days, let a scan inside
/// the ACOG line replace the LMP date (or one outside it keep it), or link a visit to the later contact on a tie, and a test here fails.
/// </summary>
public sealed class PregnancyRulesTests
{
    [Fact]
    public void LmpOf19May2026GivesDueDate23Feb2027And24Weeks3DaysOn6Nov2026()
    {
        var (due, method, reason) = PregnancyRules.Date(new(2026, 5, 19), null, null);
        Assert.Equal((new DateOnly(2027, 2, 23), DatingMethod.Lmp, DatingReason.LmpOnly), (due, method, reason));
        var days = PregnancyRules.GestationDays(due, new(2026, 11, 6));
        Assert.Equal((24, 3), (days / 7, days % 7));
    }

    [Theory]
    // A scan at 10 w 0 d: the LMP date is kept up to 7 days' difference, the scan's is used beyond it.
    [InlineData(70, 7, DatingMethod.Lmp, DatingReason.ScanAgrees)]
    [InlineData(70, 8, DatingMethod.Scan, DatingReason.ScanDiffers)]
    [InlineData(62, 5, DatingMethod.Lmp, DatingReason.ScanAgrees)] // 8 w 6 d: 5 days
    [InlineData(62, 6, DatingMethod.Scan, DatingReason.ScanDiffers)]
    [InlineData(112, 10, DatingMethod.Lmp, DatingReason.ScanAgrees)] // 16 w 0 d: 10 days
    [InlineData(112, 11, DatingMethod.Scan, DatingReason.ScanDiffers)]
    [InlineData(154, 14, DatingMethod.Lmp, DatingReason.ScanAgrees)] // 22 w 0 d: 14 days
    [InlineData(196, 21, DatingMethod.Lmp, DatingReason.ScanAgrees)] // 28 w 0 d: 21 days
    [InlineData(196, 22, DatingMethod.Scan, DatingReason.ScanDiffers)]
    public void AScanReplacesTheLmpDateOnlyBeyondTheDatingRule(int scanGestationDays, int scanEarlierByDays, DatingMethod method, DatingReason reason)
    {
        var lmp = new DateOnly(2026, 5, 19);
        // The scan shows a pregnancy further on than the LMP says, so its due date is earlier by the given days.
        var scanDate = lmp.AddDays(scanGestationDays - scanEarlierByDays);
        var dated = PregnancyRules.Date(lmp, scanDate, scanGestationDays);
        Assert.Equal((method, reason), (dated.Method, dated.Reason));
        Assert.Equal(method == DatingMethod.Lmp ? lmp.AddDays(280) : lmp.AddDays(280 - scanEarlierByDays), dated.Due);
    }

    [Fact]
    public void AScanAloneDatesThePregnancy() =>
        Assert.Equal((new DateOnly(2027, 2, 23), DatingMethod.Scan, DatingReason.ScanOnly), PregnancyRules.Date(null, new(2026, 7, 28), 70));

    [Fact]
    public void ContactsAreDoneTodayToComeOrMissedAndAVisitLinksToTheNearest()
    {
        var due = new DateOnly(2027, 2, 23);
        var lmp = due.AddDays(-280);
        Guid booking = Guid.NewGuid(), tie = Guid.NewGuid(), before = Guid.NewGuid();
        // 8 w 2 d -> 8; 14 w 0 d is a tie between 12 and 16 -> 12; a visit before the pregnancy is ignored.
        var contacts = PregnancyRules.Contacts(due, new(2026, 11, 6), [(booking, lmp.AddDays(58)), (tie, lmp.AddDays(98)), (before, lmp.AddDays(-3))]);
        Assert.Equal(PregnancyRules.ContactWeeks, contacts.Select(c => c.Week));
        Assert.Equal((ContactState.Done, booking), (contacts[0].State, contacts[0].VisitId!.Value));
        Assert.Equal((ContactState.Done, tie), (contacts[1].State, contacts[1].VisitId!.Value));
        Assert.Equal([ContactState.Missed, ContactState.Missed, ContactState.Today, ContactState.ToCome], contacts.Skip(2).Take(4).Select(c => c.State));
        Assert.All(contacts.Skip(5), c => Assert.Equal(ContactState.ToCome, c.State));
    }

    [Fact]
    public void ANotPregnantStatementHoldsOnlyOnTheDayItWasMade()
    {
        Assert.True(PregnancyRules.RuledOutHolds(new(2026, 11, 6), new(2026, 11, 6)));
        Assert.False(PregnancyRules.RuledOutHolds(new(2026, 11, 5), new(2026, 11, 6)));
    }

    [Fact]
    public void TheRulesAwaitClinicalSignOff() => Assert.Null(PregnancyRules.SignOff);
}

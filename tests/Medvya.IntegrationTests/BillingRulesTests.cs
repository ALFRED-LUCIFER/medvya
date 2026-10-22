using Medvya.Billing.Application;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P4-001 money arithmetic, rounding, amount in words and number formats: pure rules, no database.</summary>
public class BillingRulesTests
{
    static Money Inr(long minor) => new(minor, Currency.Inr);
    static Money Aed(long minor) => new(minor, Currency.Aed);

    [Fact]
    public void MoneyAddsAndSubtractsInMinorUnits()
    {
        Assert.Equal(Inr(160_000), Inr(80_000) + Inr(35_000) + Inr(45_000));
        Assert.Equal(Inr(60_000), Inr(110_000) - Inr(50_000));
        Assert.Equal(Inr(-60_000), -Inr(60_000));
        Assert.Equal(Aed(36_000), Aed(12_000).Times(3));
    }

    [Fact]
    public void MoneyNeverMixesCurrencies()
    {
        Assert.Throws<InvalidOperationException>(() => Inr(100) + Aed(100));
        Assert.Throws<InvalidOperationException>(() => Inr(100) - Aed(100));
        Assert.Throws<ArgumentNullException>(() => null! + Inr(100));
        Assert.Throws<ArgumentNullException>(() => null! - Inr(100));
        Assert.Throws<ArgumentNullException>(() => Inr(100) + null!);
        Assert.Throws<ArgumentOutOfRangeException>(() => Currency.From("USD"));
        Assert.Same(Currency.Aed, Currency.From("AED"));
    }

    [Fact]
    public void MoneyOverflowThrowsInsteadOfWrapping()
    {
        Assert.Throws<OverflowException>(() => Inr(long.MaxValue) + Inr(1));
        Assert.Throws<OverflowException>(() => Inr(long.MinValue) - Inr(1));
        Assert.Throws<OverflowException>(() => -Inr(long.MinValue));
        Assert.Throws<OverflowException>(() => Inr(long.MaxValue / 2 + 1).Times(2));
        Assert.Throws<OverflowException>(() => Inr(long.MaxValue).ApplyRate(10_001));
        Assert.Equal(Inr(long.MaxValue), Inr(long.MaxValue).ApplyRate(10_000)); // the intermediate product does not overflow
        Assert.Equal(Inr(long.MaxValue), Inr(-1) - Inr(long.MinValue)); // the exact edge still fits
    }

    [Theory]
    [InlineData(100_000, 1800, 18_000)] // 18 % GST of ₹1,000
    [InlineData(10_000, 500, 500)]      // 5 % VAT of AED 100
    [InlineData(3, 1800, 1)]            // 0.54 paisa → 1
    [InlineData(2, 1800, 0)]            // 0.36 → 0
    [InlineData(10, 500, 1)]            // exactly half → away from zero
    [InlineData(30, 500, 2)]            // 1.5 → 2 (truncation would give 1)
    [InlineData(50, 500, 3)]            // 2.5 → 3 (banker's rounding would give 2)
    [InlineData(29, 500, 1)]            // 1.45 → 1
    [InlineData(-10, 500, -1)]          // credit notes round symmetrically
    [InlineData(-50, 500, -3)]
    public void RatesRoundHalfAwayFromZeroToTheMinorUnit(long minor, int basisPoints, long expected)
    {
        Assert.Equal(Inr(expected), Inr(minor).ApplyRate(basisPoints));
        Assert.Equal(Aed(expected), Aed(minor).ApplyRate(basisPoints));
    }

    [Theory]
    // Design examples (Medvya Clinician App, P-15 receipts): INV-3380 total ₹1,600, INV-3381 ₹600 after discount,
    // INV-3379 ₹1,200, India and UAE advance receipts, UAE co-pay AED 50.
    [InlineData("IN", 160_000, "Rupees one thousand six hundred only")]
    [InlineData("IN", 60_000, "Rupees six hundred only")]
    [InlineData("IN", 120_000, "Rupees one thousand two hundred only")]
    [InlineData("IN", 200_000, "Rupees two thousand only")]
    [InlineData("AE", 60_000, "UAE dirhams six hundred only")]
    [InlineData("AE", 5_000, "UAE dirhams fifty only")]
    // Indian grouping (lakh, crore) and paise; international grouping and fils for AED.
    [InlineData("IN", 123_456_789, "Rupees twelve lakh thirty-four thousand five hundred and sixty-seven and eighty-nine paise only")]
    [InlineData("IN", 1_234_567_800, "Rupees one crore twenty-three lakh forty-five thousand six hundred and seventy-eight only")]
    [InlineData("IN", 25_005, "Rupees two hundred and fifty and five paise only")]
    [InlineData("IN", 0, "Rupees zero only")]
    [InlineData("IN", -60_000, "Rupees six hundred only")] // credit note: the sign is the document's, not the words'
    [InlineData("AE", 125_000_050, "UAE dirhams one million two hundred and fifty thousand and fifty fils only")]
    [InlineData("AE", 1_101_900, "UAE dirhams eleven thousand nineteen only")]
    public void AmountInWords(string country, long minor, string expected)
    {
        var rules = BillingCountryRules.For(country);
        Assert.Equal(expected, rules.AmountInWords(new Money(minor, rules.Currency)));
    }

    [Fact]
    public void AmountInWordsRefusesTheOtherCountrysCurrency()
    {
        Assert.Throws<InvalidOperationException>(() => BillingCountryRules.India.AmountInWords(Aed(100)));
        Assert.Throws<InvalidOperationException>(() => BillingCountryRules.Uae.AmountInWords(Inr(100)));
    }

    [Theory]
    [InlineData("2026-03-31T18:29:59Z", "25-26")] // 31 Mar 23:59:59 India time
    [InlineData("2026-03-31T18:30:00Z", "26-27")] // 1 Apr 00:00 India time
    [InlineData("2026-10-21T08:00:00Z", "26-27")]
    [InlineData("2027-01-15T00:00:00Z", "26-27")]
    [InlineData("2099-12-31T12:00:00Z", "99-00")]
    public void IndiaFinancialYearRunsAprilToMarchInIndiaTime(string issuedAt, string expected) =>
        Assert.Equal(expected, BillingCountryRules.India.FinancialYear(DateTimeOffset.Parse(issuedAt, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void UaeSeriesIsContinuous()
    {
        Assert.Equal("", BillingCountryRules.Uae.FinancialYear(new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero)));
        Assert.Equal("", BillingCountryRules.Uae.FinancialYear(new DateTimeOffset(2027, 1, 1, 1, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void NumbersMatchTheDesignFormats()
    {
        Assert.Equal("INV/25-26/03376", BillingCountryRules.India.Format("INV", "25-26", 3376));
        Assert.Equal("RCT/25-26/00001", BillingCountryRules.India.Format("RCT", "25-26", 1));
        Assert.Equal("TI-AD-03381", BillingCountryRules.Uae.Format("TI-AD", "", 3381));
        Assert.Equal("CN-AD-00042", BillingCountryRules.Uae.Format("CN-AD", "", 42));
    }

    [Fact]
    public void IndiaNumbersNeverExceedTheGstSixteenCharacters()
    {
        Assert.Equal("INV/25-26/100000", BillingCountryRules.India.Format("INV", "25-26", 100_000));
        Assert.Throws<InvalidOperationException>(() => BillingCountryRules.India.Format("ADVR", "25-26", 100_000));
    }

    [Theory]
    [InlineData("IN", "INV", true)]
    [InlineData("IN", "ADVR", true)]
    [InlineData("IN", "INVOI", false)] // would leave no room for five digits within 16 characters
    [InlineData("IN", "inv", false)]
    [InlineData("IN", "IN/V", false)]
    [InlineData("IN", "", false)]
    [InlineData("AE", "TI-AD", true)]
    [InlineData("AE", "RC-DXB-2", true)]
    [InlineData("AE", "TI_AD", false)]
    [InlineData("AE", "TI-", false)]
    public void PrefixesAreChecked(string country, string prefix, bool valid)
    {
        var check = () => BillingCountryRules.For(country).CheckPrefix(prefix);
        if (valid) check();
        else Assert.Throws<ArgumentException>(check);
    }
}

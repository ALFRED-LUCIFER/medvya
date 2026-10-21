using System.Globalization;
using System.Text.RegularExpressions;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Billing.Application;

/// <summary>
/// UAE (VAT): dirhams and fils; printed PREFIX-00042 (design: TI-AD-03381, RC-AD-03381, CN-AD-0042). The FTA asks for a
/// sequential, unique tax invoice number; the design's numbers carry no year, so the series is continuous and never resets
/// (a yearly reset would repeat numbers). The prefix carries the emirate (AD).
/// </summary>
sealed partial class UaeBillingRules : BillingCountryRules
{
    static readonly (long, string)[] Groups = [(1_000_000_000, "billion"), (1_000_000, "million"), (1_000, "thousand")];

    public override string CountryCode => "AE";
    public override Currency Currency => Currency.Aed;

    public override string FinancialYear(DateTimeOffset issuedAt) => "";

    public override string Format(string prefix, string financialYear, long number) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}-{number:D5}");

    public override void CheckPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (!Prefix().IsMatch(prefix)) throw new ArgumentException("Prefix must be capital letters or digits in up to three hyphen-separated parts.", nameof(prefix));
    }

    public override string AmountInWords(Money amount)
    {
        var (dirhams, fils) = Split(amount);
        var words = "UAE dirhams " + Spell(dirhams, Groups);
        return (fils == 0 ? words : words + " and " + Spell(fils, Groups) + " fils") + " only";
    }

    [GeneratedRegex("^[A-Z0-9]{1,4}(-[A-Z0-9]{1,4}){0,2}$")] private static partial Regex Prefix();
}

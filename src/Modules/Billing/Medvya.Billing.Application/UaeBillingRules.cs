using System.Globalization;
using System.Text.RegularExpressions;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Billing.Application;

/// <summary>
/// UAE (VAT): dirhams and fils ("fils" is singular and plural); printed PREFIX-00042 (design: TI-AD-03381, RC-AD-03381,
/// CN-AD-0042). Decided: one continuous, gap-free series that never resets. VAT Executive Regulation Article 59 asks for a
/// sequential number, or a unique number identifying the invoice and its order, and the FTA flags gaps
/// (https://www.mondaq.com/sales-taxes-vat-gst/670546/tax-invoice-under-uae-vat-law,
/// https://www.wafeq.com/en-ae/tax-and-reporting/vat-invoice-requirements-in-uae). The prefix carries the emirate (AD).
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

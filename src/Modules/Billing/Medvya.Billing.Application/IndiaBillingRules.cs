using System.Globalization;
using System.Text.RegularExpressions;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Billing.Application;

/// <summary>
/// India (GST): rupees and paise; one series per prefix per financial year (1 April–31 March, India time), printed
/// PREFIX/25-26/00042. CGST Rules 46(b), 50 and 53 ask for a consecutive serial number of at most 16 characters
/// (letters, digits, "/" and "-"), unique for the financial year.
/// </summary>
sealed partial class IndiaBillingRules : BillingCountryRules
{
    const int MaxLength = 16;
    static readonly TimeZoneInfo IndiaTime = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    static readonly (long, string)[] Groups = [(10_000_000, "crore"), (100_000, "lakh"), (1_000, "thousand")];

    public override string CountryCode => "IN";
    public override Currency Currency => Currency.Inr;

    public override string FinancialYear(DateTimeOffset issuedAt)
    {
        var local = TimeZoneInfo.ConvertTime(issuedAt, IndiaTime);
        var start = local.Month >= 4 ? local.Year : local.Year - 1;
        return string.Create(CultureInfo.InvariantCulture, $"{start % 100:D2}-{(start + 1) % 100:D2}");
    }

    public override string Format(string prefix, string financialYear, long number)
    {
        var value = string.Create(CultureInfo.InvariantCulture, $"{prefix}/{financialYear}/{number:D5}");
        return value.Length <= MaxLength && Allowed().IsMatch(value)
            ? value
            : throw new InvalidOperationException($"Document number would break the GST format (at most {MaxLength} characters).");
    }

    public override void CheckPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        // Four characters, so PREFIX/25-26/ plus five digits fits the 16 characters up to number 99999.
        if (!Prefix().IsMatch(prefix))
            throw new ArgumentException("Prefix must be 1–4 capital letters or digits.", nameof(prefix));
    }

    public override string AmountInWords(Money amount)
    {
        var (rupees, paise) = Split(amount);
        var words = "Rupees " + Spell(rupees, Groups);
        return (paise == 0 ? words : words + " and " + Spell(paise, Groups) + " paise") + " only";
    }

    [GeneratedRegex("^[A-Z0-9]{1,4}$")] private static partial Regex Prefix();
    [GeneratedRegex("^[A-Za-z0-9/-]+$")] private static partial Regex Allowed();
}

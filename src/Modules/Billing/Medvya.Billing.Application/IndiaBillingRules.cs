using System.Globalization;
using System.Text.RegularExpressions;
using Medvya.Billing.Domain;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Billing.Application;

/// <summary>
/// India (GST): rupees and paise; one series per prefix per financial year (1 April–31 March, India time), printed
/// PREFIX/25-26/00042. CGST Rules 46(b), 50 and 53 ask for a consecutive serial number of at most 16 characters
/// (letters, digits, "/" and "-"), unique for the financial year
/// (https://taxinformation.cbic.gov.in/content/html/tax_repository/gst/rules/cgst_rules/active/chapter6/rule46_v1.00.html).
/// The serial is padded to five digits and grows past them while the whole number fits 16 characters.
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
        // Three characters, so PREFIX/25-26/ leaves six digits: every series can issue 999,999 numbers a financial year.
        if (!Prefix().IsMatch(prefix))
            throw new ArgumentException("Prefix must be 1–3 capital letters or digits.", nameof(prefix));
    }

    public override long MaxNumber(string prefix, string financialYear)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(financialYear);
        long max = 1;
        for (var digits = MaxLength - prefix.Length - financialYear.Length - 2; digits > 0; digits--) max *= 10; // two "/"
        return max - 1;
    }

    /// <summary>
    /// The payable total to the nearest rupee: 50 paise or more rounds up, less rounds down (₹1,234.50 → +0.50, ₹1,234.49 → −0.49). Lines and tax stay
    /// exact; the e-invoice RndOffAmt carries this amount (MDV-P4-004).
    /// </summary>
    public override long RoundOff(long exactMinor)
    {
        var paise = exactMinor % Currency.MinorPerMajor;
        return paise >= 50 ? Currency.MinorPerMajor - paise : -paise;
    }

    public override string AmountInWords(Money amount)
    {
        var (rupees, paise) = Split(amount);
        var words = "Rupees " + Spell(rupees, Groups);
        return (paise == 0 ? words : words + " and " + Spell(paise, Groups) + (paise == 1 ? " paisa" : " paise")) + " only";
    }

    /// <summary>
    /// GST: exempt care only is a bill of supply, taxed services only a tax invoice, both together an invoice-cum-bill of supply (CGST Rules 46 and 49).
    /// Clinic care is exempt by default, so most bills are bills of supply.
    /// </summary>
    public override string BillTitle(IReadOnlyCollection<TaxTreatment> lines) =>
        !lines.Contains(TaxTreatment.Taxable) ? "billing.print.billOfSupply"
        : lines.Contains(TaxTreatment.Exempt) ? "billing.print.invoiceCumBillOfSupply" : "billing.print.taxInvoice";

    /// <summary>
    /// The state of the clinic's GSTIN (its first two digits), e.g. "Tamil Nadu (33)": care is supplied at the clinic.
    /// shortcut: always the clinic's state; whether a teleconsult takes the patient's state is the CA review's call (MDV-P4-031).
    /// </summary>
    public override string? PlaceOfSupply(string? taxNumber) =>
        taxNumber is { Length: >= 2 } && States.TryGetValue(taxNumber[..2], out var state) ? $"{state} ({taxNumber[..2]})" : null;

    /// <summary>GST state codes, the first two digits of a GSTIN.</summary>
    static readonly Dictionary<string, string> States = new()
    {
        ["01"] = "Jammu and Kashmir", ["02"] = "Himachal Pradesh", ["03"] = "Punjab", ["04"] = "Chandigarh", ["05"] = "Uttarakhand", ["06"] = "Haryana",
        ["07"] = "Delhi", ["08"] = "Rajasthan", ["09"] = "Uttar Pradesh", ["10"] = "Bihar", ["11"] = "Sikkim", ["12"] = "Arunachal Pradesh", ["13"] = "Nagaland",
        ["14"] = "Manipur", ["15"] = "Mizoram", ["16"] = "Tripura", ["17"] = "Meghalaya", ["18"] = "Assam", ["19"] = "West Bengal", ["20"] = "Jharkhand",
        ["21"] = "Odisha", ["22"] = "Chhattisgarh", ["23"] = "Madhya Pradesh", ["24"] = "Gujarat", ["26"] = "Dadra and Nagar Haveli and Daman and Diu",
        ["27"] = "Maharashtra", ["29"] = "Karnataka", ["30"] = "Goa", ["31"] = "Lakshadweep", ["32"] = "Kerala", ["33"] = "Tamil Nadu", ["34"] = "Puducherry",
        ["35"] = "Andaman and Nicobar Islands", ["36"] = "Telangana", ["37"] = "Andhra Pradesh", ["38"] = "Ladakh",
    };

    [GeneratedRegex("^[A-Z0-9]{1,3}$")] private static partial Regex Prefix();
    [GeneratedRegex("^[A-Za-z0-9/-]+$")] private static partial Regex Allowed();
}

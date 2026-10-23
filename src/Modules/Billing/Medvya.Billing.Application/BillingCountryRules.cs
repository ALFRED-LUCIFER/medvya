using Medvya.BuildingBlocks.Domain;

namespace Medvya.Billing.Application;

/// <summary>
/// Money and legal numbering rules per country (AGENTS.md 3.3): one subclass per country, no country checks elsewhere.
/// Formats follow the receipts design (P-15); the printed documents' sign-off is MDV-P4-031.
/// </summary>
public abstract class BillingCountryRules
{
    public static readonly BillingCountryRules India = new IndiaBillingRules();
    public static readonly BillingCountryRules Uae = new UaeBillingRules();

    public static BillingCountryRules For(string countryCode) => countryCode switch
    {
        "IN" => India,
        "AE" => Uae,
        _ => throw new ArgumentOutOfRangeException(nameof(countryCode), countryCode, "Unsupported country."),
    };

    public abstract string CountryCode { get; }
    public abstract Currency Currency { get; }

    /// <summary>The series period a document issued at <paramref name="issuedAt"/> belongs to; a new period starts a new series.</summary>
    public abstract string FinancialYear(DateTimeOffset issuedAt);

    /// <summary>The printed legal number, e.g. INV/25-26/00042 (India) or TI-AD-00042 (UAE).</summary>
    public abstract string Format(string prefix, string financialYear, long number);

    /// <summary>Rejects a prefix whose numbers would break the country's format rules.</summary>
    public abstract void CheckPrefix(string prefix);

    /// <summary>The highest number the series can print; issuing past it is refused ("series_exhausted"). Default: no format limit, only the bigint column.</summary>
    public virtual long MaxNumber(string prefix, string financialYear) => long.MaxValue - 1;

    /// <summary>
    /// Amount in words for printed documents, e.g. "Rupees two thousand only": "and" joins only the paise or fils, "only" ends
    /// the phrase, zero paise are left out. The sign is not spelled (credit notes say "credit").
    /// </summary>
    public abstract string AmountInWords(Money amount);

    protected (long Major, long Minor) Split(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        if (amount.Currency != Currency) throw new InvalidOperationException($"{CountryCode} documents are in {Currency.Code}, not {amount.Currency.Code}.");
        var abs = Math.Abs(amount.Minor);
        return (abs / Currency.MinorPerMajor, abs % Currency.MinorPerMajor);
    }

    static readonly string[] Ones =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen",
        "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    ];
    static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    /// <summary>1–999 as in the design: "two hundred and fifty", "twenty-one".</summary>
    protected static string BelowThousand(long n)
    {
        var hundreds = n / 100;
        var rest = n % 100;
        var restWords = rest == 0 ? "" : rest < 20 ? Ones[rest] : Tens[rest / 10] + (rest % 10 == 0 ? "" : "-" + Ones[rest % 10]);
        if (hundreds == 0) return restWords;
        return Ones[hundreds] + " hundred" + (rest == 0 ? "" : " and " + restWords);
    }

    /// <summary>Spells n by groups, largest first (each group's count spelled recursively); groups are joined with spaces, as in the design.</summary>
    protected static string Spell(long n, IReadOnlyList<(long Size, string Name)> groups)
    {
        if (n == 0) return Ones[0];
        var parts = new List<string>();
        foreach (var (size, name) in groups)
        {
            if (n < size) continue;
            parts.Add(Spell(n / size, groups) + " " + name);
            n %= size;
        }
        if (n > 0) parts.Add(BelowThousand(n));
        return string.Join(' ', parts);
    }
}

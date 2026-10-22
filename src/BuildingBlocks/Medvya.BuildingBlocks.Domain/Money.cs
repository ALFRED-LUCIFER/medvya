namespace Medvya.BuildingBlocks.Domain;

/// <summary>An ISO 4217 currency Medvya bills in, with its minor-unit exponent (INR paise, AED fils: 2 digits).</summary>
public sealed record Currency
{
    public static readonly Currency Inr = new("INR", 2);
    public static readonly Currency Aed = new("AED", 2);

    Currency(string code, int minorDigits) => (Code, MinorDigits) = (code, minorDigits);

    public string Code { get; }
    public int MinorDigits { get; }
    /// <summary>Minor units in one major unit (100 for INR and AED).</summary>
    public long MinorPerMajor
    {
        get
        {
            long factor = 1;
            for (var i = 0; i < MinorDigits; i++) factor *= 10;
            return factor;
        }
    }

    public static Currency From(string code) => code switch
    {
        "INR" => Inr,
        "AED" => Aed,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unsupported currency."),
    };

    public override string ToString() => Code;
}

/// <summary>
/// An amount in integer minor units of one currency (AGENTS.md 3.4); stored as bigint plus the ISO code, never decimal or float.
/// All arithmetic is checked (overflow throws) and refuses to mix currencies. Fractions of a minor unit only arise from a rate
/// (tax, percentage discount) and are rounded half away from zero to the currency's minor unit, the commercial rule used for
/// GST (paise) and UAE VAT (fils).
/// shortcut: no cash rounding (India rupee round-off, UAE 25 fils) yet; add it with cash payments and day close (MDV-P4-009, MDV-P4-012)
/// once the advisers confirm it (MDV-P4-031).
/// </summary>
public sealed record Money
{
    public Money(long minor, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        (Minor, Currency) = (minor, currency);
    }

    public long Minor { get; }
    public Currency Currency { get; }

    public static Money Zero(Currency currency) => new(0, currency);

    public static Money operator +(Money a, Money b)
    {
        var other = Same(a, b); // before a.Minor, so a null operand throws ArgumentNullException, not NullReferenceException
        return new(checked(a.Minor + other.Minor), a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        var other = Same(a, b);
        return new(checked(a.Minor - other.Minor), a.Currency);
    }

    public static Money operator -(Money a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new(checked(-a.Minor), a.Currency);
    }

    /// <summary>Line amount: unit price times quantity.</summary>
    public Money Times(long quantity) => new(checked(Minor * quantity), Currency);

    /// <summary>The share at a rate in basis points (1800 = 18 %, 500 = 5 %), rounded half away from zero to the minor unit.</summary>
    public Money ApplyRate(int basisPoints) => new(RoundHalfAwayFromZero((Int128)Minor * basisPoints, 10_000), Currency);

    static long RoundHalfAwayFromZero(Int128 numerator, long denominator)
    {
        var quotient = Int128.DivRem(numerator, denominator);
        var result = quotient.Quotient;
        if (Int128.Abs(quotient.Remainder) * 2 >= denominator) result += Int128.Sign(numerator);
        return checked((long)result);
    }

    static Money Same(Money a, Money b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Currency == b.Currency ? b : throw new InvalidOperationException($"Currency mismatch: {a.Currency.Code} and {b.Currency.Code}.");
    }

    public override string ToString() => $"{Minor} {Currency.Code} minor units";
}

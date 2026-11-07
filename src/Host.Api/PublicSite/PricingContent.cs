using System.Globalization;

namespace Host.Api.PublicSite;

/// <summary>
/// Approved plans and prices on the public site, from PublicSite/pricing.json (reloads on change).
/// shortcut: plans read from pricing.json, move to console plans when MDV-P5-042 lands.
/// </summary>
public sealed class PricingContent
{
    public string Version { get; set; } = "";
    public DateOnly? Approved { get; set; }
    public IList<PricingPlan> Plans { get; } = [];
    /// <summary>Medvya Assist add-on per doctor, by country deployment (IN, AE).</summary>
    public IDictionary<string, PlanPrice> Assist { get; } = new Dictionary<string, PlanPrice>();

    /// <summary>The add-on's monthly price in this region, or null when none is approved.</summary>
    public string? AssistMonthly(string region) =>
        Assist.TryGetValue(region, out var p) && p.Monthly is { } m ? Money(m, p.Currency) : null;

    /// <summary>Minor units (paise, fils) as shown to a buyer: ₹2,50,000 (Indian grouping), AED 349, AED 999.50.</summary>
    // shortcut: assumes a currency with 2 minor digits (INR, AED, the only two markets); use ISO 4217 exponents for a third.
    public static string Money(long minor, string currency)
    {
        var number = (minor / 100m).ToString(minor % 100 == 0 ? "N0" : "N2", CultureInfo.GetCultureInfo(currency == "INR" ? "en-IN" : "en-AE"));
        return currency == "INR" ? $"₹{number}" : $"{currency} {number}";
    }
}

public sealed class PricingPlan
{
    /// <summary>Plan code: the copy key (site.pricing.plan.&lt;Code&gt;.*) and the value passed to sign-up.</summary>
    public string Code { get; set; } = "";
    public bool PerDoctor { get; set; }
    public bool Popular { get; set; }
    /// <summary>Priced by contract: "Custom", a starting yearly price and "Talk to us" instead of a trial.</summary>
    public bool Custom { get; set; }
    /// <summary>By country deployment (IN, AE); a missing region or amount shows no price.</summary>
    public IDictionary<string, PlanPrice> Prices { get; } = new Dictionary<string, PlanPrice>();
}

public sealed class PlanPrice
{
    public string Currency { get; set; } = "";
    public long? Monthly { get; set; }
    public long? Yearly { get; set; }
}

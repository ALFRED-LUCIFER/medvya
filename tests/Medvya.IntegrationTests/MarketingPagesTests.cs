using System.Net;
using System.Text.RegularExpressions;
using Host.Api.PublicSite;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Medvya.IntegrationTests;

/// <summary>Pricing (MDV-P5-050), Medvya Assist (MDV-P1-055) and For hospitals (MDV-P1-056) on the public site.</summary>
public sealed partial class MarketingPagesTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    async Task<string> Get(string path, string region = "IN", params (string Key, string Value)[] settings)
    {
        var client = factory.WithWebHostBuilder(b => { foreach (var (k, v) in settings) b.UseSetting(k, v); })
            .CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative)) { Headers = { { "Cookie", $"medvya-region={region}" } } };
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(199900L, "INR", "₹1,999")]
    [InlineData(25000000L, "INR", "₹2,50,000")] // Indian digit grouping
    [InlineData(5L, "INR", "₹0.05")]
    [InlineData(34900L, "AED", "AED 349")]
    [InlineData(4000000L, "AED", "AED 40,000")]
    [InlineData(99950L, "AED", "AED 999.50")]
    public void MinorUnitsAreFormattedForTheBuyer(long minor, string currency, string shown) =>
        Assert.Equal(shown, PricingContent.Money(minor, currency));

    // MDV-AUDIT-1112: features the product does not have yet are marked planned on the pages that mention them, in every language.
    [Theory]
    [InlineData("/", "Planned, not available yet: AI-drafted notes")]
    [InlineData("/assist", "Planned, not available yet: AI-drafted notes")]
    [InlineData("/pricing", "Planned, not available yet: AI-drafted notes")]
    [InlineData("/ar/pricing", "مخطط له وغير متاح بعد")]
    public async Task PagesThatMentionFeaturesNotBuiltYetSayTheyArePlanned(string path, string expected)
    {
        Assert.Contains(expected, await Get(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndiaSeesRupeesAndIndianPaymentMethods()
    {
        var html = await Get("/pricing", "IN");
        foreach (var shown in new[] { "₹1,999", "₹3,499", "₹2,50,000", "UPI AutoPay", "18% GST", "₹999 per doctor", "Price list 2026.11 · approved 6 Nov 2026" })
            Assert.Contains(shown, html, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"AED \d", html); // only the region switch names the other currency
    }

    [Fact]
    public async Task UaeSeesDirhamsWithApplePayAndCards()
    {
        var html = await Get("/pricing", "AE");
        foreach (var shown in new[] { "AED 349", "AED 599", "AED 40,000", "card, Apple Pay", "5% VAT", "AED 149 per doctor" })
            Assert.Contains(shown, html, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"₹\d", html);
    }

    [Fact]
    public async Task TheYearlySwitchIsAPlainLinkThatChangesThePrices()
    {
        var monthly = await Get("/pricing");
        Assert.Contains("<a href=\"?cycle=yearly\">Yearly</a>", monthly, StringComparison.Ordinal);
        Assert.Contains("<a href=\"?cycle=monthly\" aria-current=\"true\">Monthly</a>", monthly, StringComparison.Ordinal);
        Assert.DoesNotContain("₹19,990", monthly, StringComparison.Ordinal);

        var yearly = await Get("/pricing?cycle=yearly");
        Assert.Contains("<a href=\"?cycle=yearly\" aria-current=\"true\">Yearly</a>", yearly, StringComparison.Ordinal);
        foreach (var shown in new[] { "₹19,990", "₹34,990", "per doctor a year", "Save ₹3,998 a year", "Save ₹6,998 a year" })
            Assert.Contains(shown, yearly, StringComparison.Ordinal);
        Assert.DoesNotContain("₹1,999<", yearly, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartFreeTrialCarriesOnlyTheCountryAndPlanCode()
    {
        var html = await Get("/pricing", "AE");
        Assert.Contains("href=\"/region?go=AE&plan=solo\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/region?go=AE&plan=clinic\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("plan=hospital", html, StringComparison.Ordinal); // contract plan: "Talk to us" instead
        Assert.Contains("href=\"/ar/region?go=IN&plan=clinic\"", await Get("/ar/pricing"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("clinic", "https://in.example/signup?country=IN&plan=clinic")]
    [InlineData("hospital", "https://in.example/signup?country=IN")] // not a trial plan
    [InlineData("<script>", "https://in.example/signup?country=IN")] // visitor text never reaches sign-up
    [InlineData(null, "https://in.example/signup?country=IN")]
    public async Task SignUpOpensWithTheChosenTrialPlan(string? plan, string location)
    {
        var client = factory.WithWebHostBuilder(b => b.UseSetting("PublicSite:Deployments:SignUpUrls:IN", "https://in.example/signup"))
            .CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
        var res = await client.GetAsync(new Uri($"/region?go=IN{(plan is null ? "" : $"&plan={Uri.EscapeDataString(plan)}")}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(location, res.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task APriceChangedInTheListShowsOnTheNextLoad()
    {
        var html = await Get("/pricing", settings: ("Pricing:Plans:1:Prices:IN:Monthly", "459900"));
        Assert.Contains("₹4,599", html, StringComparison.Ordinal);
        Assert.DoesNotContain("₹3,499", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAnApprovedAmountNoPriceIsShown()
    {
        (string, string)[] none = [("Pricing:Plans:0:Prices:IN:Monthly", ""), ("Pricing:Assist:IN:Monthly", "")];
        var pricing = await Get("/pricing", settings: none);
        Assert.Contains("Price to be announced", pricing, StringComparison.Ordinal);
        Assert.DoesNotContain("₹1,999", pricing, StringComparison.Ordinal);
        Assert.Contains("AI notes and prescriptions, priced per doctor.", pricing, StringComparison.Ordinal);
        Assert.DoesNotContain("₹999", pricing, StringComparison.Ordinal);

        var assist = await Get("/assist", settings: none);
        Assert.Contains("Medvya Assist is an add-on, priced per doctor.", assist, StringComparison.Ordinal);
        Assert.DoesNotContain("₹", assist, StringComparison.Ordinal);
        Assert.Contains("Medvya Assist is an add-on: ₹999 per doctor.", await Get("/assist"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PricingHasTheCompareTableAndTheFaqWithoutScript()
    {
        var html = await Get("/pricing");
        Assert.Contains("<th scope=\"col\">Clinic</th>", html, StringComparison.Ordinal);
        Assert.Contains("<th scope=\"row\">UPI QR, links and AutoPay (planned)</th>", html, StringComparison.Ordinal); // not built yet, so it says so (#1112)
        Assert.DoesNotContain("Online booking and WhatsApp reminders", html, StringComparison.Ordinal); // only SMS and email reminders exist today
        Assert.Contains("If a payment fails", html, StringComparison.Ordinal);
        Assert.Contains("Free migration", html, StringComparison.Ordinal);
        Assert.Equal(4, Summary().Count(html));
        Assert.Contains("<summary>Is there a free trial?</summary>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/assist"), InlineData("/hi/assist"), InlineData("/ar/assist")]
    public async Task AssistSaysNothingIsSavedUntilTheDoctorSignsAndMakesNoAccuracyClaim(string path)
    {
        var html = await Get(path);
        Assert.DoesNotMatch(AccuracyClaim(), html);
        // In Arabic the price is isolated so it stays "₹999", not "999₹".
        Assert.Equal(path == "/ar/assist", html.Contains("\u2068₹999\u2069", StringComparison.Ordinal));
        if (path == "/assist")
        {
            Assert.Contains("Nothing is saved until you sign.", html, StringComparison.Ordinal);
            Assert.Contains("It does not diagnose, prescribe or sign.", html, StringComparison.Ordinal);
            Assert.Contains("Visit note · Meera Iyer, 54", html, StringComparison.Ordinal); // synthetic cast
        }
    }

    [Fact]
    public async Task HospitalsPresentsOnlyBuiltFeaturesAsAvailable()
    {
        var html = await Get("/hospitals");
        var blocks = Module().Matches(html).ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value);
        Assert.Equal(HospitalsModel.Modules.Length, blocks.Count);
        foreach (var notBuilt in new[] { "Single sign-on", "Central reporting", "Insurance e-claims", "Patient app under your brand" })
            Assert.Equal("Planned", blocks[notBuilt]);
        Assert.Equal(["Outpatient (OPD)", "Access log"], blocks.Where(b => b.Value == "Available").Select(b => b.Key));
        Assert.Contains("href=\"/demo\">Talk to our hospital team", html, StringComparison.Ordinal);
        Assert.Contains("Records stay in your country.", html, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"accura|\d+\s*%|percent|सटीक|دقة|دقيق", RegexOptions.IgnoreCase)]
    private static partial Regex AccuracyClaim();
    [GeneratedRegex("<details><summary>")] // FAQ items; the header menus are details with attributes
    private static partial Regex Summary();
    [GeneratedRegex("""<p class="chip[^"]*">([^<]+)</p>\s*<h3>([^<]+)</h3>""")]
    private static partial Regex Module();
}

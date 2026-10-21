using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Medvya.IntegrationTests;

/// <summary>Public site (MDV-P1-001): server-rendered, no JavaScript, region-aware copy, three languages, never touches the session.</summary>
public sealed partial class PublicSiteTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly WebApplicationFactory<Program> factory;
    public PublicSiteTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });

    static async Task<string> Html(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync());
    }

    async Task<string> Get(string path, string? region = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (region is not null) req.Headers.Add("Cookie", $"medvya-region={region}");
        return await Html(await Client().SendAsync(req));
    }

    public static TheoryData<string> Pages => new() { "/", "/pricing", "/hi", "/hi/pricing", "/ar", "/ar/pricing" };

    [Theory, MemberData(nameof(Pages))]
    public async Task PagesRenderWithoutScriptsOrThirdPartyResources(string path)
    {
        var html = await Get(path);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" src=", html, StringComparison.OrdinalIgnoreCase); // no images, pixels or embeds from anywhere
        // Every link and stylesheet stays on this site (absolute URLs are only canonical/alternate links to itself).
        foreach (Match m in Href().Matches(html))
            Assert.True(m.Groups[1].Value.StartsWith('/') || m.Groups[1].Value.StartsWith('?') || m.Groups[1].Value.StartsWith('#')
                || m.Groups[1].Value.StartsWith("http://localhost", StringComparison.Ordinal) || m.Groups[1].Value == "data:,", m.Groups[1].Value);
        Assert.Contains("<meta name=\"viewport\"", html, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"canonical\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LandingHasTheDesignedCopyAndOnlyTheTwoCtas()
    {
        var html = await Get("/");
        Assert.Contains("<h1 id=\"hero-title\">Talk to your patient. Medvya writes the note. You approve it.</h1>", html, StringComparison.Ordinal);
        var ctas = Assert.Single(Ctas().Matches(html)).Groups[1].Value;
        Assert.Equal(["I'm a doctor · start free trial", "For hospitals · book a demo"], LinkText().Matches(ctas).Select(m => m.Groups[1].Value));
        foreach (var pillar in new[] { "Know the patient in 60 seconds", "Consent before every recording", "Nothing falls through" })
            Assert.Contains($"<h2>{pillar}</h2>", html, StringComparison.Ordinal);
        foreach (var nav in new[] { "For doctors", "For hospitals", "Trust centre", "Pricing", "Sign in" })
            Assert.Contains($">{nav}</a>", html, StringComparison.Ordinal);
    }

    // Exact strings from docs/02-design/screen-specs.md F-01.
    [Theory]
    [InlineData(null, "Built for Indian OPD · ABDM-ready", "English, Hindi, Tamil and more", "Data stays in India · Azure Central India")]
    [InlineData("IN", "Built for Indian OPD · ABDM-ready", "English, Hindi, Tamil and more", "Data stays in India · Azure Central India")]
    [InlineData("AE", "Built for UAE clinics · DoH / DHA aware", "Arabic and English", "Data stays in the UAE · Azure UAE North")]
    [InlineData("XX", "Built for Indian OPD · ABDM-ready", "English, Hindi, Tamil and more", "Data stays in India · Azure Central India")]
    public async Task RegionCookieChoosesTheRegionCopy(string? region, string tag, string languages, string residency)
    {
        var html = await Get("/", region);
        Assert.Contains($"<p class=\"tag\">{tag}</p>", html, StringComparison.Ordinal);
        Assert.Contains($"ambient notes in {languages}, and", html, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"residency\">{residency}</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegionSwitchStoresOnlyTheRegionInANonTrackingCookie()
    {
        var res = await Client().GetAsync(new Uri("/ar/pricing?region=AE", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/ar/pricing", res.Headers.Location?.OriginalString);
        var cookie = Assert.Single(res.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("medvya-region=AE;", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        var ignored = await Client().GetAsync(new Uri("/?region=https://evil.example", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, ignored.StatusCode);
        Assert.False(ignored.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("/", "en", "ltr")]
    [InlineData("/hi", "hi", "ltr")]
    [InlineData("/ar", "ar", "rtl")]
    public async Task EachLanguageHasItsOwnAddressAndDirection(string path, string lang, string dir)
    {
        var html = await Get(path);
        Assert.Contains($"<html lang=\"{lang}\" dir=\"{dir}\">", html, StringComparison.Ordinal);
        foreach (var (l, href) in new[] { ("en", "/"), ("hi", "/hi"), ("ar", "/ar") })
            Assert.Contains($"hreflang=\"{l}\" href=\"http://localhost{href}\"", html, StringComparison.Ordinal);
        Assert.Contains($"<h1 id=\"hero-title\">{Catalog(lang)["site.hero.title"]}</h1>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownLanguagePrefixIsNotFound() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync(new Uri("/fr", UriKind.Relative))).StatusCode);

    [Fact]
    public void EveryPublicSiteStringIsTranslatedIntoHindiAndArabic()
    {
        var keys = Catalog("en").Keys.Where(k => k.StartsWith("site.", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(keys);
        foreach (var lang in new[] { "hi", "ar" })
            Assert.Empty(keys.Except(Catalog(lang).Keys));
    }

    [Theory]
    [InlineData("/pricing"), InlineData("/hi/pricing"), InlineData("/ar/pricing")]
    public async Task PricingIsAPlaceholderWithoutPrices(string path) =>
        Assert.DoesNotMatch(Price(), await Get(path));

    [Fact]
    public async Task StylesheetCarriesTheSharedDesignTokens()
    {
        var res = await Client().GetAsync(new Uri("/site.css", UriKind.Relative));
        Assert.Equal("text/css", res.Content.Headers.ContentType?.MediaType);
        var css = await res.Content.ReadAsStringAsync();
        Assert.StartsWith(":root {", css, StringComparison.Ordinal);
        Assert.Contains("--color-brand-700: #0f766e;", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublicPagesNeverAuthenticateTheRequest()
    {
        var probe = new AuthenticationProbe();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton(probe);
            s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ProbeHandler>("probe", _ => { });
            s.PostConfigure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = "probe");
        }));
        var client = app.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
        foreach (var path in new[] { "/", "/ar/pricing", "/site.css", "/?region=AE" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
            req.Headers.Add("Cookie", "__Host-medvya=session-cookie-value");
            Assert.True((int)(await client.SendAsync(req)).StatusCode < 400, path);
        }
        Assert.Equal(0, probe.Calls);

        await client.GetAsync(new Uri("/health", UriKind.Relative));
        Assert.True(probe.Calls > 0); // the probe works: API endpoints still authenticate
    }

    sealed class AuthenticationProbe { public int Calls; }

    sealed class ProbeHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, AuthenticationProbe probe)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            Interlocked.Increment(ref probe.Calls);
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }

    static Dictionary<string, string> Catalog(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(RepoRoot(), "packages", "i18n", "messages", $"{lang}.json")))!;

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Medvya.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex("href=\"([^\"]*)\"")]
    private static partial Regex Href();
    [GeneratedRegex("""<div class="ctas">(.*?)</div>""", RegexOptions.Singleline)]
    private static partial Regex Ctas();
    [GeneratedRegex("""<a [^>]*>([^<]*)</a>""")]
    private static partial Regex LinkText();
    [GeneratedRegex(@"[₹$€]|\bAED\b|\bINR\b|\d+\s*(/|per)\s*(month|mo|year)", RegexOptions.IgnoreCase)]
    private static partial Regex Price();
}

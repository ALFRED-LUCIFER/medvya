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

    internal static readonly string[] PagePaths =
        ["/", "/pricing", "/trust", "/demo", "/region", "/assist", "/hospitals", "/hi", "/hi/pricing", "/hi/trust", "/hi/demo", "/hi/region", "/hi/assist", "/hi/hospitals",
         "/ar", "/ar/pricing", "/ar/trust", "/ar/demo", "/ar/region", "/ar/assist", "/ar/hospitals"];
    public static TheoryData<string> Pages => new(PagePaths);

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
                || m.Groups[1].Value.StartsWith("http://localhost", StringComparison.Ordinal) || m.Groups[1].Value == "data:,"
                || m.Groups[1].Value == "mailto:privacy@medvya.example", m.Groups[1].Value); // the notice's withdrawal address: opens the visitor's mail app, loads nothing
        Assert.Contains("<meta name=\"viewport\"", html, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"canonical\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HomeHasEveryDesignedSectionAndTheTwoHeroCtas()
    {
        var html = await Get("/");
        Assert.Contains("<h1 id=\"hero-title\">Run your whole clinic from <span class=\"grad\">one calm screen.</span></h1>", html, StringComparison.Ordinal);
        var ctas = Ctas().Match(html).Groups[1].Value;
        Assert.Equal(["/region", "/demo"], Href().Matches(ctas).Select(m => m.Groups[1].Value));
        Assert.Equal(["Start your free trial", "Book a demo"], LinkText().Matches(ctas).Select(m => m.Groups[1].Value));
        foreach (var heading in new[]
        {
            "From the first token to the last receipt.", "Replace five tools with one that your whole team likes.", "From a one-doctor clinic to a hospital group.",
            "Your patients get an app too. Free for them, branded for you.", "Live in a day. We move your data for you.",
            "Built for patient records from day one", "Simple plans with a free trial.", "Questions doctors ask us",
        })
            Assert.Contains($">{heading}</h2>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/pricing\">Compare plans</a>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/trust\">Visit the trust centre</a>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/pricing\">See plans</a>", html, StringComparison.Ordinal);
        // The FAQ opens without script: seven <details>, the first open.
        Assert.Equal(7, FaqItem().Count(html));
        Assert.Contains("<details open=\"open\"><summary>Is there a free trial?</summary>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/"), InlineData("/hi"), InlineData("/ar")]
    public async Task HomeShowsNoPricesQuotesOrNamesOutsideTheSyntheticCast(string path)
    {
        var html = await Get(path);
        Assert.DoesNotMatch(Price(), html);
        Assert.DoesNotContain("<blockquote", html, StringComparison.OrdinalIgnoreCase); // no customer stories until real, permitted quotes exist
        // Product pictures name only people from docs/00-overview/personas.md.
        var names = PictureName().Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.Contains(n, SyntheticCast));
    }

    static readonly string[] SyntheticCast = ["Deepak Nair", "Rahul Menon", "Meera Iyer", "Fatima Al Mansoori", "Kavya Iyer"];

    [Fact]
    public async Task HeaderMenusAndPhoneMenuWorkWithoutScript()
    {
        var html = await Get("/");
        foreach (var menu in new[] { "Product", "Solutions" })
            Assert.Matches($"<details class=\"menu\" name=\"site-menu\">\\s*<summary>{menu}</summary>", html);
        Assert.Contains("<details class=\"menu phone-menu\" name=\"site-menu\">", html, StringComparison.Ordinal);
        foreach (var (href, text) in new[]
        {
            ("/assist", "Medvya Assist"), ("/pricing", "Pricing"), ("/trust", "Security"), ("/hospitals", "Hospitals"), ("/demo", "Book a demo"),
            ("/region", "Start free"), ("/demo", "Contact sales"), ("/trust", "Trust centre"),
        })
            Assert.Contains($"href=\"{href}\">{text}</a>", html, StringComparison.Ordinal);
        Assert.Contains(">Sign in</a>", html, StringComparison.Ordinal);
        Assert.Contains("<aside class=\"announce\" aria-label=\"What is new\">", html, StringComparison.Ordinal);
        // Hindi and Arabic links keep their language prefix.
        Assert.Contains("href=\"/ar/assist\">", await Get("/ar"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreLinksAppearOnlyWhenTheirAddressesAreConfigured()
    {
        Assert.DoesNotContain("class=\"app-links\"", await Get("/"), StringComparison.Ordinal);

        using var configured = factory.WithWebHostBuilder(b => b
            .UseSetting("PublicSite:AppLinks:AppStore", "https://apps.apple.com/app/id0000000000")
            .UseSetting("PublicSite:AppLinks:GooglePlay", "https://play.google.com/store/apps/details?id=example.medvya"));
        var html = await Html(await configured.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false }).GetAsync(new Uri("/trust", UriKind.Relative)));
        Assert.Contains("<a class=\"store\" href=\"https://apps.apple.com/app/id0000000000\" rel=\"noopener\">Download on the App Store</a>", html, StringComparison.Ordinal);
        Assert.Contains(">Get it on Google Play</a>", html, StringComparison.Ordinal);
    }

    // Region-dependent copy (MDV-P1-054 home).
    [Theory]
    [InlineData(null, "UPI payments", "Data stays in India")]
    [InlineData("IN", "UPI payments", "Data stays in India")]
    [InlineData("AE", "card payments", "Data stays in the UAE")]
    [InlineData("XX", "UPI payments", "Data stays in India")]
    public async Task RegionCookieChoosesTheRegionCopy(string? region, string payments, string residency)
    {
        var html = await Get("/", region);
        Assert.Contains(payments == "UPI payments" ? "UPI and billing" : "Payments and billing", html, StringComparison.Ordinal); // the region card, marked planned (MDV-AUDIT-1112)
        Assert.Contains($"<li>{residency}</li>", html, StringComparison.Ordinal);
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

    [Fact]
    public async Task OverHttpsEveryCookieIsSecureAndNoSharedCacheKeepsAPage()
    {
        var https = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri("https://localhost") });
        foreach (var path in new[] { "/?region=AE", "/demo", "/region?go=AE" })
        {
            var res = await https.GetAsync(new Uri(path, UriKind.Relative));
            var cookie = Assert.Single(res.Headers.GetValues("Set-Cookie")); // the region preference, or the form's anti-forgery token
            Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Equal((true, true), (res.Headers.CacheControl?.NoCache, res.Headers.CacheControl?.NoStore));
        }
        // The landing's copy follows the region cookie and its links the Host header, so no cache may keep it.
        var landing = await https.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Equal((true, true), (landing.Headers.CacheControl?.NoCache, landing.Headers.CacheControl?.NoStore));
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
        Assert.Contains($"<h1 id=\"hero-title\">{Catalog(lang)["site.home.hero.titleStart"]} ", html, StringComparison.Ordinal);
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
        foreach (var path in new[] { "/", "/ar/pricing", "/trust", "/hi/region?go=AE", "/site.css", "/?region=AE" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
            req.Headers.Add("Cookie", "__Host-medvya=session-cookie-value");
            Assert.True((int)(await client.SendAsync(req)).StatusCode < 400, path);
        }
        // A form post too (refused for its missing token, but never by reading the session).
        using (var post = new HttpRequestMessage(HttpMethod.Post, new Uri("/demo", UriKind.Relative)) { Content = new FormUrlEncodedContent([]) })
        {
            post.Headers.Add("Cookie", "__Host-medvya=session-cookie-value");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(post)).StatusCode);
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
    [GeneratedRegex("""<details( open="open")?><summary>""")]
    private static partial Regex FaqItem();
    [GeneratedRegex("""<span class="token">[^<]*</span><span>([^<]+)</span>""")]
    private static partial Regex PictureName();
    [GeneratedRegex(@"[₹$€]|\bAED\b|\bINR\b|\d+\s*(/|per)\s*(month|mo|year)", RegexOptions.IgnoreCase)]
    private static partial Regex Price();
}

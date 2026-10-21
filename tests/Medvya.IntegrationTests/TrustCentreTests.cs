using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Medvya.Leads.Application;
using Medvya.Leads.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>Trust centre and demo request (MDV-P1-003): designed copy, dated lists from content, and a guarded form with no patient fields.</summary>
public sealed partial class TrustCentreTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    sealed class RecordingDemoRequests : IDemoRequests
    {
        public List<DemoRequest> Received { get; } = [];
        public Task SubmitAsync(DemoRequest request, CancellationToken cancellationToken = default) { Received.Add(request); return Task.CompletedTask; }
    }

    (HttpClient Client, RecordingDemoRequests Store) Site(int perHour = 5, int minimumSeconds = 0, params (string Key, string Value)[] settings)
    {
        var store = new RecordingDemoRequests();
        var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("PublicSite:SubmissionsPerHour", perHour.ToString(System.Globalization.CultureInfo.InvariantCulture));
            b.UseSetting("PublicSite:MinimumFillSeconds", minimumSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var (k, v) in settings) b.UseSetting(k, v);
            b.ConfigureTestServices(s => s.AddSingleton<IDemoRequests>(store));
        });
        return (app.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false }), store);
    }

    internal static async Task<string> Html(HttpResponseMessage res) => WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync());

    static readonly Dictionary<string, string> Valid = new()
    {
        ["Form.Name"] = "  Kavya Menon  ", ["Form.Organisation"] = "Synthetic General Hospital", ["Form.Role"] = "Chief medical officer",
        ["Form.Country"] = "IN", ["Form.Email"] = "kavya.menon@example.com", ["Form.Phone"] = "", ["Form.Message"] = "Twenty OPD rooms.",
    };

    // GET the form, then POST it with its anti-forgery cookie and token (as a browser would), optionally overriding fields.
    internal static async Task<HttpResponseMessage> Submit(HttpClient client, Dictionary<string, string> fields, bool withToken = true, string path = "/trust", string? extraCookie = null)
    {
        var form = await client.GetAsync(new Uri(path, UriKind.Relative));
        var html = await form.Content.ReadAsStringAsync();
        var values = new Dictionary<string, string>(fields) { ["Form.Started"] = HiddenValue(html, "Form.Started") };
        if (withToken) values["__RequestVerificationToken"] = HiddenValue(html, "__RequestVerificationToken");
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = new FormUrlEncodedContent(values) };
        var cookies = form.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).ToList();
        if (extraCookie is not null) cookies.Add(extraCookie);
        req.Headers.Add("Cookie", string.Join("; ", cookies));
        return await client.SendAsync(req);
    }

    internal static string HiddenValue(string html, string name)
    {
        var tag = Regex.Match(html, $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*>");
        var value = Regex.Match(tag.Value, "value=\"([^\"]*)\"");
        return value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : throw new InvalidOperationException($"{name} not found");
    }

    [Fact]
    public async Task TrustCentreShowsTheSixDesignedBlocksAndTheFooter()
    {
        var html = await Html(await Site().Client.GetAsync(new Uri("/trust", UriKind.Relative)));
        // Exact copy from design/Medvya Access and Onboarding (F-07).
        foreach (var (title, body) in new[]
        {
            ("Data residency", "India and UAE run as separate deployments with separate keys, logs and backups."),
            ("Consent model", "Treatment, recording, sharing, reminders and marketing are separate choices."),
            ("AI drafts", "AI output is a draft until a licensed clinician reviews and signs it."),
            ("Retention", "Audio kept 30 days by default; records per local medical rules."),
            ("Audit", "Every access to a chart is logged and reviewable."),
            ("Sub-processors", "Listed with region and purpose; changes announced 30 days ahead."),
        })
            Assert.Contains($"<h2>{title}</h2>\n                    <p>{body}</p>", html.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("<h1 id=\"trust-title\">How Medvya handles patient data</h1>", html, StringComparison.Ordinal);
        Assert.Contains("We describe our controls; we do not claim certifications we have not obtained. Policy versions and sub-processors are listed with dates.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolicyVersionsAndSubProcessorsShowTheirDatesFromTheContentFile()
    {
        var (client, _) = Site(settings: [("TrustCentre:Policies:0:Effective", "2026-12-01"), ("TrustCentre:SubProcessors:0:Effective", "2026-11-15")]);
        var html = await Html(await client.GetAsync(new Uri("/trust", UriKind.Relative)));
        Assert.Contains("<h3>Privacy notice</h3>", html, StringComparison.Ordinal);
        Assert.Contains("<dd>1 Dec 2026</dd>", html, StringComparison.Ordinal);
        Assert.Contains("<dd>15 Nov 2026</dd>", html, StringComparison.Ordinal);
        Assert.Contains("<dd>Not yet in effect</dd>", html, StringComparison.Ordinal); // entries not signed off yet
        Assert.Contains("<dd>Azure Central India · South India</dd>", html, StringComparison.Ordinal);
        Assert.Contains("List version 2026.10-draft · updated 21 Oct 2026", html, StringComparison.Ordinal);

        var uae = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri("/ar/trust", UriKind.Relative)) { Headers = { { "Cookie", "medvya-region=AE" } } });
        var ar = await Html(uae);
        Assert.Contains("<dd>Azure UAE North · UAE Central</dd>", ar, StringComparison.Ordinal);
        Assert.Contains("<h3>إشعار الخصوصية</h3>", ar, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFormAsksOnlyForContactDetailsAndWarnsAgainstPatientInformation()
    {
        var html = await Html(await Site().Client.GetAsync(new Uri("/trust", UriKind.Relative)));
        var names = FieldName().Matches(html).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal);
        Assert.Equal(["Form.Country", "Form.Email", "Form.Message", "Form.Name", "Form.Organisation", "Form.Phone", "Form.Role", "Form.Started", "Form.Website", "__RequestVerificationToken"], names);
        Assert.Contains("Do not include patient information. This form is only for your own contact details.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AValidRequestIsSubmittedEvenWithAStaffSessionCookieAndThanksTheVisitor()
    {
        var (client, store) = Site();
        var res = await Submit(client, Valid, extraCookie: "__Host-medvya=session-cookie-value"); // no CSRF header: the staff session is never read
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/trust?sent=1#demo", res.Headers.Location?.OriginalString);
        var saved = Assert.Single(store.Received);
        Assert.Equal(("Kavya Menon", "IN", "kavya.menon@example.com", (string?)null), (saved.Name, saved.Country, saved.Email, saved.Phone));

        var thanks = await Html(await client.GetAsync(new Uri("/trust?sent=1", UriKind.Relative)));
        Assert.Contains("role=\"status\">Thank you. We have received your request.", thanks, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidRequestsAreRefusedOnTheServerWithFieldErrors()
    {
        var (client, store) = Site();
        var res = await Submit(client, new() { ["Form.Name"] = " ", ["Form.Country"] = "XX", ["Form.Email"] = "not-an-email", ["Form.Message"] = new string('x', 1001) });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var html = await Html(res);
        foreach (var error in new[] { "Enter your name.", "Enter your organisation.", "Enter your role.", "Choose a country.", "Enter a valid email address.", "This is too long." })
            Assert.Contains($"class=\"field-error\">{error}</p>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"demo-name\" name=\"Form.Name\" maxlength=\"100\" autocomplete=\"name\" aria-invalid=\"true\" aria-describedby=\"demo-name-error\" autofocus", html, StringComparison.Ordinal);
        Assert.Empty(store.Received);

        var noContact = await Submit(client, new(Valid) { ["Form.Email"] = "" });
        Assert.Contains("Enter an email address or a phone number.", await Html(noContact), StringComparison.Ordinal);
        Assert.Empty(store.Received);
    }

    [Fact]
    public async Task RequestsWithoutTheAntiForgeryTokenAreRefused()
    {
        var (client, store) = Site();
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(client, Valid, withToken: false)).StatusCode);
        Assert.Empty(store.Received);
    }

    [Fact]
    public async Task BotsAreTurnedAwayWithoutStoringAnything()
    {
        var (client, store) = Site();
        var honeypot = await Submit(client, new(Valid) { ["Form.Website"] = "https://spam.example" });
        Assert.Equal(HttpStatusCode.Redirect, honeypot.StatusCode); // same answer as a real submission
        Assert.Empty(store.Received);

        var (slowClient, slowStore) = Site(minimumSeconds: 30); // submitted faster than a person can type
        var tooFast = await Submit(slowClient, Valid);
        Assert.Equal(HttpStatusCode.BadRequest, tooFast.StatusCode);
        Assert.Contains("This form has expired or was sent too quickly.", await Html(tooFast), StringComparison.Ordinal);
        Assert.Empty(slowStore.Received);
    }

    [Fact]
    public async Task SubmissionsAreRateLimitedPerClient()
    {
        var (client, store) = Site(perHour: 1);
        Assert.Equal(HttpStatusCode.Redirect, (await Submit(client, Valid)).StatusCode);
        var second = await Submit(client, Valid);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Contains("Too many requests from your network.", await Html(second), StringComparison.Ordinal);
        Assert.Single(store.Received);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/trust", UriKind.Relative))).StatusCode); // reading is never limited
    }

    static readonly string[] Languages = ["en", "hi", "ar"];
    static readonly string[] KnownClaims = ["ISO 27001 certified", "HIPAA compliant", "SOC 2 Type II", "प्रमाणित", "معتمد", "GDPR-compliant"];

    // AC: no page claims a certification. Covers every public-site string in every language and the trust centre content file.
    [Fact]
    public void PublicCopyClaimsNoCertification()
    {
        var copy = Languages
            .SelectMany(l => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(RepoRoot(), "packages", "i18n", "messages", $"{l}.json")))!)
            .Where(kv => kv.Key.StartsWith("site.", StringComparison.Ordinal))
            .Select(kv => (Key: kv.Key, Value: kv.Value))
            .Append((Key: "trust-centre.json", Value: File.ReadAllText(Path.Combine(RepoRoot(), "src", "Host.Api", "PublicSite", "trust-centre.json"))))
            .ToList();
        Assert.Contains(copy, c => c.Key == "site.trust.footer");
        Assert.Empty(copy.Where(c => CertificationClaim().IsMatch(c.Value)).Select(c => c.Key));
        // The checker itself catches the claims named in the ticket.
        foreach (var claim in KnownClaims)
            Assert.Matches(CertificationClaim(), claim);
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Medvya.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex("<(?:input|select|textarea)[^>]* name=\"([^\"]+)\"")]
    private static partial Regex FieldName();

    // Standards and certification words in English, Hindi (प्रमाणित certified, मान्यता प्राप्त accredited, अनुपालन compliance)
    // and Arabic (معتمد accredited/certified, شهادة ... حاصل obtained certificate, متوافق compliant).
    [GeneratedRegex(@"\b(ISO\s?27\d{3}|SOC\s?[12]|HIPAA|HITRUST|PCI[\s-]?DSS|NABH|JCI|certified|accredited|compliant)\b|प्रमाणित|मान्यता प्राप्त|अनुपालन|معتمد|متوافق|حاصل على", RegexOptions.IgnoreCase)]
    private static partial Regex CertificationClaim();
}

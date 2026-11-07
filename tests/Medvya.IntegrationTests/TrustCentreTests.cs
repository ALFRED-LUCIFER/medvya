using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Host.Api.PublicSite;
using Medvya.Leads.Application;
using Medvya.Leads.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>Trust centre and demo request (MDV-P1-003, MDV-P1-057/058): designed copy, statuses and dated lists from content, and a guarded /demo form with no patient fields.</summary>
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
        ["Form.Consent"] = "true", ["Form.PreferredAt"] = DemoModel.Value(DateTimeOffset.UtcNow.AddDays(2)),
    };

    // GET the form, then POST it with its anti-forgery cookie and token (as a browser would), optionally overriding fields.
    internal static async Task<HttpResponseMessage> Submit(HttpClient client, Dictionary<string, string> fields, bool withToken = true, string path = "/demo", string? extraCookie = null)
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
    public async Task TrustCentreShowsTheDesignedHeadingTheSixControlsAndTheFooter()
    {
        var html = (await Html(await Site().Client.GetAsync(new Uri("/trust", UriKind.Relative)))).ReplaceLineEndings("\n");
        Assert.Contains("<h1 id=\"trust-title\">Patient data is the most personal data there is. We treat it that way.</h1>", html, StringComparison.Ordinal);
        // Exact copy from docs/02-design/source/Medvya Access and Onboarding (F-07).
        foreach (var (title, body) in new[]
        {
            ("Data residency", "India and UAE run as separate deployments with separate keys, logs and backups."),
            ("Consent model", "Treatment, recording, sharing, reminders and marketing are separate choices."),
            ("AI drafts", "AI output is a draft until a licensed clinician reviews and signs it."),
            ("Retention", "Audio kept 30 days by default; records per local medical rules."),
            ("Audit", "Every access to a chart is logged and reviewable."),
            ("Sub-processors", "Listed with region and purpose; changes announced 30 days ahead."),
        })
            Assert.Contains($"<h3>{title}</h3>\n                    <p>{body}</p>", html, StringComparison.Ordinal);
        Assert.Contains("We describe our controls; we do not claim certifications we have not obtained. Policy versions and sub-processors are listed with dates.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", html, StringComparison.Ordinal); // the demo form is on /demo
    }

    // AC (MDV-P1-058): the block says it is not a certification, and every status comes from trust-centre.json.
    [Theory]
    [InlineData("/trust", "Compliance status", "Updated by Medvya · not a certification", "India · DPDP Act 2023", "In progress", "Planned")]
    [InlineData("/hi/trust", "नियामक स्थिति", "Medvya द्वारा अपडेट · यह कोई प्रमाणन नहीं है", "भारत · DPDP अधिनियम 2023", "काम जारी है", "योजना में")]
    [InlineData("/ar/trust", "حالة الالتزام", "يحدّثها Medvya · ليست شهادة", "الهند · قانون DPDP لعام 2023", "قيد العمل", "مخطط له")]
    public async Task ComplianceStatusesComeFromTheContentFileAndAreNotACertification(string path, string title, string note, string first, string inProgress, string planned)
    {
        var html = await Html(await Site().Client.GetAsync(new Uri(path, UriKind.Relative)));
        Assert.Contains($"<h2 id=\"compliance-title\">{title}</h2>", html, StringComparison.Ordinal);
        Assert.Contains($"<p>{note}</p>", html, StringComparison.Ordinal);
        var block = html[html.IndexOf("id=\"compliance-title\"", StringComparison.Ordinal)..html.IndexOf("id=\"policies-title\"", StringComparison.Ordinal)];
        Assert.Contains($"<strong>{first}</strong>", block, StringComparison.Ordinal);
        Assert.Equal(["inProgress", "planned", "planned", "inProgress", "planned"], Regex.Matches(block, "class=\"status status-(\\w+)\"").Select(m => m.Groups[1].Value));
        Assert.Contains($">{inProgress}</span>", block, StringComparison.Ordinal);
        Assert.Contains($">{planned}</span>", block, StringComparison.Ordinal);

        // Editing the file (or an app setting over it) changes the page: no status is typed into the view.
        var (edited, _) = Site(settings: [("TrustCentre:Compliance:0:Status", "planned")]);
        var after = await Html(await edited.GetAsync(new Uri(path, UriKind.Relative)));
        Assert.StartsWith("class=\"status status-planned\"", after[after.IndexOf("class=\"status ", StringComparison.Ordinal)..], StringComparison.Ordinal);
        // A typo in the file still renders the page, with the weaker status.
        var (typo, _) = Site(settings: [("TrustCentre:Compliance:0:Status", "done")]);
        var typed = await typo.GetAsync(new Uri(path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, typed.StatusCode);
        var typedHtml = await Html(typed);
        Assert.StartsWith("class=\"status status-planned\"", typedHtml[typedHtml.IndexOf("class=\"status ", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    // Every status in the shipped content file has copy (an unknown one would fail the page).
    [Fact]
    public void EveryComplianceStatusInTheContentFileIsKnown()
    {
        var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "src", "Host.Api", "PublicSite", "trust-centre.json")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var statuses = file.RootElement.GetProperty("TrustCentre").GetProperty("Compliance").EnumerateArray().Select(c => c.GetProperty("Status").GetString()).ToList();
        Assert.NotEmpty(statuses);
        Assert.All(statuses, s => Assert.True(s is "planned" or "inProgress", s));
    }

    // Old /trust#demo links (bookmarks, emails) land on a section that links to the form's new page.
    [Fact]
    public async Task OldTrustDemoLinksLandOneLinkAwayFromTheDemoPage()
    {
        var html = await Html(await Site().Client.GetAsync(new Uri("/ar/trust", UriKind.Relative)));
        Assert.Matches("<section id=\"demo\"[^>]*>\\s*<h2[^>]*>[^<]+</h2>\\s*<p>[^<]+</p>\\s*<a class=\"button button-primary\" href=\"/ar/demo\">", html);
        Assert.Equal("/demo", Host.Api.PublicSite.PublicSite.Demo);
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
        Assert.Contains("List version 2026.11-draft · updated 7 Nov 2026", html, StringComparison.Ordinal);

        var uae = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri("/ar/trust", UriKind.Relative)) { Headers = { { "Cookie", "medvya-region=AE" } } });
        var ar = await Html(uae);
        Assert.Contains("<dd>Azure UAE North · UAE Central</dd>", ar, StringComparison.Ordinal);
        Assert.Contains("<h3>إشعار الخصوصية</h3>", ar, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFormAsksOnlyForContactDetailsAndWarnsAgainstPatientInformation()
    {
        var html = await Html(await Site().Client.GetAsync(new Uri("/demo", UriKind.Relative)));
        var names = FieldName().Matches(html).Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal);
        Assert.Equal(["Form.Consent", "Form.Country", "Form.Email", "Form.Message", "Form.Name", "Form.Organisation", "Form.Phone", "Form.PreferredAt", "Form.Role", "Form.Started", "Form.Website", "__RequestVerificationToken"], names);
        Assert.Contains("Do not include patient information. This form is only for your own contact details.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AValidRequestIsSubmittedEvenWithAStaffSessionCookieAndThanksTheVisitor()
    {
        var (client, store) = Site();
        var before = DateTimeOffset.UtcNow;
        var res = await Submit(client, Valid, extraCookie: "__Host-medvya=session-cookie-value"); // no CSRF header: the staff session is never read
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        // Only the requested time (a UTC minute) goes into the address, never a contact value.
        Assert.Equal($"/demo?sent=1&at={Valid["Form.PreferredAt"]}", res.Headers.Location?.OriginalString);
        var saved = Assert.Single(store.Received);
        Assert.Equal(("Kavya Menon", "IN", "kavya.menon@example.com", (string?)null), (saved.Name, saved.Country, saved.Email, saved.Phone));
        Assert.InRange(saved.ConsentedAt!.Value, before, DateTimeOffset.UtcNow); // proof of consent: when, and which notice
        Assert.Equal(TimeSpan.Zero, saved.ConsentedAt.Value.Offset);
        Assert.Equal(LeadRules.NoticeVersion, saved.NoticeVersion);
        Assert.Equal(Valid["Form.PreferredAt"], DemoModel.Value(saved.PreferredAt!.Value));
        Assert.Equal(TimeSpan.Zero, saved.PreferredAt.Value.Offset); // stored in UTC

        var thanks = await Html(await client.GetAsync(new Uri("/demo?sent=1", UriKind.Relative)));
        Assert.Contains("Thank you. We have received your request.</h2>", thanks, StringComparison.Ordinal); // no time in the address: no time shown
        Assert.Contains("<div class=\"demo-sent\" role=\"status\">", thanks, StringComparison.Ordinal);
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

    // AC (MDV-P1-057): a past or malformed time is refused with an error under the field, and nothing is stored.
    [Theory]
    [InlineData("past", "That time has passed. Choose another time.")]
    [InlineData("tomorrow 10am", "Choose one of the times shown.")]
    [InlineData("2026-11-09T04:30+05:30", "Choose one of the times shown.")] // not the UTC form the page sends
    [InlineData("far", "Choose one of the times shown.")]
    [InlineData("", "Choose a time for the demo.")]
    public async Task APastMissingOrMalformedTimeIsRefusedUnderTheField(string value, string error)
    {
        var (client, store) = Site();
        var at = value switch
        {
            "past" => DemoModel.Value(DateTimeOffset.UtcNow.AddMinutes(-1)),
            "far" => DemoModel.Value(DateTimeOffset.UtcNow.AddDays(61)),
            _ => value,
        };
        var res = await Submit(client, new(Valid) { ["Form.PreferredAt"] = at });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var html = await Html(res);
        Assert.Contains($"<p id=\"demo-time-error\" class=\"field-error\">{error}</p>", html, StringComparison.Ordinal);
        Assert.Contains($"<li><a href=\"#demo-time-0\">{error}</a></li>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"demo-time-0\" name=\"Form.PreferredAt\" type=\"radio\"", html, StringComparison.Ordinal);
        Assert.Matches("id=\"demo-time-0\"[^>]* aria-invalid=\"true\" aria-describedby=\"demo-time-hint demo-time-error\" autofocus", html);
        Assert.Empty(store.Received);
    }

    // Slots are the next three weekdays at 10:00, 12:30 and 16:00 in the region's time; each value is that moment in UTC.
    [Theory]
    [InlineData("IN", "India time (IST)", 5.5)]
    [InlineData("AE", "UAE time (GST)", 4)]
    public async Task TimesAreOfferedInTheRegionsTimeZoneAndSentAsUtc(string region, string zone, double offset)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri("/demo", UriKind.Relative)) { Headers = { { "Cookie", $"medvya-region={region}" } } };
        var html = await Html(await Site().Client.SendAsync(req));
        Assert.Contains($"<legend>Preferred time · {zone}</legend>", html, StringComparison.Ordinal);
        var slots = Regex.Matches(html, "name=\"Form.PreferredAt\" type=\"radio\" value=\"([^\"]+)\"[^>]*>\\s*<span dir=\"ltr\">([^<]+)</span>")
            .Select(m => (At: DateTimeOffset.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), Label: m.Groups[2].Value)).ToList();
        Assert.Equal(9, slots.Count);
        Assert.All(slots, s =>
        {
            var local = s.At.ToOffset(TimeSpan.FromHours(offset));
            Assert.Equal(s.Label, local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            Assert.True(s.At > DateTimeOffset.UtcNow);
            Assert.False(local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        });
        Assert.Equal(["10:00", "12:30", "16:00"], slots.Take(3).Select(s => s.Label));
        Assert.Contains("<input id=\"demo-time-0\" name=\"Form.PreferredAt\" type=\"radio\"", html, StringComparison.Ordinal); // none picked for the visitor
        Assert.DoesNotContain("checked", html[html.IndexOf("demo-time-0", StringComparison.Ordinal)..html.IndexOf("demo-message", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    // AC (MDV-P1-057): the confirmation says the time is a request, in the visitor's region time, and offers sign-up.
    [Theory]
    [InlineData("/demo", "IN", "Time requested: Tuesday 1 December, 15:30 India time (IST)", "/region")]
    [InlineData("/demo", "AE", "Time requested: Tuesday 1 December, 14:00 UAE time (GST)", "/region")]
    [InlineData("/ar/demo", "AE", "الوقت المطلوب: الثلاثاء 1 ديسمبر، 14:00 توقيت الإمارات (GST)", "/ar/region")]
    public async Task TheConfirmationShowsTheRequestedTimeInTheRegionsTimeZone(string path, string region, string title, string signUp)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri($"{path}?sent=1&at=2026-12-01T10:00Z", UriKind.Relative)) { Headers = { { "Cookie", $"medvya-region={region}" } } };
        var html = await Html(await Site().Client.SendAsync(req));
        Assert.Contains($"<h2>{title}</h2>", html, StringComparison.Ordinal);
        Assert.Contains($"<a class=\"button button-primary\" href=\"{signUp}\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", html, StringComparison.Ordinal);
        if (path == "/demo")
            Assert.Contains("Our team will confirm it by email or phone and send a video link.", html, StringComparison.Ordinal);

        // A tampered ?at= shows the plain thanks, not the text.
        var tampered = await Html(await Site().Client.GetAsync(new Uri("/demo?sent=1&at=<b>soon</b>", UriKind.Relative)));
        Assert.Contains("<h2>Thank you. We have received your request.</h2>", tampered, StringComparison.Ordinal);
        Assert.DoesNotContain("soon", tampered, StringComparison.Ordinal);
    }

    // The whole no-JavaScript path: a picked slot is stored as the same instant in UTC and confirmed in the region's time.
    [Fact]
    public async Task APickedSlotIsStoredInUtcAndConfirmedInRegionTime()
    {
        var (client, store) = Site();
        using var form = new HttpRequestMessage(HttpMethod.Get, new Uri("/demo", UriKind.Relative)) { Headers = { { "Cookie", "medvya-region=AE" } } };
        var slot = HiddenValue(await (await client.SendAsync(form)).Content.ReadAsStringAsync(), "Form.PreferredAt"); // the first offered slot
        var res = await Submit(client, new(Valid) { ["Form.PreferredAt"] = slot }, extraCookie: "medvya-region=AE");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var saved = Assert.Single(store.Received).PreferredAt!.Value;
        Assert.Equal((TimeSpan.Zero, slot), (saved.Offset, DemoModel.Value(saved)));
        Assert.Equal(new TimeSpan(10, 0, 0), saved.ToOffset(TimeSpan.FromHours(4)).TimeOfDay); // 10:00 in Dubai

        using var thanks = new HttpRequestMessage(HttpMethod.Get, res.Headers.Location) { Headers = { { "Cookie", "medvya-region=AE" } } };
        Assert.Contains(", 10:00 UAE time (GST)</h2>", await Html(await client.SendAsync(thanks)), StringComparison.Ordinal);
    }

    // DPDP Act s.6 / UAE PDPL Art. 6: no tick, no storing. The recording store is the only way to the database and the founder email.
    [Fact]
    public async Task ARequestWithoutConsentIsRefusedAndNothingIsStoredOrEmailed()
    {
        var (client, store) = Site();
        var fields = new Dictionary<string, string>(Valid);
        fields.Remove("Form.Consent");
        var res = await Submit(client, fields);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var html = await Html(res);
        Assert.Contains("<li><a href=\"#demo-consent\">Tick the box to agree, so that we can use your details.</a></li>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"demo-consent\" name=\"Form.Consent\" type=\"checkbox\" value=\"true\" aria-invalid=\"true\" aria-describedby=\"demo-consent-error\" autofocus", html, StringComparison.Ordinal);
        Assert.Contains("value=\"Synthetic General Hospital\"", html, StringComparison.Ordinal); // what was typed is kept
        Assert.Empty(store.Received);

        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(client, new(Valid) { ["Form.Consent"] = "false" })).StatusCode);
        Assert.Empty(store.Received);
    }

    [Fact]
    public async Task TheConsentBoxIsUntickedUntilTheVisitorTicksIt()
    {
        var html = await Html(await Site().Client.GetAsync(new Uri("/demo", UriKind.Relative)));
        Assert.Contains("<input id=\"demo-consent\" name=\"Form.Consent\" type=\"checkbox\" value=\"true\">", html, StringComparison.Ordinal);
        Assert.Contains("I agree to Medvya using these details for the purpose above.", html, StringComparison.Ordinal);
    }

    // DPDP Rules 2025 r.3 and UAE PDPL Art. 13: who, which data, why, no other use, how long, how to withdraw, and where to complain.
    [Theory]
    [InlineData("/demo", "IN", "How Medvya uses these details", "We use them only to contact you about a demo.", "Data Protection Board of India", "UAE Data Office")]
    [InlineData("/demo", "AE", "How Medvya uses these details", "We use them only to contact you about a demo.", "UAE Data Office", "Data Protection Board of India")]
    [InlineData("/region?go=IN", "AE", "How Medvya uses these details", "We use them only to tell you when Medvya opens in India.", "Data Protection Board of India", "UAE Data Office")]
    [InlineData("/region?go=AE", "IN", "How Medvya uses these details", "We use them only to tell you when Medvya opens in UAE.", "UAE Data Office", "Data Protection Board of India")]
    [InlineData("/hi/demo", "IN", "Medvya इन विवरणों का उपयोग कैसे करता है", "हम इनका उपयोग केवल डेमो के बारे में आपसे संपर्क करने के लिए करते हैं।", "भारतीय डेटा संरक्षण बोर्ड", "यूएई डेटा कार्यालय")]
    [InlineData("/hi/region?go=AE", "IN", "Medvya इन विवरणों का उपयोग कैसे करता है", "Medvya यूएई में कब शुरू होता है", "यूएई डेटा कार्यालय", "भारतीय डेटा संरक्षण बोर्ड")]
    [InlineData("/ar/demo", "AE", "كيف يستخدم Medvya هذه البيانات", "نستخدمها فقط للتواصل معك بشأن عرض توضيحي.", "مكتب الإمارات للبيانات", "مجلس حماية البيانات في الهند")]
    [InlineData("/ar/region?go=IN", "AE", "كيف يستخدم Medvya هذه البيانات", "لإبلاغك عند إطلاق Medvya في الهند.", "مجلس حماية البيانات في الهند", "مكتب الإمارات للبيانات")]
    public async Task ThePrivacyNoticeIsShownAboveTheSubmitButtonWithTheRegionsAuthority(string path, string cookie, string title, string purpose, string authority, string otherAuthority)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative)) { Headers = { { "Cookie", $"medvya-region={cookie}" } } };
        var html = (await Html(await Site().Client.SendAsync(req))).ReplaceLineEndings("\n");
        var notice = html.IndexOf($">{title}</h3>", StringComparison.Ordinal);
        Assert.InRange(notice, 0, html.IndexOf("type=\"submit\"", StringComparison.Ordinal));
        var block = html[notice..html.IndexOf("type=\"submit\"", StringComparison.Ordinal)];
        Assert.Contains(purpose, block, StringComparison.Ordinal);
        Assert.Contains(authority, block, StringComparison.Ordinal);
        Assert.DoesNotContain(otherAuthority, block, StringComparison.Ordinal);
        Assert.Contains("<a href=\"mailto:privacy@medvya.example\" dir=\"ltr\">privacy@medvya.example</a>", block, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNoticeListsEveryFieldTheFormCollectsAndTheRetention()
    {
        var html = (await Html(await Site().Client.GetAsync(new Uri("/demo", UriKind.Relative)))).ReplaceLineEndings("\n");
        var items = Regex.Matches(html[html.IndexOf("class=\"notice\"", StringComparison.Ordinal)..], "<li>([^<]+)</li>").Select(m => m.Groups[1].Value);
        Assert.Equal(["Your name", "Organisation", "Your role", "Country", "Email", "Phone", "Preferred demo time", "Message (optional)"], items);
        Assert.Contains("We do not share or sell them, and we do not use them for anything else.", html, StringComparison.Ordinal);
        Assert.Contains("We keep them for 2 years after our last contact with you, then delete them.", html, StringComparison.Ordinal);
    }

    // No privacy contact means no way to withdraw consent, so the notice would be incomplete: no form, and a crafted post stores nothing.
    [Fact]
    public async Task WithoutAPrivacyContactNoFormIsOfferedAndNothingIsStored()
    {
        var store = new RecordingDemoRequests();
        var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("PublicSite:MinimumFillSeconds", "0");
            b.UseSetting("PublicSite:PrivacyContact", "");
            b.ConfigureTestServices(s => s.AddSingleton<IDemoRequests>(store));
        });
        var client = app.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
        var html = await Html(await client.GetAsync(new Uri("/demo", UriKind.Relative)));
        Assert.Contains("This form is not open yet.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", html, StringComparison.Ordinal);

        // A valid token and start time taken from the app itself, as if a form had been rendered.
        var antiforgery = app.Services.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
        var tokens = antiforgery.GetTokens(new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = app.Services });
        var values = new Dictionary<string, string>(Valid) { ["Form.Started"] = app.Services.GetRequiredService<PublicFormGuard>().Start(), [tokens.FormFieldName] = tokens.RequestToken! };
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri("/demo", UriKind.Relative)) { Content = new FormUrlEncodedContent(values) };
        req.Headers.Add("Cookie", $"{app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>>().Value.Cookie.Name}={tokens.CookieToken}");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(req)).StatusCode);
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
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/demo", UriKind.Relative))).StatusCode); // reading is never limited
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")] // IPv4 through a dual-stack socket keeps its own quota
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::9", "2001:db8:1:2::/64")] // same subscriber, same quota
    public void TheQuotaCountsAnIPv6SubscriberNotEachAddress(string ip, string key) =>
        Assert.Equal(key, PublicFormGuard.Client(IPAddress.Parse(ip)));

    static readonly string[] Languages = ["en", "hi", "ar"];
    static readonly string[] KnownClaims = ["ISO 27001 certified", "HIPAA compliant", "SOC 2 Type II", "प्रमाणित", "معتمد", "GDPR-compliant"];

    // AC: no page claims a certification. Covers every public-site string in every language, the trust centre content file and
    // every page as rendered in each language and region (so text typed straight into a page is caught too).
    [Fact]
    public async Task PublicCopyClaimsNoCertification()
    {
        var copy = Languages
            .SelectMany(l => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(RepoRoot(), "packages", "i18n", "messages", $"{l}.json")))!)
            .Where(kv => kv.Key.StartsWith("site.", StringComparison.Ordinal))
            .Select(kv => (Key: kv.Key, Value: kv.Value))
            .Append((Key: "trust-centre.json", Value: File.ReadAllText(Path.Combine(RepoRoot(), "src", "Host.Api", "PublicSite", "trust-centre.json"))))
            .ToList();
        var client = Site().Client;
        foreach (var page in PublicSiteTests.PagePaths)
            foreach (var region in Host.Api.PublicSite.PublicSite.Regions)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(page, UriKind.Relative)) { Headers = { { "Cookie", $"medvya-region={region}" } } };
                copy.Add(($"{page} ({region})", await Html(await client.SendAsync(req))));
            }
        Assert.Contains(copy, c => c.Key == "site.trust.footer");
        Assert.Contains(copy, c => c.Key == "/ar/region (AE)");
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

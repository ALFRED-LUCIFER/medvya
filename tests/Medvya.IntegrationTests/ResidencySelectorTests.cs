using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Medvya.Leads.Application;
using Medvya.Leads.Domain;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using static Medvya.IntegrationTests.TrustCentreTests;

namespace Medvya.IntegrationTests;

/// <summary>F-02 country and data-residency selector (MDV-P1-002): designed copy, one deployment per country, waitlist when not live.</summary>
public sealed partial class ResidencySelectorTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    sealed class RecordingWaitlist : IWaitlist
    {
        public List<WaitlistEntry> Received { get; } = [];
        public Task JoinAsync(WaitlistEntry entry, CancellationToken cancellationToken = default) { Received.Add(entry); return Task.CompletedTask; }
    }

    const string IndiaSignUp = "https://in.medvya.test/signup";

    (HttpClient Client, RecordingWaitlist Store) Site()
    {
        var store = new RecordingWaitlist();
        var app = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("PublicSite:MinimumFillSeconds", "0");
            b.UseSetting("PublicSite:Deployments:SignUpUrls:IN", IndiaSignUp); // India live, UAE not (as configured per deployment)
            b.ConfigureTestServices(s => s.AddSingleton<IWaitlist>(store));
        });
        return (app.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false }), store);
    }

    static Task<HttpResponseMessage> Get(HttpClient client, string path, string? region = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (region is not null) req.Headers.Add("Cookie", $"medvya-region={region}");
        return client.SendAsync(req);
    }

    [Fact]
    public async Task TheSelectorHasTheDesignedCopy()
    {
        var html = await Html(await Get(Site().Client, "/region"));
        // Exact copy from docs/02-design/screen-specs.md F-02 and the design file.
        Assert.Contains("<h1 id=\"region-title\">Where do you practise?</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Your patients' data is stored and processed in this country. It cannot be moved to the other region later without a new account and export.", html, StringComparison.Ordinal);
        Assert.Contains("<strong>India</strong><span>Azure Central India · DPDP Act · ABDM integration available</span>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>UAE</strong><span>Azure UAE North · separate deployment · DoH / DHA / MOHAP integrations per emirate</span>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "IN", "Continue in India")]
    [InlineData("AE", "AE", "Continue in UAE")]
    public async Task ThePickedCardIsCurrentAndTheButtonContinuesInThatCountry(string? cookie, string region, string cta)
    {
        var html = await Html(await Get(Site().Client, "/region", cookie));
        Assert.Contains($"href=\"?region={region}\" aria-current=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"?go={region}\">{cta}</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALiveCountryContinuesToItsOwnDeploymentOnly()
    {
        var (client, _) = Site();
        var india = await Get(client, "/region?go=IN");
        Assert.Equal(HttpStatusCode.Redirect, india.StatusCode);
        Assert.Equal($"{IndiaSignUp}?country=IN", india.Headers.Location?.OriginalString);

        var uae = await Get(client, "/ar/region?go=AE"); // not live: never sent to India's deployment
        Assert.Equal(HttpStatusCode.OK, uae.StatusCode);
        var html = await Html(uae);
        Assert.Contains("<h2 id=\"waitlist-title\">لم يُطلق Medvya في الإمارات بعد</h2>", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Form.Country\" value=\"AE\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"?region=AE\" aria-current=\"true\"", html, StringComparison.Ordinal); // cards follow the country continued with

        Assert.DoesNotContain("waitlist-title", await Html(await Get(client, "/region?go=XX")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheWaitlistAsksOnlyForContactDetails()
    {
        var html = await Html(await Get(Site().Client, "/region?go=AE"));
        var names = FieldName().Matches(html).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal);
        Assert.Equal(["Form.Country", "Form.Email", "Form.Name", "Form.Phone", "Form.Started", "Form.Website", "__RequestVerificationToken"], names);
        Assert.Contains("Do not include patient information.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JoiningTheWaitlistStoresTheCountryAndThanksTheVisitor()
    {
        var (client, store) = Site();
        var res = await Submit(client, new() { ["Form.Country"] = "AE", ["Form.Name"] = "Fatima Al Hashimi", ["Form.Email"] = "fatima@example.com", ["Form.Phone"] = "" }, path: "/region?go=AE");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/region?go=AE&sent=1#waitlist", res.Headers.Location?.OriginalString);
        var saved = Assert.Single(store.Received);
        Assert.Equal(("Fatima Al Hashimi", "AE", "fatima@example.com"), (saved.Name, saved.Country, saved.Email));
        Assert.Contains("Thank you. You are on the waitlist for UAE.", await Html(await Get(client, "/region?go=AE&sent=1")), StringComparison.Ordinal);

        var invalid = await Submit(client, new() { ["Form.Country"] = "AE", ["Form.Name"] = "", ["Form.Email"] = "" }, path: "/region?go=AE");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("Enter an email address or a phone number.", await Html(invalid), StringComparison.Ordinal);
        Assert.Single(store.Received);
    }

    [Fact]
    public async Task AWaitlistPostWithoutTheAntiForgeryTokenIsRefused()
    {
        var (client, store) = Site();
        var fields = new Dictionary<string, string> { ["Form.Country"] = "AE", ["Form.Name"] = "Fatima Al Hashimi", ["Form.Email"] = "fatima@example.com" };
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(client, fields, withToken: false, path: "/region?go=AE")).StatusCode);
        Assert.Empty(store.Received);
    }

    [Fact]
    public async Task AWaitlistForAnUnknownCountryIsRefused()
    {
        var (client, store) = Site();
        var res = await SubmitWith(client, "/region?go=AE", new() { ["Form.Name"] = "x", ["Form.Email"] = "a@example.com", ["Form.Country"] = "US" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Empty(store.Received);
    }

    // Like Submit, but lets the caller override the form's own hidden country.
    static async Task<HttpResponseMessage> SubmitWith(HttpClient client, string path, Dictionary<string, string> fields)
    {
        var form = await client.GetAsync(new Uri(path, UriKind.Relative));
        var html = await form.Content.ReadAsStringAsync();
        var values = new Dictionary<string, string>(fields)
        {
            ["Form.Started"] = HiddenValue(html, "Form.Started"), ["__RequestVerificationToken"] = HiddenValue(html, "__RequestVerificationToken"),
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = new FormUrlEncodedContent(values) };
        req.Headers.Add("Cookie", string.Join("; ", form.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])));
        return await client.SendAsync(req);
    }

    [GeneratedRegex("<(?:input|select|textarea)[^>]* name=\"([^\"]+)\"")]
    private static partial Regex FieldName();
}

/// <summary>AC: a tenant's country and residency region cannot change after it is created (ADR-003, MDV-P1-002).</summary>
public class TenantResidencyTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    [Theory]
    [InlineData(nameof(Tenant.CountryCode))]
    [InlineData(nameof(Tenant.ResidencyRegion))]
    public void CountryAndRegionAreSetOnlyAtCreation(string property)
    {
        var setter = typeof(Tenant).GetProperty(property)!.SetMethod!;
        Assert.Contains(typeof(IsExternalInit), setter.ReturnParameter.GetRequiredCustomModifiers()); // init-only in the domain
        using var db = sql.Context(null);
        Assert.Equal(PropertySaveBehavior.Throw, db.Model.FindEntityType(typeof(Tenant))!.FindProperty(property)!.GetAfterSaveBehavior());
    }

    [Fact]
    public async Task ChangingAStoredTenantsCountryIsRefused()
    {
        var id = Guid.NewGuid();
        await using (var db = sql.Context(id))
        {
            db.Tenants.Add(new Tenant { Id = id, Name = "Synthetic Residency Clinic", CountryCode = "IN", ResidencyRegion = "centralindia" });
            await db.SaveChangesAsync();
        }
        await using var again = sql.Context(id);
        var tenant = await again.Tenants.SingleAsync(t => t.Id == id);
        again.Entry(tenant).Property(t => t.CountryCode).CurrentValue = "AE";
        await Assert.ThrowsAsync<InvalidOperationException>(() => again.SaveChangesAsync());

        await using var check = sql.Context(id);
        Assert.Equal("IN", (await check.Tenants.SingleAsync(t => t.Id == id)).CountryCode);
    }
}

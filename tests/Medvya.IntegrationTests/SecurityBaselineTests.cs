using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

public class SecurityBaselineTests
{
    static HttpClient Client(string environment = "Production", int permitsPerMinute = 300) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment(environment).UseSetting("RateLimiting:PermitsPerMinute", permitsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .CreateClient(new() { BaseAddress = new Uri("https://clinic.medvya.test") /* HSTS is skipped for localhost by design */ });

    [Fact]
    public async Task ResponsesCarrySecurityHeaders()
    {
        var res = await Client().GetAsync(new Uri("/health", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var csp = Assert.Single(res.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(res.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(res.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(res.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("camera=()", Assert.Single(res.Headers.GetValues("Permissions-Policy")), StringComparison.Ordinal);
        Assert.StartsWith("max-age=31536000", Assert.Single(res.Headers.GetValues("Strict-Transport-Security")), StringComparison.Ordinal);
        Assert.False(res.Headers.Contains("Server"));
    }

    [Fact]
    public async Task HealthReportsTheBuildVersion()
    {
        // The deploy smoke test compares this with the tag it published (MDV-P0-007).
        var health = await Client().GetFromJsonAsync<JsonElement>(new Uri("/health", UriKind.Relative));
        var expected = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(expected, health.GetProperty("version").GetString());
    }

    [Fact]
    public async Task ErrorResponsesAlsoCarrySecurityHeaders()
    {
        var res = await Client().GetAsync(new Uri("/does-not-exist", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.True(res.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task ExcessRequestsFromOneClientGet429()
    {
        var client = Client(permitsPerMinute: 2);
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++) codes.Add((await client.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], codes);
    }

    // MDV-P1-038: the public site has no sign-in, so the per-IP limit is its only brake on scraping and floods.
    [Theory]
    [InlineData("/")]
    [InlineData("/assist")]
    [InlineData("/hospitals")]
    [InlineData("/pricing")]
    [InlineData("/demo")]
    [InlineData("/trust")]
    [InlineData("/region")]
    [InlineData("/ar/pricing")]
    public async Task PublicPagesAreRateLimitedPerClient(string path)
    {
        var client = Client(permitsPerMinute: 1);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri(path, UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync(new Uri(path, UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task TvPairingHasItsOwnTighterLimit()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("RateLimiting:DisplayPairingPerMinute", "1"));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://clinic.medvya.test") });
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 2; i++) codes.Add((await client.PostAsJsonAsync(new Uri("/v1/queue/tv/session", UriKind.Relative), new { token = "not-a-display" })).StatusCode);
        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], codes);
    }

    [Fact]
    public async Task TheDeliveryReceiptWebhookIsRateLimitedPerClient()
    {
        // Its own 600 a minute (a provider sends bursts), under a global limit raised out of the way.
        using var client = Client(permitsPerMinute: 10_000);
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 601; i++)
        {
            using var unsigned = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            codes.Add((await client.PostAsync(new Uri("/v1/notifications/receipts", UriKind.Relative), unsigned)).StatusCode);
        }
        Assert.All(codes[..600], c => Assert.Equal(HttpStatusCode.Unauthorized, c));
        Assert.Equal(HttpStatusCode.TooManyRequests, codes[600]);
    }

    [Fact]
    public async Task OversizedBodiesAreRejected()
    {
        using var body = new ByteArrayContent(new byte[1_048_577]);
        var res = await Client().PostAsync(new Uri("/health", UriKind.Relative), body);
        Assert.NotEqual(HttpStatusCode.OK, res.StatusCode);
    }

    /// <summary>Every request arrives from one front-end proxy at 10.0.0.5, as behind App Service.</summary>
    sealed class FromProxy : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) => { ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5"); return nxt(ctx); });
            next(app);
        };
    }

    static Task<List<HttpStatusCode>> FromClients(string? knownProxy, params string[] clients) => FromClients(knownProxy, appService: false, clients);

    static async Task<List<HttpStatusCode>> FromClients(string? knownProxy, bool appService, params string[] clients)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:PermitsPerMinute", "1").ConfigureServices(s => s.AddSingleton<IStartupFilter, FromProxy>());
            if (knownProxy is not null) b.UseSetting("ForwardedHeaders:KnownProxies:0", knownProxy);
            // App Service sets this environment variable on every instance; clients cannot set it.
            if (appService) b.UseSetting("WEBSITE_INSTANCE_ID", "synthetic-instance");
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://clinic.medvya.test") });
        var codes = new List<HttpStatusCode>();
        foreach (var ip in clients)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health", UriKind.Relative));
            request.Headers.Add("X-Forwarded-For", ip);
            codes.Add((await client.SendAsync(request)).StatusCode);
        }
        return codes;
    }

    [Fact]
    public async Task BehindAKnownProxyEachClientHasItsOwnLimit() =>
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests],
            await FromClients("10.0.0.5", "203.0.113.1", "203.0.113.2", "203.0.113.1"));

    [Fact]
    public async Task ForwardedAddressesFromAnUnlistedProxyAreIgnored() =>
        // No proxy listed (the default): a caller cannot pick a fresh address per request to dodge the limit.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], await FromClients(null, "203.0.113.1", "203.0.113.2"));

    // MDV-P0-007: on App Service no proxy address is listed (the front end's addresses are not fixed), yet each phone and
    // browser must get its own sign-in and API limit.
    [Fact]
    public async Task OnAppServiceEachClientHasItsOwnLimit() =>
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests],
            await FromClients(null, appService: true, "203.0.113.1", "203.0.113.2", "203.0.113.1"));

    [Fact]
    public async Task OnAppServiceTheAddressCountsWithoutItsPort() =>
        // The front end writes "address:port"; a new source port is the same client.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests],
            await FromClients(null, appService: true, "203.0.113.1:50432", "203.0.113.1:50999"));

    [Fact]
    public async Task OnAppServiceAnAddressTheClientWroteItselfIsIgnored() =>
        // The client sends its own X-Forwarded-For; the front end appends the real address. Only that last entry counts.
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests],
            await FromClients(null, appService: true, "198.51.100.9, 203.0.113.1", "198.51.100.10, 203.0.113.1"));
}

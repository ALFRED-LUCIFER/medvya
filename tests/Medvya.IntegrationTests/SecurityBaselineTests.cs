using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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

    [Fact]
    public async Task OversizedBodiesAreRejected()
    {
        using var body = new ByteArrayContent(new byte[1_048_577]);
        var res = await Client().PostAsync(new Uri("/health", UriKind.Relative), body);
        Assert.NotEqual(HttpStatusCode.OK, res.StatusCode);
    }
}

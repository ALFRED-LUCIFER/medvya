using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Host.Api.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Medvya.IntegrationTests;

/// <summary>#1417: a mobile build below the configured minimum is refused with 426 and a stable code; the web (no header) never is.</summary>
public sealed class MobileAppVersionTests
{
    static WebApplicationFactory<Program> Host(string? minimum) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("Onboarding:DeploymentCountry", "IN");
        if (minimum is not null) b.UseSetting("MobileApp:MinimumVersion", minimum);
    });

    static async Task<HttpResponseMessage> Health(WebApplicationFactory<Program> host, string? version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health", UriKind.Relative));
        if (version is not null) request.Headers.Add(MobileAppVersion.Header, version);
        return await host.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task AnOldBuildGets426WithTheUpgradeCodeAndEveryOtherRequestPasses()
    {
        using var host = Host("2.1.0");
        using var old = await Health(host, "2.0.9");
        Assert.Equal(HttpStatusCode.UpgradeRequired, old.StatusCode);
        Assert.Equal("app.upgrade_required", (await old.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UpgradeRequired, (await Health(host, "not-a-version")).StatusCode); // the app's claim: unreadable counts as old
        Assert.Equal(HttpStatusCode.OK, (await Health(host, "2.1.0")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Health(host, "3.0.0")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Health(host, null)).StatusCode); // the web apps send no header
    }

    [Fact]
    public async Task WithoutAMinimumNothingIsRefused()
    {
        using var host = Host(null);
        Assert.Equal(HttpStatusCode.OK, (await Health(host, "0.0.1")).StatusCode);
    }

    [Fact]
    public void AnUnreadableMinimumStopsTheHost()
    {
        using var host = Host("soon");
        Assert.Contains("MobileApp:MinimumVersion", Assert.Throws<InvalidOperationException>(() => host.CreateClient()).Message, StringComparison.Ordinal);
    }
}

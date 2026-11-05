using System.Net;
using Medvya.Identity.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-007: the API serves the built web app under /app/ (the public site keeps /), and sign-in returns there.</summary>
public sealed class WebAppTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IDisposable
{
    readonly string webRoot = Directory.CreateTempSubdirectory("medvya-webroot").FullName;
    WebApplicationFactory<Program>? factory;

    HttpClient Browser(bool built = true)
    {
        if (built)
        {
            Directory.CreateDirectory(Path.Combine(webRoot, "app", "assets"));
            File.WriteAllText(Path.Combine(webRoot, "app", "index.html"), "<!doctype html><title>shell</title>");
            File.WriteAllText(Path.Combine(webRoot, "app", "assets", "main-abc123.js"), "export {}");
        }
        factory = StaffSignInTests.CreateFactory(sql.ConnectionString, b => b.UseSetting("Web:AppPath", "/app/").UseWebRoot(webRoot));
        return factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        factory?.Dispose();
        Directory.Delete(webRoot, recursive: true);
    }

    [Theory]
    [InlineData("/app/")]
    [InlineData("/app/schedule")] // a client-side route reloaded in the browser
    [InlineData("/app/invite/abc")]
    public async Task ClientRoutesLoadTheShellAndItIsNeverCached(string path)
    {
        using var browser = Browser();
        var res = await browser.GetAsync(new Uri(path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("<title>shell</title>", await res.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("no-cache", res.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task HashedBuildFilesAreCachedForeverAndAMissingOneIsA404()
    {
        using var browser = Browser();
        var asset = await browser.GetAsync(new Uri("/app/assets/main-abc123.js", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains("immutable", asset.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        // A missing file must not answer with the shell page (a stale tab would run HTML as script).
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(new Uri("/app/assets/gone.js", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task ThePublicSiteAndTheApiKeepTheirOwnPaths()
    {
        using var browser = Browser();
        var home = await browser.GetAsync(new Uri("/", UriKind.Relative));
        Assert.DoesNotContain("<title>shell</title>", await home.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task SignInWithoutAReturnAddressLandsInTheApp()
    {
        var id = Guid.NewGuid();
        await using (var db = sql.Identity())
        {
            db.Users.Add(new StaffUser { Id = id, Issuer = IdentityProviders.Development, Subject = id.ToString(), DisplayName = "Web App (synthetic)" });
            await db.SaveChangesAsync();
        }
        using var browser = Browser();
        var challenge = await browser.GetAsync(new Uri("/bff/login", UriKind.Relative));
        var authorize = await browser.GetAsync(new Uri($"{challenge.Headers.Location}&login_hint={id}"));
        var callback = await browser.GetAsync(authorize.Headers.Location);
        Assert.Equal("/app/", callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ARefusedSignInReturnsToTheAppWhereTheMessageShows()
    {
        var id = Guid.NewGuid();
        await using (var db = sql.Identity())
        {
            db.Users.Add(new StaffUser { Id = id, Issuer = IdentityProviders.Development, Subject = id.ToString(), DisplayName = "No MFA (synthetic)" });
            await db.SaveChangesAsync();
        }
        using var browser = Browser();
        var challenge = await browser.GetAsync(new Uri("/bff/login", UriKind.Relative));
        var authorize = await browser.GetAsync(new Uri($"{challenge.Headers.Location}&login_hint={id}&no_mfa=true"));
        var callback = await browser.GetAsync(authorize.Headers.Location);
        Assert.Equal("/app/?signin=failed", callback.Headers.Location!.OriginalString);
    }
}

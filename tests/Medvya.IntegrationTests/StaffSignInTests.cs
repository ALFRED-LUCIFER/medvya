using System.Net;
using System.Net.Http.Json;
using Host.Api.Auth;
using Medvya.Identity.Domain;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-011: OIDC code + PKCE through the BFF, MFA required, Medvya accounts only, tokens never reach the browser.</summary>
public sealed class StaffSignInTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IDisposable
{
    readonly WebApplicationFactory<Program> factory = CreateFactory(sql.ConnectionString);

    internal static WebApplicationFactory<Program> CreateFactory(string connectionString)
    {
        WebApplicationFactory<Program> factory = null!;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("ConnectionStrings:Medvya", connectionString)
            .UseSetting("Authentication:Oidc:Authority", "http://localhost/dev-idp")
            // The handler fetches discovery, keys and tokens from the in-process dev provider.
            .ConfigureServices(s => s.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme,
                o => o.BackchannelHttpHandler = factory.Server.CreateHandler())));
        return factory;
    }

    public void Dispose() => factory.Dispose();

    HttpClient Browser() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    async Task<StaffUser> Account(string name)
    {
        var id = Guid.NewGuid();
        var user = new StaffUser { Id = id, Issuer = IdentityProviders.Development, Subject = id.ToString(), DisplayName = $"{name} (synthetic)" };
        await using var db = sql.Identity();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Walks the browser through login → provider → callback; returns the callback response.</summary>
    internal static async Task<HttpResponseMessage> SignIn(HttpClient browser, string subject, string extra = "", string returnUrl = "/", Func<Task>? beforeCallback = null)
    {
        var challenge = await browser.GetAsync(new Uri($"/bff/login?returnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.Contains("code_challenge_method=S256", challenge.Headers.Location!.Query, StringComparison.Ordinal);
        var authorize = await browser.GetAsync(new Uri($"{challenge.Headers.Location}&login_hint={subject}{extra}"));
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        if (beforeCallback is not null) await beforeCallback();
        return await browser.GetAsync(authorize.Headers.Location);
    }

    [Fact]
    public async Task SignedInStaffGetAnHttpOnlySessionCookieAndNoTokens()
    {
        var user = await Account("Arjun Rao");
        using var browser = Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative))).StatusCode);

        var callback = await SignIn(browser, user.Subject, returnUrl: "/schedule");
        Assert.Equal("/schedule", callback.Headers.Location!.OriginalString);
        var session = Assert.Single(callback.Headers.GetValues("Set-Cookie"), c => c.StartsWith(StaffAuthentication.CookieName + "=", StringComparison.Ordinal));
        foreach (var flag in new[] { "httponly", "secure", "samesite=strict", "path=/" })
            Assert.Contains(flag, session, StringComparison.OrdinalIgnoreCase);

        var me = await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await me.Content.ReadAsStringAsync();
        Assert.Contains(user.DisplayName, body, StringComparison.Ordinal);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignInWithoutMfaIsRefused()
    {
        var user = await Account("Joseph");
        using var browser = Browser();
        var callback = await SignIn(browser, user.Subject, extra: "&no_mfa=true");
        Assert.Equal("/?signin=failed", callback.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task ProviderAccountWithoutAMedvyaAccountIsRefused()
    {
        var user = await Account("Thomas");
        using var browser = Browser();
        var callback = await SignIn(browser, user.Subject, beforeCallback: async () =>
        {
            await using var db = sql.Identity(); // the account goes away between provider sign-in and callback
            await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        });
        Assert.Equal("/?signin=failed", callback.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative))).StatusCode);
    }

    [Theory]
    [InlineData("//evil.example/x")]
    [InlineData("/\\evil.example")]
    [InlineData("https://evil.example")]
    public async Task ReturnUrlNeverLeavesTheSite(string returnUrl)
    {
        var user = await Account("Priya");
        using var browser = Browser();
        Assert.Equal("/", (await SignIn(browser, user.Subject, returnUrl: returnUrl)).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task StateChangingRequestsNeedTheCsrfHeader()
    {
        var user = await Account("Lakshmi");
        using var browser = Browser();
        await SignIn(browser, user.Subject);

        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync(new Uri("/bff/logout", UriKind.Relative), null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative))).StatusCode);

        using var logout = new HttpRequestMessage(HttpMethod.Post, new Uri("/bff/logout", UriKind.Relative));
        logout.Headers.Add(StaffAuthentication.CsrfHeader, "1");
        Assert.Equal(HttpStatusCode.NoContent, (await browser.SendAsync(logout)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task DevelopmentProviderDoesNotExistOutsideDevelopment()
    {
        using var production = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("Authentication:Oidc:Authority", "http://localhost/dev-idp"));
        using var browser = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        // Production refuses a provider over plain http on every request (fail closed), and the dev provider is not mapped.
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => browser.GetAsync(new Uri("/dev-idp/.well-known/openid-configuration", UriKind.Relative)));
        Assert.Contains("HTTPS", ex.Message, StringComparison.Ordinal);
        using var noProvider = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Production"));
        Assert.Equal(HttpStatusCode.NotFound, (await noProvider.CreateClient().GetAsync(new Uri("/dev-idp/.well-known/openid-configuration", UriKind.Relative))).StatusCode);
    }
}

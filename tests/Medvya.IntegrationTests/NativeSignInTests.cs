using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Host.Api.Auth;
using Medvya.Identity.Domain;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P5-033: native app sign-in (OIDC in the system browser, PKCE, one-time code), rotating refresh tokens, replay and revocation.</summary>
public sealed class NativeSignInTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IDisposable
{
    const string Redirect = "medvya-clinician://auth";
    readonly WebApplicationFactory<Program> factory = StaffSignInTests.CreateFactory(sql.ConnectionString);
    readonly Guid tenant = Guid.NewGuid();

    public void Dispose() => factory.Dispose();

    HttpClient Browser() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    sealed record Tokens(string Access, string Refresh);

    async Task<(StaffUser User, Guid Membership)> Doctor()
    {
        var facility = Guid.NewGuid();
        await using (var db = sql.Context(tenant))
        {
            db.Tenants.Add(new Tenant { Id = tenant, Name = "Iyer Family Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            db.Facilities.Add(new Facility { Id = facility, TenantId = tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" });
            await db.SaveChangesAsync();
        }
        var id = Guid.NewGuid();
        var user = new StaffUser { Id = id, Issuer = IdentityProviders.Development, Subject = id.ToString(), DisplayName = "Dr. Arjun Rao (synthetic)" };
        await using (var db = sql.Identity())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        var membership = new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = id, FacilityId = facility, Role = Role.SoloDoctor, Status = MembershipStatus.Active };
        await using (var db = sql.Context(tenant))
        {
            db.Memberships.Add(membership);
            await db.SaveChangesAsync();
        }
        return (user, membership.Id);
    }

    static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    /// <summary>The system browser's part: login → provider → callback → complete. Returns the code from the app redirect.</summary>
    static async Task<string> BrowserSignIn(HttpClient browser, string subject, string challenge, Action<HttpResponseMessage>? onCallback = null)
    {
        var start = await browser.GetAsync(new Uri($"/native/login?redirect_uri={Uri.EscapeDataString(Redirect)}&code_challenge={challenge}&code_challenge_method=S256", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Assert.Contains("prompt=login", start.Headers.Location!.Query, StringComparison.Ordinal);
        var authorize = await browser.GetAsync(new Uri($"{start.Headers.Location}&login_hint={subject}"));
        var callback = await browser.GetAsync(authorize.Headers.Location);
        onCallback?.Invoke(callback);
        Assert.Equal("/native/complete", callback.Headers.Location!.OriginalString);
        var complete = await browser.GetAsync(callback.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode);
        var location = complete.Headers.Location!.OriginalString;
        Assert.StartsWith(Redirect + "?code=", location, StringComparison.Ordinal);
        return Uri.UnescapeDataString(location[(Redirect.Length + "?code=".Length)..]);
    }

    async Task<HttpResponseMessage> Token(params (string Key, string Value)[] fields)
    {
        using var client = Browser();
        return await client.PostAsync(new Uri("/native/token", UriKind.Relative), new FormUrlEncodedContent(fields.Select(f => KeyValuePair.Create(f.Key, f.Value))));
    }

    static async Task<Tokens> Read(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(900, body.GetProperty("expires_in").GetInt32());
        return new Tokens(body.GetProperty("access_token").GetString()!, body.GetProperty("refresh_token").GetString()!);
    }

    async Task<Tokens> SignIn(StaffUser user)
    {
        var (verifier, challenge) = Pkce();
        using var browser = Browser();
        var code = await BrowserSignIn(browser, user.Subject, challenge);
        return await Read(await Token(("grant_type", "authorization_code"), ("code", code), ("code_verifier", verifier), ("redirect_uri", Redirect)));
    }

    Task<HttpResponseMessage> Refresh(string refresh) => Token(("grant_type", "refresh_token"), ("refresh_token", refresh));

    async Task<HttpResponseMessage> Api(string access, HttpMethod method, string path, object? body = null)
    {
        using var client = Browser();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return await client.SendAsync(request);
    }

    async Task<StaffSession> SessionOf(Guid userId)
    {
        await using var db = sql.Identity();
        return await db.Sessions.AsNoTracking().OrderByDescending(s => s.CreatedAt).FirstAsync(s => s.UserId == userId);
    }

    [Fact]
    public async Task AppSignsInChoosesAFacilityAndRefreshesWithoutACookie()
    {
        var (user, membership) = await Doctor();
        var tokens = await SignIn(user);
        Assert.True((await SessionOf(user.Id)).IsNative);

        Assert.Equal(HttpStatusCode.OK, (await Api(tokens.Access, HttpMethod.Get, "/v1/me/context")).StatusCode);
        // No CSRF header: a bearer request carries no cookie, so there is nothing to forge.
        Assert.Equal(HttpStatusCode.NoContent, (await Api(tokens.Access, HttpMethod.Post, "/v1/me/facility", new { membershipId = membership })).StatusCode);
        // The old access token names no facility while the session now does: refused until the app refreshes.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(tokens.Access, HttpMethod.Get, "/v1/me/context")).StatusCode);

        var next = await Read(await Refresh(tokens.Refresh));
        var context = await (await Api(next.Access, HttpMethod.Get, "/v1/me/context")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(membership, context.GetProperty("active").GetProperty("membershipId").GetGuid());
        Assert.Equal("SoloDoctor", context.GetProperty("active").GetProperty("role").GetString());
    }

    [Fact]
    public async Task TheBrowserCookieFromTheSignInCannotReachTheNativeSession()
    {
        var (user, _) = await Doctor();
        var (_, challenge) = Pkce();
        using var browser = Browser();
        string? cookie = null;
        await BrowserSignIn(browser, user.Subject, challenge, callback =>
            cookie = callback.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(StaffAuthentication.CookieName + "=", StringComparison.Ordinal)).Split(';')[0]);

        // A copy of the cookie taken before /native/complete cleared it: the session is native now, so the cookie is refused.
        using var stolen = Browser();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/me/context", UriKind.Relative));
        request.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stolen.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData("medvya-evil://auth", "S256")]
    [InlineData(Redirect, "plain")]
    public async Task LoginRefusesAnUnlistedRedirectOrAWeakChallenge(string redirect, string method)
    {
        using var browser = Browser();
        var (_, challenge) = Pkce();
        var res = await browser.GetAsync(new Uri($"/native/login?redirect_uri={Uri.EscapeDataString(redirect)}&code_challenge={challenge}&code_challenge_method={method}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task ACodeNeedsItsOwnVerifierAndRedirectAndWorksOnce()
    {
        var (user, _) = await Doctor();
        var (verifier, challenge) = Pkce();
        using var browser = Browser();
        var code = await BrowserSignIn(browser, user.Subject, challenge);

        Assert.Equal(HttpStatusCode.BadRequest, (await Token(("grant_type", "authorization_code"), ("code", code), ("code_verifier", Pkce().Verifier), ("redirect_uri", Redirect))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Token(("grant_type", "authorization_code"), ("code", code), ("code_verifier", verifier), ("redirect_uri", "medvya-patient://auth"))).StatusCode);

        var tokens = await Read(await Token(("grant_type", "authorization_code"), ("code", code), ("code_verifier", verifier), ("redirect_uri", Redirect)));
        // The same code again means someone else has it: the session ends, including the tokens already issued.
        Assert.Equal(HttpStatusCode.BadRequest, (await Token(("grant_type", "authorization_code"), ("code", code), ("code_verifier", verifier), ("redirect_uri", Redirect))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(tokens.Access, HttpMethod.Get, "/v1/me/context")).StatusCode);
    }

    [Fact]
    public async Task AReplayedRefreshTokenEndsTheWholeSessionAndIsAudited()
    {
        var (user, membership) = await Doctor();
        var first = await SignIn(user);
        await Api(first.Access, HttpMethod.Post, "/v1/me/facility", new { membershipId = membership });
        var second = await Read(await Refresh(first.Refresh));

        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(first.Refresh)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(second.Access, HttpMethod.Get, "/v1/me/context")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(second.Refresh)).StatusCode);
        Assert.NotNull((await SessionOf(user.Id)).RevokedAt);
        await using var audit = sql.Audit(tenant);
        Assert.True(await audit.Events.AnyAsync(e => e.ActorUserId == user.Id && e.Action == "session.refresh.replay"));
    }

    [Fact]
    public async Task AForgedRefreshTokenWithKnownIdsNeitherEndsNorExtendsTheSession()
    {
        // #883: session and user ids are not secret (audit rows carry them); only the replaced token coming back is a replay.
        var (user, _) = await Doctor();
        var tokens = await SignIn(user);
        var before = await SessionOf(user.Id);
        var (forged, _) = RefreshToken.New(before.Id, user.Id); // the right ids, a secret that was never issued

        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(forged)).StatusCode);
        var after = await SessionOf(user.Id);
        Assert.Null(after.RevokedAt);
        Assert.Equal(before.LastSeenAt, after.LastSeenAt);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(tokens.Refresh)).StatusCode); // the phone keeps working
    }

    [Fact]
    public async Task OffboardingRevokesThePhoneOnItsNextRequestAndRefresh()
    {
        var (user, membership) = await Doctor();
        var first = await SignIn(user);
        await Api(first.Access, HttpMethod.Post, "/v1/me/facility", new { membershipId = membership });
        var tokens = await Read(await Refresh(first.Refresh));
        Assert.Equal(HttpStatusCode.OK, (await Api(tokens.Access, HttpMethod.Get, "/v1/me/context")).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<Medvya.Identity.Application.IStaffSessions>().RevokeInTenantAsync(user.Id, tenant);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(tokens.Access, HttpMethod.Get, "/v1/me/context")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(tokens.Refresh)).StatusCode);
    }

    [Fact]
    public async Task OffboardingTheMembershipRevokesThePairedPhoneToo()
    {
        // #225 "paired devices": the offboard endpoint ends sessions per membership; a phone's refresh token is such a session.
        var (user, membership) = await Doctor();
        var first = await SignIn(user);
        await Api(first.Access, HttpMethod.Post, "/v1/me/facility", new { membershipId = membership });
        var tokens = await Read(await Refresh(first.Refresh));

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<Medvya.Identity.Application.IStaffSessions>().RevokeForMembershipAsync(user.Id, tenant, membership);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(tokens.Access, HttpMethod.Get, "/v1/me/context")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(tokens.Refresh)).StatusCode);
    }

    [Fact]
    public async Task APhoneOutlivesTheBrowserIdleTimeoutButNotAWeekUnused()
    {
        var (user, _) = await Doctor();
        var tokens = await SignIn(user);
        var session = await SessionOf(user.Id);
        await using var db = sql.Identity();

        await db.Sessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeenAt, DateTimeOffset.UtcNow - TimeSpan.FromHours(20)));
        tokens = await Read(await Refresh(tokens.Refresh));

        await db.Sessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeenAt, DateTimeOffset.UtcNow - TimeSpan.FromDays(8)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(tokens.Access, HttpMethod.Get, "/v1/me/context")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(tokens.Refresh)).StatusCode);
    }

    [Fact]
    public async Task SigningOutFromTheAppEndsTheSession()
    {
        var (user, _) = await Doctor();
        var tokens = await SignIn(user);
        Assert.Equal(HttpStatusCode.NoContent, (await Api(tokens.Access, HttpMethod.Post, "/bff/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(tokens.Access, HttpMethod.Get, "/v1/me/context")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(tokens.Refresh)).StatusCode);
    }

    [Fact]
    public async Task AGarbledOrMissingTokenIsRefused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api("not-a-token", HttpMethod.Get, "/v1/me/context")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh("x")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Token(("grant_type", "password"))).StatusCode);
    }
}

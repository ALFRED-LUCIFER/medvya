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

    async Task<(StaffUser User, Guid Membership)> Staff(Role role = Role.SoloDoctor)
    {
        var facility = Guid.NewGuid();
        await using (var db = sql.Context(tenant))
        {
            db.Tenants.Add(new Tenant { State = OrganisationState.Approved, Id = tenant, Name = "Iyer Family Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
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
        var membership = new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = id, FacilityId = facility, Role = role, Status = MembershipStatus.Active };
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
        var (user, membership) = await Staff();
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
        var (user, _) = await Staff();
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
        var (user, _) = await Staff();
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
        var (user, membership) = await Staff();
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
        var (user, _) = await Staff();
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
        var (user, membership) = await Staff();
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
        var (user, membership) = await Staff();
        var first = await SignIn(user);
        await Api(first.Access, HttpMethod.Post, "/v1/me/facility", new { membershipId = membership });
        var tokens = await Read(await Refresh(first.Refresh));

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<Medvya.Identity.Application.IStaffSessions>().RevokeForMembershipAsync(user.Id, tenant, membership);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(tokens.Access, HttpMethod.Get, "/v1/me/context")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(tokens.Refresh)).StatusCode);
    }

    // Push tokens (MDV-P5-033 check 1, ADR-011 §8). Synthetic Expo-style tokens.
    const string PushA = "ExponentPushToken[synthetic-aaaa]", PushB = "ExponentPushToken[synthetic-bbbb]";

    async Task<string?> PushTokenOf(Guid userId) => (await SessionOf(userId)).PushToken;

    Task<HttpResponseMessage> RegisterPush(string access, string? token) => Api(access, HttpMethod.Put, "/v1/me/push-token", new { token });

    [Fact]
    public async Task ThePhoneRegistersReplacesAndRemovesItsPushToken()
    {
        var (user, _) = await Staff();
        var tokens = await SignIn(user);

        Assert.Equal(HttpStatusCode.NoContent, (await RegisterPush(tokens.Access, PushA)).StatusCode);
        Assert.Equal(PushA, await PushTokenOf(user.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await RegisterPush(tokens.Access, PushB)).StatusCode); // the OS rotated it
        Assert.Equal(PushB, await PushTokenOf(user.Id));
        foreach (var bad in new[] { null, "", "token <script>", new string('a', StaffSession.PushTokenMaxLength + 1) })
            Assert.Equal(HttpStatusCode.BadRequest, (await RegisterPush(tokens.Access, bad)).StatusCode);
        Assert.Equal(PushB, await PushTokenOf(user.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await Api(tokens.Access, HttpMethod.Delete, "/v1/me/push-token")).StatusCode);
        Assert.Null(await PushTokenOf(user.Id));
    }

    [Fact]
    public async Task OffboardingRemovesThePushTokenWithTheSessionAndOnlyInItsOwnPractice()
    {
        var (user, membership) = await Staff();
        var first = await SignIn(user);
        await Api(first.Access, HttpMethod.Post, "/v1/me/facility", new { membershipId = membership });
        var tokens = await Read(await Refresh(first.Refresh));
        Assert.Equal(HttpStatusCode.NoContent, (await RegisterPush(tokens.Access, PushA)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<Medvya.Identity.Application.IStaffSessions>();
            // Another practice ending this person's sessions there leaves this practice's phone alone.
            await sessions.RevokeInTenantAsync(user.Id, Guid.NewGuid());
            Assert.Equal(PushA, await PushTokenOf(user.Id));
            await sessions.RevokeForMembershipAsync(user.Id, tenant, membership);
        }

        Assert.Null(await PushTokenOf(user.Id)); // a revoked device gets no push
        Assert.Equal(HttpStatusCode.Unauthorized, (await RegisterPush(tokens.Access, PushA)).StatusCode);
    }

    [Fact]
    public async Task SigningOutOrRevokingInMyDevicesRemovesThePushToken()
    {
        var (user, membership) = await Staff();
        var first = await SignIn(user);
        await Api(first.Access, HttpMethod.Post, "/v1/me/facility", new { membershipId = membership });
        var tokens = await Read(await Refresh(first.Refresh));
        await RegisterPush(tokens.Access, PushA);
        Assert.Equal(HttpStatusCode.NoContent, (await Api(tokens.Access, HttpMethod.Post, "/bff/logout")).StatusCode);
        Assert.Null(await PushTokenOf(user.Id));

        var phone = await SignIn(user);
        await Api(phone.Access, HttpMethod.Post, "/v1/me/facility", new { membershipId = membership });
        phone = await Read(await Refresh(phone.Refresh));
        await RegisterPush(phone.Access, PushB);
        var lost = await SessionOf(user.Id);
        var laptop = await SignIn(user); // reports the phone lost from another device
        await Api(laptop.Access, HttpMethod.Post, "/v1/me/facility", new { membershipId = membership });
        laptop = await Read(await Refresh(laptop.Refresh));
        Assert.Equal(HttpStatusCode.NoContent, (await Api(laptop.Access, HttpMethod.Delete, $"/v1/me/sessions/{lost.Id}")).StatusCode);
        await using var db = sql.Identity();
        Assert.Null(await db.Sessions.Where(s => s.Id == lost.Id).Select(s => s.PushToken).SingleAsync());
    }

    [Fact]
    public async Task APhonesTokenFollowsTheLatestSessionAndABrowserHasNone()
    {
        var (user, _) = await Staff();
        var first = await SignIn(user);
        await RegisterPush(first.Access, PushA);
        var second = await SignIn(user); // a new sign-in on the same phone (switch user): the same device token, a new session
        Assert.Equal(HttpStatusCode.NoContent, (await RegisterPush(second.Access, PushA)).StatusCode);
        await using (var db = sql.Identity())
            Assert.Equal(new string?[] { null, PushA }, await db.Sessions.Where(s => s.UserId == user.Id).OrderBy(s => s.CreatedAt).Select(s => s.PushToken).ToListAsync());

        // A browser session is not a device the app can push to.
        using var browser = Browser();
        await StaffSignInTests.SignIn(browser, user.Subject);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/v1/me/push-token", UriKind.Relative)) { Content = JsonContent.Create(new { token = PushB }) };
        request.Headers.Add(StaffAuthentication.CsrfHeader, "1");
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task OneTokenIsNeverOnTwoSessionsEvenWhenClaimedAtOnce()
    {
        const string PushC = "ExponentPushToken[synthetic-cccc]";
        var (user, _) = await Staff();
        var a = await SignIn(user);
        var b = await SignIn(user);
        for (var i = 0; i < 5; i++)
        {
            var claims = await Task.WhenAll(RegisterPush(a.Access, PushC), RegisterPush(b.Access, PushC));
            Assert.All(claims, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
            await using var db = sql.Identity();
            Assert.Equal(1, await db.Sessions.CountAsync(s => s.PushToken == PushC));
        }
        // The database itself refuses a second holder (the unique index), not only the service.
        await using (var db = sql.Identity())
            await Assert.ThrowsAnyAsync<Exception>(() => db.Sessions.Where(s => s.UserId == user.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.PushToken, PushC)));
    }

    [Fact]
    public async Task ATimedOutPhoneKeepsNoPushTokenOnceRevoked()
    {
        var (user, _) = await Staff();
        var tokens = await SignIn(user);
        await RegisterPush(tokens.Access, PushA);
        var session = await SessionOf(user.Id);
        await using (var db = sql.Identity())
            await db.Sessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeenAt, DateTimeOffset.UtcNow - TimeSpan.FromDays(8)));

        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.Null(await scope.ServiceProvider.GetRequiredService<Medvya.Identity.Application.IStaffSessions>().RevokeAsync(user.Id, session.Id));
        Assert.Null(await PushTokenOf(user.Id));
    }

    [Fact]
    public async Task APhoneOutlivesTheBrowserIdleTimeoutButNotAWeekUnused()
    {
        var (user, _) = await Staff();
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
        var (user, _) = await Staff();
        var tokens = await SignIn(user);
        Assert.Equal(HttpStatusCode.NoContent, (await Api(tokens.Access, HttpMethod.Post, "/bff/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(tokens.Access, HttpMethod.Get, "/v1/me/context")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(tokens.Refresh)).StatusCode);
    }

    // The clinical calls behind the staff app's Today, patient record, inbox and Consult tabs (MDV-P5-034). Ids are random: the policy refuses before any lookup.
    static readonly (string Method, string Path, object? Body)[] ClinicalCalls =
    [
        ("GET", "/v1/notes/pending", null),
        ("POST", "/v1/chart/allergy-flags", new { patientIds = new[] { Guid.NewGuid() } }),
        ("GET", $"/v1/patients/{Guid.NewGuid()}/chart", null),
        ("GET", $"/v1/patients/{Guid.NewGuid()}/timeline", null),
        ("GET", $"/v1/intake-escalations?facilityId={Guid.NewGuid()}", null),
        ("POST", "/v1/encounters", new { appointmentId = Guid.NewGuid() }),
        ("GET", $"/v1/encounters/{Guid.NewGuid()}/note", null),
        ("PUT", $"/v1/encounters/{Guid.NewGuid()}/note", new { }),
        ("POST", $"/v1/encounters/{Guid.NewGuid()}/note/sign", new { }),
    ];

    async Task<IReadOnlyList<(string Call, HttpStatusCode Status)>> ClinicalCallsAs(Role role)
    {
        var (user, membership) = await Staff(role);
        var first = await SignIn(user);
        Assert.Equal(HttpStatusCode.NoContent, (await Api(first.Access, HttpMethod.Post, "/v1/me/facility", new { membershipId = membership })).StatusCode);
        var tokens = await Read(await Refresh(first.Refresh));
        var results = new List<(string, HttpStatusCode)>();
        foreach (var (method, path, body) in ClinicalCalls)
            results.Add(($"{method} {path}", (await Api(tokens.Access, new HttpMethod(method), path, body)).StatusCode));
        return results;
    }

    [Fact]
    public async Task FrontDeskOnThePhoneIsRefusedEveryClinicalCall() =>
        // MDV-P5-034 check 2: the app hides the clinical tabs from front desk; the API is what refuses them, through the native bearer session.
        Assert.All(await ClinicalCallsAs(Role.Receptionist), r => Assert.Equal(HttpStatusCode.Forbidden, r.Status));

    [Fact]
    public async Task ADoctorsPhonePassesTheSameClinicalPolicies() =>
        // The control: the 403 above is the role, not the bearer token or the route.
        Assert.All(await ClinicalCallsAs(Role.SoloDoctor), r => Assert.True(r.Status is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized), $"{r.Call}: {r.Status}"));

    [Fact]
    public async Task AGarbledOrMissingTokenIsRefused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api("not-a-token", HttpMethod.Get, "/v1/me/context")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh("x")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Token(("grant_type", "password"))).StatusCode);
    }
}

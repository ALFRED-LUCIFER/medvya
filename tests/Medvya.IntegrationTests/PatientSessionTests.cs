using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Medvya.Audit.Domain;
using Medvya.Identity.Api;
using Medvya.Identity.Domain;
using Medvya.Notifications.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P5-033, ADR-020: the patient session after a right SMS code. Verify answers a token pair, /v1/patient/me proves it, refresh rotates,
/// a replayed refresh token ends the session (audited), logout ends it, and a patient token opens no staff or clinical endpoint.
/// </summary>
public sealed partial class PatientSessionTests : IClassFixture<PostgresFixture>, IDisposable
{
    readonly PostgresFixture sql;
    readonly FakeSms sms = new();
    readonly WebApplicationFactory<Program> factory;
    readonly string number = "90000" + Random.Shared.Next(10_000, 100_000);

    public sealed class FakeSms : ISmsGateway
    {
        public List<SmsMessage> Sent { get; } = [];
        public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
        {
            lock (Sent) Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    public PatientSessionTests(PostgresFixture sql)
    {
        this.sql = sql;
        // Through CreateFactory's hook, not WithWebHostBuilder: a derived factory's staff sign-in would call the parent's OIDC back channel.
        factory = StaffSignInTests.CreateFactory(sql.ConnectionString, b => WithSms(b, sms));
    }

    /// <summary>A host whose SMS codes land in <paramref name="sms"/>; also used by the authorisation matrix.</summary>
    internal static void WithSms(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder, FakeSms sms) => builder
        .UseSetting("RateLimiting:SignInPerMinute", "1000")
        .UseSetting("Notifications:Sms:DefaultSender", "MEDVYA")
        .UseSetting("Notifications:Sms:DltTemplateIds:SignInCode.en", "1107000000000000009")
        .ConfigureTestServices(s => s.AddSingleton<ISmsGateway>(sms));

    public void Dispose() => factory.Dispose();

    HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    static Uri U(string path) => new(path, UriKind.Relative);

    [GeneratedRegex(@"\d{6}")] private static partial Regex CodeIn();

    internal sealed record Tokens(string Access, string Refresh);

    /// <summary>Code → verify → the first token pair, through HTTP only.</summary>
    internal static async Task<Tokens> SignInAsync(HttpClient client, FakeSms sms, string mobile, string platform = "android", string appVersion = "1.4.0")
    {
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(U("/v1/patient-sign-in/code"), new { countryCode = "IN", mobile, language = "en" })).StatusCode);
        string code;
        lock (sms.Sent) code = CodeIn().Match(sms.Sent.Last(m => m.To == "+91" + mobile).Text).Value;
        return await Read(await client.PostAsJsonAsync(U("/v1/patient-sign-in/verify"), new { countryCode = "IN", mobile, code, platform, appVersion }));
    }

    static async Task<Tokens> Read(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
        Assert.Equal(900, body.GetProperty("expires_in").GetInt32());
        return new(body.GetProperty("access_token").GetString()!, body.GetProperty("refresh_token").GetString()!);
    }

    Task<HttpResponseMessage> Refresh(string refresh) => Client().PostAsJsonAsync(U("/v1/patient-sign-in/refresh"), new { refreshToken = refresh });

    async Task<HttpResponseMessage> Api(string access, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, U(path)) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return await Client().SendAsync(request);
    }

    async Task<PatientSession> Session(Guid id)
    {
        await using var db = sql.Identity();
        return await db.PatientSessions.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    async Task<bool> Audited(string action, Guid sessionId)
    {
        await using var audit = sql.Audit(PlatformTenant.Id);
        return await audit.Events.AnyAsync(e => e.Action == action && e.ResourceType == "patient_session" && e.ResourceId == sessionId && e.ActorUserId == null);
    }

    [Fact]
    public async Task AVerifiedNumberGetsASessionThatRefreshesRotatesAndEndsOnReplay()
    {
        var first = await SignInAsync(Client(), sms, number);

        var me = await Api(first.Access, HttpMethod.Get, "/v1/patient/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await me.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = body.GetProperty("sessionId").GetGuid();
        // Only what the session holds: the device registration, never the number, a name, a clinic or a record.
        Assert.Equal("Medvya app 1.4.0 on Android", body.GetProperty("device").GetString());
        Assert.Equal(["sessionId", "device", "signedInAt"], body.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain(number, await me.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var row = await Session(sessionId);
        Assert.Equal(SmsSignInRules.HashPhone("+91" + number), row.PhoneHash);
        Assert.True(await Audited("patient.signin", sessionId));

        // Rotation: the new pair works and the old refresh token is spent.
        var second = await Read(await Refresh(first.Refresh));
        Assert.NotEqual(first.Refresh, second.Refresh);
        Assert.Equal(HttpStatusCode.OK, (await Api(second.Access, HttpMethod.Get, "/v1/patient/me")).StatusCode);

        // Replay: the spent token comes back, so someone holds a copy. The whole session ends and the event is audited.
        var replay = await Refresh(first.Refresh);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("invalid_grant", (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.NotNull((await Session(sessionId)).RevokedAt);
        Assert.True(await Audited("session.refresh.replay", sessionId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(second.Access, HttpMethod.Get, "/v1/patient/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(second.Refresh)).StatusCode);
    }

    [Fact]
    public async Task LogoutEndsTheSessionAtOnce()
    {
        var tokens = await SignInAsync(Client(), sms, number, platform: "ios", appVersion: "2.0.1");
        var sessionId = (await (await Api(tokens.Access, HttpMethod.Get, "/v1/patient/me")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetGuid();
        Assert.Equal("Medvya app 2.0.1 on iOS", (await Session(sessionId)).Device);

        Assert.Equal(HttpStatusCode.NoContent, (await Api(tokens.Access, HttpMethod.Post, "/v1/patient/logout")).StatusCode);
        Assert.True(await Audited("patient.signout", sessionId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(tokens.Access, HttpMethod.Get, "/v1/patient/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(tokens.Refresh)).StatusCode);
    }

    [Fact]
    public async Task AForgedOrStaffShapedRefreshTokenIsRefusedAndChangesNothing()
    {
        var tokens = await SignInAsync(Client(), sms, number);
        var sessionId = (await (await Api(tokens.Access, HttpMethod.Get, "/v1/patient/me")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetGuid();
        var (forged, _) = Medvya.Identity.Application.RefreshToken.New(sessionId, Guid.Empty); // the right id, a secret that was never issued
        var (staffShaped, _) = Medvya.Identity.Application.RefreshToken.New(sessionId, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(forged)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(staffShaped)).StatusCode);
        Assert.Null((await Session(sessionId)).RevokedAt);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(tokens.Refresh)).StatusCode);
    }

    /// <summary>
    /// A patient token is not a staff credential: on a sample of staff, session and clinical endpoints it is refused as no caller at all (401),
    /// and a patient refresh token gets nothing from the staff token endpoint. The full list of id-taking endpoints is in the authorisation matrix.
    /// </summary>
    [Theory]
    [InlineData("GET", "/v1/me/context")]
    [InlineData("GET", "/v1/me/sessions")]
    [InlineData("POST", "/v1/me/facility")]
    [InlineData("PUT", "/v1/me/push-token")]
    [InlineData("POST", "/bff/logout")]
    [InlineData("POST", "/v1/patients/search")]
    [InlineData("GET", "/v1/patients/{id}")]
    [InlineData("GET", "/v1/patients/{id}/chart")]
    [InlineData("GET", "/v1/encounters/{id}")]
    [InlineData("GET", "/v1/front-desk/board")]
    [InlineData("GET", "/v1/staff/")]
    [InlineData("GET", "/v1/onboarding/")]
    public async Task APatientTokenOpensNoStaffOrClinicalEndpoint(string method, string path)
    {
        var tokens = await SignInAsync(Client(), sms, number);
        var response = await Api(tokens.Access, new HttpMethod(method), path.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal), method == "GET" ? null : new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var staffToken = await Client().PostAsync(U("/native/token"), new FormUrlEncodedContent([KeyValuePair.Create("grant_type", "refresh_token"), KeyValuePair.Create("refresh_token", tokens.Refresh)]));
        Assert.Equal(HttpStatusCode.BadRequest, staffToken.StatusCode);
    }

    /// <summary>The other way round: a live staff session, as a native access token or as the web cookie, is no caller at all on a patient endpoint.</summary>
    [Fact]
    public async Task AStaffTokenOrCookieOpensNoPatientEndpoint()
    {
        var id = Guid.NewGuid();
        var subject = id.ToString();
        await using (var db = sql.Identity())
        {
            db.Users.Add(new StaffUser { Id = id, Issuer = IdentityProviders.Development, Subject = subject, DisplayName = "Dr. Arjun Rao (synthetic)" });
            await db.SaveChangesAsync();
        }

        var (verifier, challenge) = NativeSignInTests.Pkce();
        using var phone = Client();
        var code = await NativeSignInTests.BrowserSignIn(phone, subject, challenge);
        using var exchange = await Client().PostAsync(U("/native/token"), new FormUrlEncodedContent([KeyValuePair.Create("grant_type", "authorization_code"),
            KeyValuePair.Create("code", code), KeyValuePair.Create("code_verifier", verifier), KeyValuePair.Create("redirect_uri", NativeSignInTests.Redirect)]));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var staffAccess = (await exchange.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await Api(staffAccess, HttpMethod.Get, "/v1/me/sessions")).StatusCode); // the token is live
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(staffAccess, HttpMethod.Get, "/v1/patient/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(staffAccess, HttpMethod.Post, "/v1/patient/logout")).StatusCode);

        using var browser = Client();
        await StaffSignInTests.SignIn(browser, subject);
        Task<HttpResponseMessage> WithCookie(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, U(path));
            request.Headers.Add(Host.Api.Auth.StaffAuthentication.CsrfHeader, "1"); // so a refusal is the policy's, not the CSRF check's
            return browser.SendAsync(request);
        }
        Assert.Equal(HttpStatusCode.OK, (await WithCookie(HttpMethod.Get, "/v1/me/sessions")).StatusCode); // the cookie is live
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithCookie(HttpMethod.Get, "/v1/patient/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithCookie(HttpMethod.Post, "/v1/patient/logout")).StatusCode);
    }

    [Fact]
    public void PatientEndpointsAcceptOnlyThePatientSchemeAndTheRefreshIsPublicAndLimited()
    {
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToList();
        var patient = endpoints.Where(e => e.RoutePattern.RawText?.StartsWith("/v1/patient/", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(2, patient.Count);
        Assert.All(patient, e => Assert.Equal(PatientAuthentication.Policy, Assert.Single(e.Metadata.GetOrderedMetadata<IAuthorizeData>()).Policy));
        // No other endpoint names the patient policy or scheme.
        Assert.All(endpoints.Except(patient), e => Assert.DoesNotContain(e.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            a => a.Policy == PatientAuthentication.Policy || a.AuthenticationSchemes?.Contains(PatientAuthentication.Scheme, StringComparison.Ordinal) == true));
        var refresh = Assert.Single(endpoints, e => e.RoutePattern.RawText == "/v1/patient-sign-in/refresh");
        Assert.NotNull(refresh.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Equal(InvitationEndpoints.SignInRateLimit, refresh.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
    }
}

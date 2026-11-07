using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Medvya.Identity.Application;
using Medvya.Tenancy.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Host.Api.Auth;

/// <summary>
/// Sign-in for the native apps (MDV-P5-033). The app opens the system browser at <c>/native/login</c> with a PKCE challenge;
/// the normal staff OIDC sign-in runs (MFA, Medvya accounts only, invitations), then <c>/native/complete</c> turns that browser
/// session into a native one and hands the app a one-time code through its redirect URI. <c>/native/token</c> exchanges the code
/// (with the PKCE verifier) for a 15-minute access token and a refresh token. Both are backed by the same server-side session row
/// as the web cookie, so revocation, "My devices" and offboarding apply unchanged. A refresh token works once: presenting an old
/// one again ends the session.
/// </summary>
public static partial class NativeAuthentication
{
    public const string Scheme = "Native";
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(2);
    const string ChallengeItem = "medvya.native.challenge", RedirectItem = "medvya.native.redirect";
    const string AccessPurpose = "Medvya.Native.Access", CodePurpose = "Medvya.Native.Code";

    public static WebApplicationBuilder AddNativeAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, BearerHandler>(Scheme, null);
        return builder;
    }

    /// <summary>Cookie requests that carry a bearer token are handed to the native scheme (StaffAuthentication's ForwardDefaultSelector).</summary>
    public static string? SelectScheme(HttpContext ctx) =>
        ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal) ? Scheme : null;

    public static bool IsNative(ClaimsPrincipal principal) => principal.Identity?.AuthenticationType == Scheme;

    static TicketDataFormat Format(IServiceProvider services, string purpose) =>
        new(services.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose));

    sealed class BearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return AuthenticateResult.NoResult();
            var ticket = Format(Context.RequestServices, AccessPurpose).Unprotect(header["Bearer ".Length..]);
            if (ticket?.Properties.ExpiresUtc is not { } expires || expires <= TimeProvider.GetUtcNow()) return AuthenticateResult.Fail("Invalid or expired token.");
            // The session row decides, as for the cookie: revoked, idle-expired or switched sessions are refused on this request.
            return await SessionControl.IsLiveAsync(ticket.Principal, Context.RequestServices, activity: !Request.Headers.ContainsKey(SessionControl.PassiveHeader), Context.RequestAborted)
                ? AuthenticateResult.Success(ticket) : AuthenticateResult.Fail("Session ended.");
        }
    }

    public static WebApplication MapNativeSignIn(this WebApplication app)
    {
        // Step 1, in the system browser: the app's PKCE challenge and redirect URI ride in the protected OIDC state.
        app.MapGet("/native/login", (string? redirect_uri, string? code_challenge, string? code_challenge_method, IConfiguration config) =>
            {
                if (!AllowedRedirect(config, redirect_uri) || code_challenge_method != "S256" || !IsChallenge(code_challenge))
                    return Results.BadRequest();
                // Always ask the provider again: a browser that is still signed in must not hand a phone a session silently.
                var properties = new OpenIdConnectChallengeProperties { RedirectUri = "/native/complete", Prompt = "login" };
                properties.Items[ChallengeItem] = code_challenge;
                properties.Items[RedirectItem] = redirect_uri;
                return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
            })
            .AllowAnonymous()
            .RequireRateLimiting(StaffAuthentication.SignInRateLimit);

        // Step 2, after the provider: the browser session becomes native and the browser forgets it (the cookie is cleared).
        app.MapGet("/native/complete", async (HttpContext http, IConfiguration config) =>
            {
                var auth = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                // Only a session started by /native/login is converted (and cleared); a plain web session is left alone.
                if (auth.Principal is not { } principal || auth.Properties is null
                    || !auth.Properties.Items.TryGetValue(ChallengeItem, out var challenge) || challenge is null)
                    return Results.BadRequest();
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                if (!auth.Properties.Items.TryGetValue(RedirectItem, out var redirect) || !AllowedRedirect(config, redirect)
                    || Read(principal, SessionControl.SessionIdClaim) is not { } sessionId || Read(principal, ClaimTypes.NameIdentifier) is not { } userId)
                    return Results.BadRequest();
                var secret = RandomNumberGenerator.GetBytes(32);
                if (!await http.RequestServices.GetRequiredService<IStaffSessions>().RotateRefreshAsync(sessionId, null, SHA256.HashData(secret), http.RequestAborted))
                    return Results.BadRequest();
                var code = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow + CodeLifetime };
                code.Items["sid"] = sessionId.ToString();
                code.Items["uid"] = userId.ToString();
                code.Items["secret"] = Base64Url.EncodeToString(secret);
                code.Items[ChallengeItem] = challenge;
                code.Items[RedirectItem] = redirect;
                var protectedCode = Format(http.RequestServices, CodePurpose).Protect(new AuthenticationTicket(new ClaimsPrincipal(), code, CodePurpose));
                return Results.Redirect($"{redirect}?code={Uri.EscapeDataString(protectedCode)}");
            })
            .AllowAnonymous(); // reads the cookie the provider callback just set; anything else is a 400

        // Step 3, from the app: code + verifier, or a refresh token, for a new token pair. OAuth form fields; no cookie, so no CSRF.
        app.MapPost("/native/token", async (HttpContext http) =>
            {
                if (!http.Request.HasFormContentType) return Results.BadRequest();
                var form = await http.Request.ReadFormAsync(http.RequestAborted);
                return form["grant_type"].ToString() switch
                {
                    "authorization_code" => await ExchangeCodeAsync(http, form["code"].ToString(), form["code_verifier"].ToString(), form["redirect_uri"].ToString()),
                    "refresh_token" => await RefreshAsync(http, form["refresh_token"].ToString()),
                    _ => Results.BadRequest(),
                };
            })
            .AllowAnonymous()
            .RequireRateLimiting(StaffAuthentication.SignInRateLimit);
        return app;
    }

    static async Task<IResult> ExchangeCodeAsync(HttpContext http, string code, string verifier, string redirectUri)
    {
        var ticket = code.Length is > 0 and <= 2048 ? Format(http.RequestServices, CodePurpose).Unprotect(code) : null;
        string? Item(string key) => ticket?.Properties.Items.TryGetValue(key, out var value) == true ? value : null;
        if (ticket?.Properties.ExpiresUtc is not { } expires || expires <= DateTimeOffset.UtcNow
            || Item(RedirectItem) != redirectUri
            || verifier.Length is < 43 or > 128
            || Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) != Item(ChallengeItem)
            || !Guid.TryParse(Item("sid"), out var sessionId) || !Guid.TryParse(Item("uid"), out var userId) || Item("secret") is not { } secret)
            return InvalidGrant();
        return await IssueAsync(http, sessionId, userId, SHA256.HashData(Base64Url.DecodeFromChars(secret)));
    }

    static async Task<IResult> RefreshAsync(HttpContext http, string refreshToken) =>
        RefreshToken.Read(refreshToken) is { } token ? await IssueAsync(http, token.SessionId, token.UserId, token.Hash) : InvalidGrant();

    /// <summary>Rotates the session's secret from <paramref name="presented"/> to a new refresh token and issues a token pair.</summary>
    static async Task<IResult> IssueAsync(HttpContext http, Guid sessionId, Guid userId, byte[] presented)
    {
        var sessions = http.RequestServices.GetRequiredService<IStaffSessions>();
        var session = await sessions.ValidateAsync(sessionId, userId, activity: true, http.RequestAborted);
        if (session is null || !session.IsNative) return InvalidGrant();
        // From here the request runs as that user (and tenant), so membership lookups and the audit row are theirs.
        var principal = await PrincipalAsync(http, session.Id, userId, session.TenantId, session.MembershipId);
        if (principal is null) return InvalidGrant();
        var (refresh, next) = RefreshToken.New(sessionId, userId);
        if (!await sessions.RotateRefreshAsync(sessionId, presented, next, http.RequestAborted))
        {
            // An old token (or a used code) came back: someone holds a copy. End the whole session (RFC 9700 §4.14.2) first, so a
            // failing audit write cannot leave it alive.
            await sessions.RevokeAsync(userId, sessionId, http.RequestAborted);
            if (session.TenantId is not null) await SessionControl.WriteAuditAsync(http, "session.refresh.replay", "session", sessionId);
            RefreshReplayed(http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(NativeAuthentication)));
            return InvalidGrant();
        }
        var access = Format(http.RequestServices, AccessPurpose).Protect(new AuthenticationTicket(principal,
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow + AccessLifetime }, Scheme));
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { access_token = access, token_type = "Bearer", expires_in = (int)AccessLifetime.TotalSeconds, refresh_token = refresh });
    }

    /// <summary>The same claims a cookie carries. The facility comes from the live membership; one that is no longer active ends the grant.</summary>
    static async Task<ClaimsPrincipal?> PrincipalAsync(HttpContext http, Guid sessionId, Guid userId, Guid? tenantId, Guid? membershipId)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim("amr", "mfa"), new Claim(SessionControl.SessionIdClaim, sessionId.ToString())], Scheme);
        http.User = new ClaimsPrincipal(identity);
        if (membershipId is null) return http.User;
        var membership = (await http.RequestServices.GetRequiredService<IMembershipDirectory>().ForCurrentUserAsync(http.RequestAborted)).FirstOrDefault(m => m.Id == membershipId);
        if (membership is null || membership.TenantId != tenantId) return null;
        identity.AddClaims([new Claim(SessionControl.TenantClaim, membership.TenantId.ToString()), new Claim(SessionControl.MembershipClaim, membership.Id.ToString()),
            new Claim(SessionControl.FacilityClaim, membership.FacilityId.ToString())]);
        http.User = new ClaimsPrincipal(identity);
        return http.User;
    }

    static IResult InvalidGrant() => Results.BadRequest(new { error = "invalid_grant" });

    // shortcut: custom-scheme redirect URIs can be claimed by another app on the phone (PKCE keeps the code useless to it); move to
    // verified https links (iOS Universal Links, Android App Links) before the store release (MDV-P5-039).
    static bool AllowedRedirect(IConfiguration config, string? uri) =>
        uri is not null && (config.GetSection("Native:RedirectUris").Get<string[]>() ?? []).Contains(uri, StringComparer.Ordinal);

    // RFC 7636: S256 challenge = base64url(SHA-256(verifier)) without padding, always 43 characters.
    static bool IsChallenge(string? challenge) => challenge is { Length: 43 } && Base64Url.IsValid(challenge, out var length) && length == 32;

    static Guid? Read(ClaimsPrincipal principal, string claim) => Guid.TryParse(principal.FindFirstValue(claim), out var id) ? id : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Native refresh token or sign-in code reused; the session was ended")]
    static partial void RefreshReplayed(ILogger logger);
}

/// <summary>
/// Refresh token = base64url(session id ‖ user id ‖ 32 random bytes). The ids say which session a replayed token belonged to,
/// so it can be ended; they grant nothing alone. Only the SHA-256 of the whole token is stored, so it survives restarts and key loss.
/// </summary>
public static class RefreshToken
{
    const int IdBytes = 16, RandomBytes = 32, Length = IdBytes * 2 + RandomBytes;

    public static (string Token, byte[] Hash) New(Guid sessionId, Guid userId)
    {
        var bytes = new byte[Length];
        sessionId.TryWriteBytes(bytes);
        userId.TryWriteBytes(bytes.AsSpan(IdBytes));
        RandomNumberGenerator.Fill(bytes.AsSpan(IdBytes * 2));
        return (Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
    }

    public static (Guid SessionId, Guid UserId, byte[] Hash)? Read(string? token)
    {
        if (token is null || token.Length > 128 || !Base64Url.IsValid(token, out var length) || length != Length) return null;
        Span<byte> bytes = stackalloc byte[Length];
        if (!Base64Url.TryDecodeFromChars(token, bytes, out var written) || written != Length) return null;
        return (new Guid(bytes[..IdBytes]), new Guid(bytes[IdBytes..(IdBytes * 2)]), SHA256.HashData(bytes));
    }
}

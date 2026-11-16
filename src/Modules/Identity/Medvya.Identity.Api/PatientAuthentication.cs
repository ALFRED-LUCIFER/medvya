using System.Security.Claims;
using System.Text.Encodings.Web;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Identity.Application;
using Medvya.Identity.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Medvya.Identity.Api;

/// <summary>
/// The patient app's session after a right SMS code (MDV-P5-033, ADR-020). A verified number gets the staff app's token pair (15-minute access
/// token, rotating refresh token, a replay ends the session), but on its own scheme, token purpose and policy: staff endpoints never accept a
/// patient token (it is not even readable as a staff token), and patient endpoints accept nothing else. The principal holds the session id only:
/// no user, tenant, facility or patient record, so it reaches no clinic or patient data.
/// </summary>
public static partial class PatientAuthentication
{
    public const string Scheme = "Patient";
    /// <summary>Patient endpoints: the patient bearer scheme only.</summary>
    public const string Policy = "patient";
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    const string AccessPurpose = "Medvya.Patient.Access";
    /// <summary>Not "sid": nothing on the staff side reads this claim.</summary>
    const string SessionClaim = "patient_sid";

    public sealed record RefreshRequest(string? RefreshToken);

    public static AuthenticationBuilder AddPatientBearer(this AuthenticationBuilder builder) =>
        builder.AddScheme<AuthenticationSchemeOptions, BearerHandler>(Scheme, null);

    public static AuthorizationBuilder AddPatientPolicy(this AuthorizationBuilder builder) =>
        builder.AddPolicy(Policy, p => p.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser().RequireClaim(SessionClaim));

    static TicketDataFormat Format(IServiceProvider services) =>
        new(services.GetRequiredService<IDataProtectionProvider>().CreateProtector(AccessPurpose));

    static Guid? SessionId(ClaimsPrincipal principal) => Guid.TryParse(principal.FindFirstValue(SessionClaim), out var id) ? id : null;

    sealed class BearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return AuthenticateResult.NoResult();
            var ticket = Format(Context.RequestServices).Unprotect(header["Bearer ".Length..]);
            if (ticket?.Properties.ExpiresUtc is not { } expires || expires <= TimeProvider.GetUtcNow() || SessionId(ticket.Principal) is not { } sessionId)
                return AuthenticateResult.Fail("Invalid or expired token.");
            // The session row decides on every request, so a sign-out or a replay is refused at once.
            return await Context.RequestServices.GetRequiredService<IPatientSessions>().ValidateAsync(sessionId, activity: true, Context.RequestAborted) is null
                ? AuthenticateResult.Fail("Session ended.") : AuthenticateResult.Success(ticket);
        }
    }

    /// <summary>After a right code (<see cref="PatientSignInEndpoints"/>): a new session and its first token pair. Audited first, fail closed.</summary>
    internal static async Task<IResult> StartAsync(HttpContext http, string mobile, string? platform, string? appVersion)
    {
        // shortcut: no device key is registered and a refresh carries no proof of possession, because neither app has an asymmetric-key
        // library (expo-crypto has digests and random bytes only); a copied refresh token works off the phone until the real app's next refresh
        // ends the session as a replay. Add a hardware-backed key (Secure Enclave, Android Keystore) and signed refreshes, for staff and
        // patients, before the store release (MDV-P5-039).
        var sessionId = Guid.CreateVersion7();
        var (refresh, hash) = RefreshToken.New(sessionId, Guid.Empty); // a patient has no user id
        await AuditAsync(http, "patient.signin", sessionId);
        await http.RequestServices.GetRequiredService<IPatientSessions>().StartAsync(sessionId, SmsSignInRules.HashPhone(mobile),
            StaffSession.AppLabel(platform, appVersion), hash, http.RequestAborted);
        return Tokens(http, sessionId, refresh);
    }

    public static WebApplication MapPatientSession(this WebApplication app)
    {
        // Public like /native/token (the refresh token is the credential), in the body only, and limited per client as sign-in is.
        app.MapPost("/v1/patient-sign-in/refresh", async (RefreshRequest body, HttpContext http) =>
            {
                if (RefreshToken.Read(body.RefreshToken) is not { } token || token.UserId != Guid.Empty) return InvalidGrant();
                var sessions = http.RequestServices.GetRequiredService<IPatientSessions>();
                var (refresh, next) = RefreshToken.New(token.SessionId, Guid.Empty);
                switch (await sessions.RotateRefreshAsync(token.SessionId, token.Hash, next, http.RequestAborted))
                {
                    case RefreshRotation.Rotated:
                        return Tokens(http, token.SessionId, refresh);
                    case RefreshRotation.Replayed:
                        // An old token came back: someone holds a copy. End the session first, so a failing audit write cannot leave it alive (RFC 9700).
                        await sessions.RevokeAsync(token.SessionId, http.RequestAborted);
                        await AuditAsync(http, "session.refresh.replay", token.SessionId);
                        RefreshReplayed(http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PatientAuthentication)));
                        return InvalidGrant();
                    default:
                        return InvalidGrant();
                }
            })
            .AllowAnonymous()
            .RequireRateLimiting(InvitationEndpoints.SignInRateLimit);

        var patient = app.MapGroup("/v1/patient").RequireAuthorization(Policy);

        // Proves the app is signed in. Only what the session holds: no number, no name, no clinic, no record.
        patient.MapGet("/me", async (ClaimsPrincipal principal, [FromServices] IPatientSessions sessions, CancellationToken ct) =>
            await sessions.ValidateAsync(SessionId(principal)!.Value, activity: false, ct) is { } session
                ? Results.Ok(new { sessionId = session.Id, device = session.Device, signedInAt = session.CreatedAt })
                : Results.Unauthorized());

        patient.MapPost("/logout", async (HttpContext http, [FromServices] IPatientSessions sessions) =>
        {
            var sessionId = SessionId(http.User)!.Value;
            await AuditAsync(http, "patient.signout", sessionId);
            await sessions.RevokeAsync(sessionId, http.RequestAborted);
            return Results.NoContent();
        });
        return app;
    }

    static IResult Tokens(HttpContext http, Guid sessionId, string refresh)
    {
        var clock = http.RequestServices.GetRequiredService<TimeProvider>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SessionClaim, sessionId.ToString())], Scheme));
        var access = Format(http.RequestServices).Protect(new AuthenticationTicket(principal,
            new AuthenticationProperties { ExpiresUtc = clock.GetUtcNow() + AccessLifetime }, Scheme));
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { access_token = access, token_type = "Bearer", expires_in = (int)AccessLifetime.TotalSeconds, refresh_token = refresh });
    }

    static IResult InvalidGrant() => Results.BadRequest(new { error = "invalid_grant" });

    /// <summary>A patient session has no clinic, so its events go in the platform chain, with no staff actor; the session id is the resource.</summary>
    static Task AuditAsync(HttpContext http, string action, Guid sessionId) =>
        http.RequestServices.GetRequiredService<IPlatformAuditLog>().WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = PlatformTenant.Id, Action = action, ResourceType = "patient_session", ResourceId = sessionId,
            Purpose = AuditPurpose.Support, Outcome = AuditOutcome.Allowed, OccurredAt = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
            ClientIp = http.Connection.RemoteIpAddress?.ToString(), CorrelationId = http.TraceIdentifier,
        }, http.RequestAborted);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Patient refresh token reused; the session was ended")]
    static partial void RefreshReplayed(ILogger logger);
}

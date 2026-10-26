using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Host.Api.Context;
using Medvya.Scheduling.Api;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Host.Api.Auth;

/// <summary>
/// The waiting-room TV's sign-in (MDV-P1-031; design in <see cref="QueueDisplayAuth"/>). The display token carries its tenant,
/// so it is checked inside that tenant only (row-level security applies); an unknown, altered or revoked token is refused.
/// </summary>
public static class QueueDisplayAuthentication
{
    /// <summary>Per client address: pairing is cheap to try, and the token space makes guessing pointless anyway.</summary>
    public const string PairingRateLimit = "display-pairing";

    public sealed record PairDisplay(string? Token);

    public static AuthenticationBuilder AddQueueDisplay(this AuthenticationBuilder builder) =>
        builder.AddScheme<AuthenticationSchemeOptions, Handler>(QueueDisplayAuth.Scheme, null);

    /// <summary>Display endpoints accept a display principal and nothing else (not a staff session).</summary>
    public static AuthorizationBuilder AddQueueDisplayPolicy(this AuthorizationBuilder builder) =>
        builder.AddPolicy(QueueDisplayAuth.Policy, p => p.AddAuthenticationSchemes(QueueDisplayAuth.Scheme).RequireAuthenticatedUser()
            .RequireClaim(QueueDisplayAuth.DisplayClaim));

    public static RateLimiterOptions AddQueueDisplayPairingLimit(this RateLimiterOptions options, int permitsPerMinute) =>
        options.AddPolicy(PairingRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitsPerMinute, Window = TimeSpan.FromMinutes(1) }));

    /// <summary>
    /// POST /v1/queue/tv/session: the TV sends the token from its link (in the body, never a URL) and gets the display cookie.
    /// The pairing is audited in the display's tenant.
    /// </summary>
    public static WebApplication MapQueueDisplaySession(this WebApplication app)
    {
        app.MapPost("/v1/queue/tv/session", async (PairDisplay body, HttpContext http, CancellationToken ct) =>
            {
                if (await FindAsync(http.RequestServices, body.Token, pair: true, ct) is null)
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Display not recognised");
                http.Response.Cookies.Append(QueueDisplayAuth.Cookie, body.Token!, new CookieOptions
                {
                    HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = QueueDisplayAuth.CookiePath, IsEssential = true,
                    MaxAge = TimeSpan.FromDays(400), // the browser maximum; the server checks the display on every request
                });
                return Results.NoContent();
            })
            .AllowAnonymous() // public: the display token is the credential
            .RequireRateLimiting(PairingRateLimit);
        return app;
    }

    static async Task<(Guid TenantId, DisplaySession Display)?> FindAsync(IServiceProvider services, string? token, bool pair, CancellationToken ct)
    {
        if (InvitationToken.Read(token) is not { } read) return null;
        var (tenantId, hash) = read;
        await using var scope = services.EnterLinkTenant(tenantId);
        var queue = scope.ServiceProvider.GetRequiredService<IQueueService>();
        return await (pair ? queue.PairDisplayAsync(hash, ct) : queue.FindDisplayAsync(hash, ct)) is { } display ? (tenantId, display) : null;
    }

    sealed class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Cookies.TryGetValue(QueueDisplayAuth.Cookie, out var token)) return AuthenticateResult.NoResult();
            if (await FindAsync(Context.RequestServices, token, pair: false, Context.RequestAborted) is not { } found)
                return AuthenticateResult.Fail("Unknown or revoked display.");
            var (tenantId, display) = found;
            var identity = new ClaimsIdentity(
                [new Claim(QueueDisplayAuth.TenantClaim, tenantId.ToString()), new Claim(QueueDisplayAuth.FacilityClaim, display.FacilityId.ToString()),
                 new Claim(QueueDisplayAuth.DisplayClaim, display.DisplayId.ToString())], Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
    }
}

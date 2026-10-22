using System.Security.Claims;
using Medvya.Identity.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Host.Api.Auth;

/// <summary>
/// Backend-for-frontend sign-in (ADR-004, MDV-P0-011): OIDC authorization code + PKCE runs here, tokens never reach the
/// browser, and the browser holds only an HTTP-only, Secure, SameSite=Strict session cookie.
/// </summary>
public static partial class StaffAuthentication
{
    public const string CookieName = "__Host-medvya";
    /// <summary>State-changing requests must carry this header. Browsers cannot add it cross-site without a CORS preflight, which this API never grants.</summary>
    public const string CsrfHeader = "X-Medvya-Csrf";

    public static WebApplicationBuilder AddStaffAuthentication(this WebApplicationBuilder builder)
    {
        var auth = builder.Services.AddAuthentication(o =>
        {
            o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        }).AddCookie(o =>
        {
            o.Cookie.Name = CookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            // shortcut: fixed 8 h session; per-tenant idle timeout and device revocation arrive with MDV-P0-012.
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.SlidingExpiration = false;
            // An API answers 401/403; the SPA decides where to send the user.
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });

        // No provider configured = nobody can sign in (fail closed); /bff/login then errors instead of letting anyone in.
        if (builder.Configuration.GetSection("Authentication:Oidc") is { } oidc && oidc["Authority"] is { } authority)
        {
            auth.AddOpenIdConnect(o =>
            {
                o.Authority = authority;
                o.ClientId = oidc["ClientId"];
                o.ClientSecret = oidc["ClientSecret"];
                o.ResponseType = OpenIdConnectResponseType.Code;
                o.ResponseMode = OpenIdConnectResponseMode.Query;
                o.UsePkce = true;
                o.SaveTokens = false; // nothing downstream needs them, so they are not kept anywhere
                o.MapInboundClaims = false;
                // Only the local dev provider may use plain http, and it exists only in Development.
                o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                o.Events.OnTokenValidated = OnTokenValidated;
                // A refused or broken sign-in returns to the app with a generic error; the reason is only in the logs.
                o.Events.OnRemoteFailure = ctx =>
                {
                    ctx.Response.Redirect("/?signin=failed");
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }
        return builder;
    }

    // Accept only accounts that exist in Medvya and signed in with MFA; replace the provider's claims with ours.
    static async Task OnTokenValidated(TokenValidatedContext ctx)
    {
        var principal = ctx.Principal!;
        var log = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StaffAuthentication));
        // shortcut: MFA is read from the standard amr claim; confirm Entra External ID emits it (or use its MFA claim) in the Entra spike (MDV-P0-011).
        if (!principal.FindAll("amr").Any(c => c.Value == "mfa"))
        {
            SignInRefused(log, "mfa_missing");
            ctx.Fail("Multi-factor authentication is required.");
            return;
        }
        var issuer = principal.FindFirst("iss")?.Value;
        var subject = principal.FindFirst("sub")?.Value;
        var user = issuer is null || subject is null ? null
            : await ctx.HttpContext.RequestServices.GetRequiredService<IStaffUsers>().FindAsync(issuer, subject, ctx.HttpContext.RequestAborted);
        // Joining by invitation (MDV-P0-013): the token rode in the protected OIDC state.
        if (issuer is not null && subject is not null && ctx.Properties?.Items.TryGetValue(InvitationSignIn.Item, out var invitation) == true && invitation is not null)
        {
            user = await InvitationSignIn.AcceptAsync(ctx.HttpContext, invitation, issuer, subject, principal.FindFirst("name")?.Value, user);
            if (user is null)
            {
                SignInRefused(log, "invitation_unusable");
                ctx.Fail("The invitation cannot be used.");
                return;
            }
        }
        if (user is null)
        {
            SignInRefused(log, "unknown_account");
            ctx.Fail("No Medvya account for this sign-in.");
            return;
        }
        ctx.Principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("amr", "mfa")],
            CookieAuthenticationDefaults.AuthenticationScheme));
    }

    // Reason codes only; never the account, email or name.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in refused: {Reason}")]
    static partial void SignInRefused(ILogger logger, string reason);

    public static WebApplication UseStaffAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.Use(async (ctx, next) =>
        {
            // CSRF rides on the session cookie, so only cookie-carrying requests need the header (server-to-server calls carry none).
            if (ctx.Request.Cookies.ContainsKey(CookieName)
                && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method)
                && !ctx.Request.Headers.ContainsKey(CsrfHeader))
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            await next(ctx);
        });
        app.UseAuthorization();
        return app;
    }

    public static WebApplication MapStaffSession(this WebApplication app)
    {
        app.MapGet("/bff/login", (string? returnUrl, string? invitation) =>
            {
                var properties = new AuthenticationProperties { RedirectUri = returnUrl is not null && IsLocal(returnUrl) ? returnUrl : "/" };
                if (InvitationToken.Read(invitation) is not null) properties.Items[InvitationSignIn.Item] = invitation;
                return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
            })
            .AllowAnonymous()
            .RequireRateLimiting(SignInRateLimit);

        app.MapPost("/bff/logout", async (HttpContext ctx) =>
        {
            // shortcut: ends the Medvya session only; add provider sign-out (end_session_endpoint) with Entra (MDV-P0-011).
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapGet("/v1/me/context", async (ClaimsPrincipal principal, [FromServices] IStaffUsers users, CancellationToken ct) =>
        {
            var user = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? await users.GetAsync(id, ct) : null;
            // shortcut: user only; memberships, active facility, permissions and flags arrive with the facility picker (MDV-P0-012).
            return user is null ? Results.Unauthorized() : Results.Ok(new { user = new { user.Id, user.DisplayName } });
        }).RequireAuthorization();
        return app;
    }

    public const string SignInRateLimit = "sign-in";

    // Only same-site paths: "/x" but never "//host" or "/\host", which browsers treat as another site.
    static bool IsLocal(string url) => url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal);
}

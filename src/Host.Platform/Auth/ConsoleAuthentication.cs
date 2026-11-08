using System.Security.Claims;
using System.Text.Json;
using Medvya.BuildingBlocks.Security;
using Medvya.Platform.Api;
using Medvya.Platform.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Host.Platform.Auth;

/// <summary>
/// Platform console sign-in (MDV-P5-040, ADR-014): company sign-on through the backend (OIDC code + PKCE, tokens never reach the browser),
/// then the member's security key (WebAuthn). The cookie holds two ids; the session row decides everything else on every request:
/// whether it is live, whether the key was verified in it, and the member's current role.
/// </summary>
public static partial class ConsoleAuthentication
{
    public const string CookieName = "__Host-medvya-platform";
    /// <summary>State-changing requests must carry this header; a cross-site page cannot add it without a CORS preflight, which is never granted.</summary>
    public const string CsrfHeader = "X-Medvya-Csrf";
    public const string SessionIdClaim = "sid";
    public const string SignInRateLimit = "platform-sign-in";
    const string LinkItem = "medvya.keylink";

    public static WebApplicationBuilder AddConsoleAuthentication(this WebApplicationBuilder builder)
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
            o.ExpireTimeSpan = Medvya.Platform.Domain.PlatformSession.MaxLifetime;
            o.SlidingExpiration = false;
            o.Events.OnValidatePrincipal = ValidatePrincipalAsync;
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });

        // No provider configured = nobody can sign in (fail closed).
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
                o.SaveTokens = false;
                o.MapInboundClaims = false;
                o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                o.Events.OnTokenValidated = OnTokenValidated;
                o.Events.OnRemoteFailure = ctx =>
                {
                    ctx.Response.Redirect("/sign-in?signin=failed");
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }
        return builder;
    }

    // Only company accounts that are console members (or carry a fitting key link) get a session; it can do nothing until the key step.
    static async Task OnTokenValidated(TokenValidatedContext ctx)
    {
        var principal = ctx.Principal!;
        var issuer = principal.FindFirst("iss")?.Value;
        // Entra's object id is stable for the person across applications; "sub" is per application. The dev provider sends only "sub".
        var subject = principal.FindFirst("oid")?.Value ?? principal.FindFirst("sub")?.Value;
        var link = ctx.Properties?.Items.TryGetValue(LinkItem, out var l) == true ? l : null;
        var session = issuer is null || subject is null ? null
            : await ctx.HttpContext.RequestServices.GetRequiredService<IPlatformSignIn>().SignOnAsync(issuer, subject, link, ctx.HttpContext.RequestAborted);
        if (session is null)
        {
            SignOnRefused(ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ConsoleAuthentication)));
            ctx.Fail("Not a platform console member.");
            return;
        }
        ctx.Principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, session.MemberId.ToString()), new Claim(SessionIdClaim, session.SessionId.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme));
    }

    // No account, name or subject in the message: the scrubber is a safety net, not a licence.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Platform console sign-on refused: not a member, or the key link does not fit")]
    static partial void SignOnRefused(ILogger logger);

    // The session row must be live and the member still have access; the key and role claims come from it, never from the cookie.
    static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext ctx)
    {
        var state = Read(ctx.Principal!, SessionIdClaim) is { } sid && Read(ctx.Principal!, ClaimTypes.NameIdentifier) is { } member
            ? await ctx.HttpContext.RequestServices.GetRequiredService<IPlatformSignIn>().ValidateAsync(sid, member, activity: true, ctx.HttpContext.RequestAborted)
            : null;
        if (state is null)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        var identity = new ClaimsIdentity(ctx.Principal!.Claims.Where(c => c.Type is not (PlatformPolicies.RoleClaim or PlatformPolicies.KeyClaim)),
            CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(PlatformPolicies.RoleClaim, state.Role.ToString()));
        if (state.KeyVerified) identity.AddClaim(new Claim(PlatformPolicies.KeyClaim, PlatformPolicies.KeyVerified));
        ctx.ReplacePrincipal(new ClaimsPrincipal(identity)); // this request only: the cookie is not re-issued
    }

    public static WebApplication UseConsoleAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.Use(async (ctx, next) =>
        {
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

    public static WebApplication MapConsoleSession(this WebApplication app)
    {
        // A key link (invitation or recovery) rides in the protected OIDC state, then in the session row; the browser drops it here.
        app.MapGet("/bff/login", (string? link, string? returnUrl) =>
            {
                var properties = new AuthenticationProperties { RedirectUri = returnUrl is not null && IsLocal(returnUrl) ? returnUrl : "/" };
                if (link is { Length: > 0 and <= 128 }) properties.Items[LinkItem] = link;
                return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
            })
            .AllowAnonymous()
            .RequireRateLimiting(SignInRateLimit);

        app.MapGet("/v1/me", async (ClaimsPrincipal principal, [FromServices] IPlatformSignIn signIn, CancellationToken ct) =>
            await signIn.ValidateAsync(Read(principal, SessionIdClaim)!.Value, Read(principal, ClaimTypes.NameIdentifier)!.Value, activity: false, ct) is { } s
                ? Results.Ok(new { s.DisplayName, Role = s.Role.ToString(), s.KeyVerified, s.HasKey, s.CanRegister })
                : Results.Unauthorized())
            .RequireAuthorization(PlatformPolicies.SignedOn);

        var key = app.MapGroup("/bff/key").RequireAuthorization(PlatformPolicies.SignedOn).RequireRateLimiting(SignInRateLimit);
        key.MapPost("/check/options", async (ClaimsPrincipal principal, [FromServices] IPlatformSignIn signIn, CancellationToken ct) =>
            await signIn.BeginKeyCheckAsync(Session(principal), ct) is { } options
                ? Results.Content(options, "application/json")
                : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "No security key is registered"));
        key.MapPost("/check", async (ClaimsPrincipal principal, [FromBody] JsonElement response, [FromServices] IPlatformSignIn signIn, CancellationToken ct) =>
            await signIn.CompleteKeyCheckAsync(Session(principal), response.GetRawText(), ct) ? Results.NoContent() : Results.Unauthorized());
        key.MapPost("/registration/options", async (ClaimsPrincipal principal, [FromServices] IPlatformSignIn signIn, CancellationToken ct) =>
            await signIn.BeginKeyRegistrationAsync(Session(principal), ct) is { } options
                ? Results.Content(options, "application/json")
                : Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "A key link from an owner is needed"));
        key.MapPost("/registration", async (ClaimsPrincipal principal, [FromBody] JsonElement response, [FromServices] IPlatformSignIn signIn, CancellationToken ct) =>
            await signIn.CompleteKeyRegistrationAsync(Session(principal), response.GetRawText(), ct) ? Results.NoContent() : Results.Unauthorized());

        app.MapPost("/bff/logout", async (HttpContext http, [FromServices] IPlatformSignIn signIn) =>
        {
            await signIn.SignOutAsync(Session(http.User), http.RequestAborted);
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization(PlatformPolicies.SignedOn);
        return app;
    }

    static Guid Session(ClaimsPrincipal principal) => Read(principal, SessionIdClaim)!.Value;

    static Guid? Read(ClaimsPrincipal principal, string claim) => Guid.TryParse(principal.FindFirstValue(claim), out var id) ? id : null;

    static bool IsLocal(string url) => LocalUrl.IsLocal(url); // #876: control characters and backslashes refused anywhere
}

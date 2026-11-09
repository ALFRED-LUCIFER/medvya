using System.Security.Claims;
using Medvya.BuildingBlocks.Security;
using Medvya.Identity.Application;
using Medvya.Identity.Domain;
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
    const string SignUpItem = "medvya.signup";

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
            // The cookie lives at most 8 h; the server-side session (SessionControl) also enforces idle timeout and revocation.
            o.ExpireTimeSpan = StaffSession.MaxLifetime;
            o.SlidingExpiration = false;
            o.Events.OnValidatePrincipal = SessionControl.ValidatePrincipalAsync;
            // The native apps send a bearer token instead of the cookie (MDV-P5-033); every staff endpoint then authenticates that way.
            o.ForwardDefaultSelector = NativeAuthentication.SelectScheme;
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
                // The work email typed on the sign-in page (MDV-P1-059) rides in the challenge parameters, which are never stored in the state.
                o.Events.OnRedirectToIdentityProvider = ctx =>
                {
                    if (ctx.Properties.GetParameter<string>(LoginHintParameter) is { } hint) ctx.ProtocolMessage.LoginHint = hint;
                    // Step-up for signing (MDV-P2-012): the provider must ask for the credentials again, whatever its own session says.
                    if (ctx.Properties.Items.ContainsKey(ReauthItem)) { ctx.ProtocolMessage.Prompt = "login"; ctx.ProtocolMessage.MaxAge = "0"; }
                    return Task.CompletedTask;
                };
                // A refused or broken sign-in returns to the app with a generic error; the reason is only in the logs. The one exception is a
                // missing second factor (#912): the person can fix that themselves, and it says nothing about their Medvya account.
                o.Events.OnRemoteFailure = ctx =>
                {
                    var reason = ctx.Failure?.Message == MfaRequired ? "mfa" : "failed";
                    ctx.Response.Redirect($"{AppPath(ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>())}?signin={reason}");
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }
        return builder;
    }

    const string MfaRequired = "Multi-factor authentication is required.";

    // Accept only accounts that exist in Medvya and signed in with MFA; replace the provider's claims with ours.
    static async Task OnTokenValidated(TokenValidatedContext ctx)
    {
        var principal = ctx.Principal!;
        var log = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StaffAuthentication));
        // shortcut: MFA is read from the standard amr claim; confirm Entra External ID emits it (or use its MFA claim) in the Entra spike (MDV-P0-011).
        if (!principal.FindAll("amr").Any(c => c.Value == "mfa"))
        {
            SignInRefusedNoMfa(log);
            ctx.Fail(MfaRequired);
            return;
        }
        var issuer = principal.FindFirst("iss")?.Value;
        var subject = principal.FindFirst("sub")?.Value;
        var user = issuer is null || subject is null ? null
            : await ctx.HttpContext.RequestServices.GetRequiredService<IStaffUsers>().FindAsync(issuer, subject, ctx.HttpContext.RequestAborted);
        // Step-up (MDV-P2-012): only the person already signed in on this browser session may answer, and the session stays the same; the
        // cookie is re-issued with the time of this sign-in. Anyone else (or nobody) is refused and no new session starts.
        if (ctx.Properties?.Items.ContainsKey(ReauthItem) == true)
        {
            var current = ctx.HttpContext.User;
            if (user is null || current.FindFirst(ClaimTypes.NameIdentifier)?.Value != user.Id.ToString() || current.FindFirst(SessionControl.SessionIdClaim) is null)
            {
                SignInRefusedUnknownAccount(log);
                ctx.Fail("The re-authentication does not match the signed-in user.");
                return;
            }
            ctx.Principal = new ClaimsPrincipal(new ClaimsIdentity(
                current.Claims.Where(c => c.Type != ReauthClaim).Append(new Claim(ReauthClaim, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))),
                CookieAuthenticationDefaults.AuthenticationScheme));
            return;
        }
        // Joining by invitation (MDV-P0-013): the token rode in the protected OIDC state.
        if (issuer is not null && subject is not null && ctx.Properties?.Items.TryGetValue(InvitationSignIn.Item, out var invitation) == true && invitation is not null)
        {
            user = await InvitationSignIn.AcceptAsync(ctx.HttpContext, invitation, issuer, subject, principal.FindFirst("name")?.Value, user);
            if (user is null)
            {
                SignInRefusedInvitation(log);
                ctx.Fail("The invitation cannot be used.");
                return;
            }
        }
        // Signing up (MDV-P1-004): "Start" on the sign-up page asks for an account to be created. It still needs the provider's MFA
        // sign-in, and the new user has no membership, so no clinic data is reachable until the wizard creates their practice.
        if (user is null && issuer is not null && subject is not null && ctx.Properties?.Items.ContainsKey(SignUpItem) == true)
            user = await ctx.HttpContext.RequestServices.GetRequiredService<IStaffUsers>()
                .FindOrCreateAsync(issuer, subject, principal.FindFirst("name")?.Value ?? "New user", ctx.HttpContext.RequestAborted);
        if (user is null)
        {
            SignInRefusedUnknownAccount(log);
            ctx.Fail("No Medvya account for this sign-in.");
            return;
        }
        var session = await ctx.HttpContext.RequestServices.GetRequiredService<IStaffSessions>()
            .StartAsync(user.Id, ctx.Request.Headers.UserAgent.ToString(), ctx.HttpContext.RequestAborted);
        ctx.Principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("amr", "mfa"), new Claim(SessionControl.SessionIdClaim, session.Id.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme));
    }

    // One fixed message per reason, no arguments: the telemetry scrubber redacts every logged value, so a {Reason} argument would be unreadable.
    // Never the account, email or name.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in refused: the provider did not report multi-factor authentication")]
    static partial void SignInRefusedNoMfa(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in refused: the invitation cannot be used")]
    static partial void SignInRefusedInvitation(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in refused: no Medvya account for this sign-in")]
    static partial void SignInRefusedUnknownAccount(ILogger logger);

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
        // No-hint start: deploy smoke test, invitation links, passkey and organisation buttons. The work email never rides in a Medvya
        // URL (App Service logs, browser history and proxies keep URLs, AGENTS.md 1.2), so a query-string hint is refused, not read.
        app.MapGet("/bff/login", (string? returnUrl, string? invitation, bool? signup, HttpRequest request, IConfiguration config) =>
                request.Query.ContainsKey("loginHint")
                    ? Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Send the login hint in the request body")
                    : Results.Challenge(SignInProperties(returnUrl, invitation, signup, config), [OpenIdConnectDefaults.AuthenticationScheme]))
            .AllowAnonymous()
            .RequireRateLimiting(SignInRateLimit);

        // Start with the work email (MDV-P1-059): the hint comes in the body and the browser follows the returned provider address itself.
        app.MapPost("/bff/login", async (SignInStart start, HttpContext ctx, IConfiguration config) =>
            {
                // The house anti-forgery header: a cross-site page cannot add it without a CORS preflight, which this API never grants.
                if (!ctx.Request.Headers.ContainsKey(CsrfHeader)) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Missing anti-forgery header");
                // A hint is only a convenience for the provider's page, so a malformed one is refused rather than passed on.
                if (start.LoginHint is not null && !IsLoginHint(start.LoginHint)) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid login hint");
                var properties = SignInProperties(start.ReturnUrl, start.Invitation, start.Signup, config);
                if (start.LoginHint is not null) properties.SetParameter(LoginHintParameter, start.LoginHint);
                await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
                // The handler answered with a redirect; hand its address (and its correlation cookies) to the page instead.
                var location = ctx.Response.Headers.Location.ToString();
                ctx.Response.Headers.Remove(Microsoft.Net.Http.Headers.HeaderNames.Location);
                return Results.Ok(new SignInRedirect(location));
            })
            .AllowAnonymous()
            .RequireRateLimiting(SignInRateLimit);

        // Step-up before signing (MDV-P2-012): a signed-in browser session asks the provider for the password and second factor again.
        // POST with the house header like the sign-in start; the page navigates to the returned address and comes back to returnUrl.
        app.MapPost("/bff/reauth", async (ReauthStart start, HttpContext ctx, IConfiguration config) =>
            {
                if (!ctx.Request.Headers.ContainsKey(CsrfHeader)) return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Missing anti-forgery header");
                var properties = SignInProperties(start.ReturnUrl, null, null, config);
                properties.Items[ReauthItem] = "1";
                await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
                var location = ctx.Response.Headers.Location.ToString();
                ctx.Response.Headers.Remove(Microsoft.Net.Http.Headers.HeaderNames.Location);
                return Results.Ok(new SignInRedirect(location));
            })
            .RequireAuthorization()
            .RequireRateLimiting(SignInRateLimit);

        // /bff/logout and /v1/me/* live in SessionControl.
        return app.MapSessionControl();
    }

    public const string SignInRateLimit = Medvya.Identity.Api.InvitationEndpoints.SignInRateLimit;

    /// <summary>Where the web app lives: "/app/" when the API serves the built app (the public site owns "/"), "/" in development.</summary>
    public static string AppPath(IConfiguration config) => config["Web:AppPath"] ?? "/";

    const string LoginHintParameter = "login_hint";
    const string ReauthItem = "reauth";
    /// <summary>Unix seconds of the last re-authentication, in the session cookie (read by RequestContext, checked by the sign use case).</summary>
    public const string ReauthClaim = "reauth_at";

    static AuthenticationProperties SignInProperties(string? returnUrl, string? invitation, bool? signup, IConfiguration config)
    {
        var properties = new AuthenticationProperties { RedirectUri = returnUrl is not null && IsLocal(returnUrl) ? returnUrl : AppPath(config) };
        if (InvitationToken.Read(invitation) is not null) properties.Items[InvitationSignIn.Item] = invitation;
        if (signup == true) properties.Items[SignUpItem] = "1";
        return properties;
    }

    /// <summary>An email address as typed on the sign-in page: one address, no display name, no control characters, at most 254 characters.</summary>
    internal static bool IsLoginHint(string value) =>
        value.Length <= 254 && !value.Any(char.IsControl) && !value.Contains(' ', StringComparison.Ordinal)
        && System.Net.Mail.MailAddress.TryCreate(value, out var address) && address.Address == value && address.DisplayName.Length == 0;

    // Only same-site paths: "/x" but never "//host" or "/\host", which browsers treat as another site.
    static bool IsLocal(string url) => LocalUrl.IsLocal(url); // #876: control characters and backslashes refused anywhere

    /// <summary>Starting sign-in with the work email (MDV-P1-059). The email is a hint for the provider only: never stored or logged.</summary>
    public sealed record SignInStart(string? LoginHint = null, string? ReturnUrl = null, string? Invitation = null, bool? Signup = null);

    public sealed record ReauthStart(string? ReturnUrl = null);

    /// <summary>The identity provider's address (sign-in or sign-out); the page navigates there.</summary>
    public sealed record SignInRedirect(string Location);
}

using System.Security.Claims;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Identity.Application;
using Medvya.Identity.Api;
using Medvya.Identity.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Api.Auth;

/// <summary>
/// Session control (MDV-P0-012, ADR-004): a server-side session row behind every cookie, the facility picker that scopes
/// the session to one membership, and sign-out. The cookie holds ids only. Revocation by the user or a tenant admin is in Identity.Api (SessionEndpoints).
/// </summary>
public static class SessionControl
{
    public const string SessionIdClaim = SessionEndpoints.SessionIdClaim;
    /// <summary>Read by RequestContext → ICurrentTenant, which sets the session tenant setting for row-level security.</summary>
    public const string TenantClaim = StaffPolicies.TenantClaim;
    public const string MembershipClaim = "membership_id";
    public const string FacilityClaim = "facility_id";
    /// <summary>Old cookies carried it. Never read for authorisation (CapabilityHandler reads the database) and no longer issued.</summary>
    const string RoleClaim = "role";
    /// <summary>Background requests (status polls) send this so they do not count as activity and cannot keep an idle session alive.</summary>
    public const string PassiveHeader = "X-Medvya-Passive";
    /// <summary>Tenant administrators: Config on "Staff, roles, integrations" in the role matrix (Practice admin, owner-doctor, hospital IT).</summary>
    public const string StaffAdminPolicy = SessionEndpoints.StaffAdminPolicy;

    public sealed record FacilityChoiceRequest(Guid MembershipId);

    public static AuthorizationBuilder AddStaffAdminPolicy(this AuthorizationBuilder builder) =>
        builder.AddPolicy(StaffAdminPolicy, p => p.RequireAuthenticatedUser()
            .AddRequirements(new CapabilityRequirement(Capability.StaffRolesIntegrations, [Access.Config])));

    /// <summary>Runs on every cookie-authenticated request: the session row must still be live and match the cookie.</summary>
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext ctx)
    {
        if (!await IsLiveAsync(ctx.Principal!, ctx.HttpContext.RequestServices, activity: IsActivity(ctx.HttpContext), ctx.HttpContext.RequestAborted))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    /// <summary>
    /// A request counts as the person's activity unless it says it is a background poll, or it opens a live-update (hub) connection:
    /// the browser cannot put a header on a WebSocket upgrade, and the connection reconnects by itself on an unattended screen.
    /// </summary>
    internal static bool IsActivity(HttpContext http) =>
        !http.Request.Headers.ContainsKey(PassiveHeader) && http.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.SignalR.HubMetadata>() is null;

    /// <summary>
    /// The principal's server-side session is still live and scoped to the membership the principal names. Also run on every
    /// SignalR call (<see cref="Host.Api.Context.HubRequestContext"/>), where the cookie was checked only when the connection opened.
    /// </summary>
    public static async Task<bool> IsLiveAsync(ClaimsPrincipal principal, IServiceProvider services, bool activity, CancellationToken cancellationToken)
    {
        var session = Read(principal, SessionIdClaim) is { } sid && Read(principal, ClaimTypes.NameIdentifier) is { } uid
            ? await services.GetRequiredService<IStaffSessions>().ValidateAsync(sid, uid, activity, cancellationToken)
            : null;
        // A copy of the cookie from before a facility switch names another membership than the session: refuse it.
        // A native app session is reachable by bearer token only, a browser session by cookie only (MDV-P5-033).
        return session is not null && session.TenantId == Read(principal, TenantClaim) && session.MembershipId == Read(principal, MembershipClaim)
            && session.IsNative == NativeAuthentication.IsNative(principal);
    }

    public static WebApplication MapSessionControl(this WebApplication app)
    {
        app.MapGet("/v1/me/context", async (ClaimsPrincipal principal, [FromServices] IStaffUsers users, [FromServices] IMembershipDirectory directory,
            [FromServices] IFeatureFlags flags, [FromServices] IStaffAccess staff, [FromServices] IFacilityDirectory facilities,
            [FromServices] IStaffSessions sessions, [FromServices] TimeProvider clock, CancellationToken ct) =>
        {
            var user = Read(principal, ClaimTypes.NameIdentifier) is { } id ? await users.GetAsync(id, ct) : null;
            if (user is null) return Results.Unauthorized();
            var memberships = await directory.ForCurrentUserAsync(ct);
            // When this browser session ends if nothing more is done (#1365 F-057): the app warns shortly before and offers to stay signed in.
            // Read without counting as activity; the request's own activity, if any, was recorded when its cookie was checked.
            var session = !NativeAuthentication.IsNative(principal) && Read(principal, SessionIdClaim) is { } sid ? await sessions.ValidateAsync(sid, user.Id, activity: false, ct) : null;
            var ends = session?.Ends();
            // Role comes from the live membership row, so a suspended membership is no longer offered as active.
            var active = memberships.FirstOrDefault(m => m.Id == Read(principal, MembershipClaim));
            var state = active is null ? OrganisationState.Draft : await staff.OrganisationStateAsync(ct);
            var capabilities = active is null ? new Dictionary<string, string>() : RoleMatrix.Grants(active.Role).Select(g => (g.Capability, Access: OrganisationRules.Apply(g.Access, g.Capability, state)))
                .Where(g => g.Access != Access.None).ToDictionary(g => g.Capability.ToString(), g => g.Access.ToString());
            // Capabilities a practice-defined role adds to its base role (MDV-P1-008); the same live check the endpoints use.
            if (active is not null)
                foreach (var extra in CustomRoleRules.Extras(active.Role))
                    if (await staff.AccessAsync(extra, ct) is var access && access != Access.None) capabilities[extra.ToString()] = access.ToString();
            return Results.Ok(new
            {
                user = new { user.Id, user.DisplayName },
                // No active membership but a suspended or offboarded one: access was removed (F-05b), not "not set up yet".
                accessEnded = memberships.Count == 0 && await directory.HasEndedMembershipAsync(ct),
                memberships = memberships.Select(m => new { m.Id, m.TenantId, m.TenantName, m.FacilityId, m.FacilityName, Role = m.Role.ToString() }),
                // The picker lists the last used facility first (MDV-P1-059); only ever one of the memberships above.
                lastMembershipId = await sessions.LastMembershipAsync(user.Id, ct) is { } last && memberships.Any(m => m.Id == last) ? last : (Guid?)null,
                session = ends is null ? null : new
                {
                    EndsInSeconds = Math.Max(0, (int)(ends.Value.At - clock.GetUtcNow()).TotalSeconds),
                    ends.Value.Extendable,
                    IdleTimeoutSeconds = session!.IdleTimeoutMinutes * 60,
                },
                active = active is null ? null : new
                {
                    MembershipId = active.Id,
                    active.TenantId,
                    active.FacilityId,
                    // The tenant's country picks the registration rules and formats on the client; never chosen by the client.
                    Country = await staff.TenantCountryAsync(ct),
                    // The facility's time zone: screens that show instants (the access log) show them in clinic time, not the browser's.
                    TimeZone = (await facilities.FindAsync(active.FacilityId, ct))?.TimeZone,
                    Role = active.Role.ToString(),
                    // Before approval only set-up is open; Suspended is read-only (ADR-016), as the server enforces on each call.
                    OrganisationState = state.ToString(),
                    Capabilities = capabilities,
                    Flags = await flags.EnabledKeysAsync(ct),
                },
            });
        }).RequireAuthorization();

        // Facility picker: scope the session to one of the user's active memberships. No re-authentication (MFA stays from sign-in).
        app.MapPost("/v1/me/facility", async (FacilityChoiceRequest body, HttpContext http, [FromServices] IMembershipDirectory directory,
            [FromServices] IStaffSessions sessions, CancellationToken ct) =>
        {
            var choice = (await directory.ForCurrentUserAsync(ct)).FirstOrDefault(m => m.Id == body.MembershipId);
            if (choice is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not an active membership of yours");
            var sessionId = Read(http.User, SessionIdClaim)!.Value;
            var firstChoice = http.User.FindFirst(TenantClaim) is null;
            var auth = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme); // keeps the cookie's original issue/expiry times

            var identity = new ClaimsIdentity(http.User.Claims.Where(c => c.Type is not (TenantClaim or MembershipClaim or FacilityClaim or RoleClaim)),
                CookieAuthenticationDefaults.AuthenticationScheme);
            identity.AddClaims([
                new Claim(TenantClaim, choice.TenantId.ToString()), new Claim(MembershipClaim, choice.Id.ToString()),
                new Claim(FacilityClaim, choice.FacilityId.ToString()),            ]);
            // From here the request runs in the chosen tenant, so the audit row (row-level secured) lands there.
            http.User = new ClaimsPrincipal(identity);
            await WriteAuditAsync(http, firstChoice ? "auth.signin" : "auth.facility.switch", "session", sessionId);
            await sessions.ScopeAsync(sessionId, choice.TenantId, choice.Id, choice.IdleTimeoutMinutes, ct);
            // A native app has no cookie to re-issue: its access token now names the old membership and is refused, so it refreshes.
            if (NativeAuthentication.IsNative(auth.Principal!)) return Results.NoContent();
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, http.User, auth.Properties);
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapPost("/bff/logout", async (HttpContext http, [FromServices] IStaffSessions sessions, IConfiguration config) =>
        {
            if (Read(http.User, SessionIdClaim) is { } sessionId && Read(http.User, ClaimTypes.NameIdentifier) is { } userId)
            {
                // A session that never chose a facility has no tenant: its sign-out goes in the platform chain.
                if (http.User.FindFirst(TenantClaim) is not null) await WriteAuditAsync(http, "auth.signout", "session", sessionId);
                else await AuditedEndpoint.WritePlatformAsync(http, "auth.signout", "session", "support", sessionId, AuditOutcome.Allowed);
                await sessions.RevokeAsync(userId, sessionId, http.RequestAborted);
            }
            // A native app holds no cookie to clear (and the bearer scheme cannot sign out); revoking the row is the whole logout.
            if (NativeAuthentication.IsNative(http.User)) return Results.NoContent();
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            // #912: also end the provider session (OIDC RP-initiated logout), or on a shared PC it signs the last account straight back in.
            // The provider returns to /signout-callback-oidc, which sends the browser on to the app. Its address goes to the page, as on sign-in.
            // shortcut: a session that already idled out or was revoked gets 401 here and never reaches the provider sign-out; add a provider
            // sign-out to the "You were signed out" screen if pilot desks hit it (#912 follow-up).
            await http.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties { RedirectUri = StaffAuthentication.AppPath(config) });
            var location = http.Response.Headers.Location.ToString();
            http.Response.Headers.Remove(Microsoft.Net.Http.Headers.HeaderNames.Location);
            return Results.Ok(new StaffAuthentication.SignInRedirect(location));
        }).RequireAuthorization();

        return app;
    }

    internal static Task WriteAuditAsync(HttpContext http, string action, string resourceType, Guid resourceId) => SessionEndpoints.WriteAuditAsync(http, action, resourceType, resourceId);

    static Guid? Read(ClaimsPrincipal principal, string claim) => Guid.TryParse(principal.FindFirstValue(claim), out var id) ? id : null;
}

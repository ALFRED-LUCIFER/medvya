using System.Security.Claims;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Identity.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Api.Auth;

/// <summary>
/// Session control (MDV-P0-012, ADR-004): a server-side session row behind every cookie, the facility picker that scopes
/// the session to one membership, and revocation by the user or a tenant admin. The cookie holds ids only.
/// </summary>
public static class SessionControl
{
    public const string SessionIdClaim = "sid";
    /// <summary>Read by RequestContext → ICurrentTenant, which sets SESSION_CONTEXT for row-level security.</summary>
    public const string TenantClaim = "tenant_id";
    public const string MembershipClaim = "membership_id";
    public const string FacilityClaim = "facility_id";
    public const string RoleClaim = "role";
    /// <summary>Background requests (status polls) send this so they do not count as activity and cannot keep an idle session alive.</summary>
    public const string PassiveHeader = "X-Medvya-Passive";
    /// <summary>Tenant administrators: Config on "Staff, roles, integrations" in the role matrix (Practice admin, solo-doctor owner, hospital IT).</summary>
    public const string StaffAdminPolicy = "staff-admin";

    public sealed record FacilityChoiceRequest(Guid MembershipId);

    public static AuthorizationBuilder AddStaffAdminPolicy(this AuthorizationBuilder builder) =>
        builder.AddPolicy(StaffAdminPolicy, p => p.RequireAuthenticatedUser().RequireAssertion(c =>
            c.User.FindFirst(TenantClaim) is not null
            && Enum.TryParse<Role>(c.User.FindFirstValue(RoleClaim), out var role) && Enum.IsDefined(role)
            && RoleMatrix.Of(role, Capability.StaffRolesIntegrations) == Access.Config));

    /// <summary>Runs on every cookie-authenticated request: the session row must still be live and match the cookie.</summary>
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext ctx)
    {
        var principal = ctx.Principal!;
        var sessions = ctx.HttpContext.RequestServices.GetRequiredService<IStaffSessions>();
        var session = Read(principal, SessionIdClaim) is { } sid && Read(principal, ClaimTypes.NameIdentifier) is { } uid
            ? await sessions.ValidateAsync(sid, uid, activity: !ctx.Request.Headers.ContainsKey(PassiveHeader), ctx.HttpContext.RequestAborted)
            : null;
        // A copy of the cookie from before a facility switch names another membership than the session: refuse it.
        if (session is null || session.TenantId != Read(principal, TenantClaim) || session.MembershipId != Read(principal, MembershipClaim))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    public static WebApplication MapSessionControl(this WebApplication app)
    {
        app.MapGet("/v1/me/context", async (ClaimsPrincipal principal, [FromServices] IStaffUsers users, [FromServices] IMembershipDirectory directory,
            [FromServices] IFeatureFlags flags, [FromServices] IStaffAccess staff, CancellationToken ct) =>
        {
            var user = Read(principal, ClaimTypes.NameIdentifier) is { } id ? await users.GetAsync(id, ct) : null;
            if (user is null) return Results.Unauthorized();
            var memberships = await directory.ForCurrentUserAsync(ct);
            // Role comes from the live membership row, so a suspended membership is no longer offered as active.
            var active = memberships.FirstOrDefault(m => m.Id == Read(principal, MembershipClaim));
            return Results.Ok(new
            {
                user = new { user.Id, user.DisplayName },
                memberships = memberships.Select(m => new { m.Id, m.TenantId, m.TenantName, m.FacilityId, m.FacilityName, Role = m.Role.ToString() }),
                active = active is null ? null : new
                {
                    MembershipId = active.Id,
                    active.TenantId,
                    active.FacilityId,
                    // The tenant's country picks the registration rules and formats on the client; never chosen by the client.
                    Country = await staff.TenantCountryAsync(ct),
                    Role = active.Role.ToString(),
                    Capabilities = RoleMatrix.Grants(active.Role).ToDictionary(g => g.Capability.ToString(), g => g.Access.ToString()),
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
                new Claim(FacilityClaim, choice.FacilityId.ToString()), new Claim(RoleClaim, choice.Role.ToString()),
            ]);
            // From here the request runs in the chosen tenant, so the audit row (row-level secured) lands there.
            http.User = new ClaimsPrincipal(identity);
            await WriteAuditAsync(http, firstChoice ? "auth.signin" : "auth.facility.switch", "session", sessionId);
            await sessions.ScopeAsync(sessionId, choice.TenantId, choice.Id, choice.IdleTimeoutMinutes, ct);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, http.User, auth.Properties);
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapPost("/bff/logout", async (HttpContext http, [FromServices] IStaffSessions sessions) =>
        {
            if (Read(http.User, SessionIdClaim) is { } sessionId && Read(http.User, ClaimTypes.NameIdentifier) is { } userId)
            {
                // Audit rows belong to a tenant; a session that never chose a facility has none and is ended unaudited.
                if (http.User.FindFirst(TenantClaim) is not null) await WriteAuditAsync(http, "auth.signout", "session", sessionId);
                await sessions.RevokeAsync(userId, sessionId, http.RequestAborted);
            }
            // shortcut: ends the Medvya session only; add provider sign-out (end_session_endpoint) with Entra (MDV-P0-011).
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization();

        // "My devices": the caller's live sessions.
        app.MapGet("/v1/me/sessions", async (ClaimsPrincipal principal, [FromServices] IStaffSessions sessions, CancellationToken ct) =>
        {
            var current = Read(principal, SessionIdClaim);
            var list = await sessions.ListAsync(Read(principal, ClaimTypes.NameIdentifier)!.Value, ct);
            return Results.Ok(list.Select(s => new { s.Id, s.Device, s.CreatedAt, s.LastSeenAt, Current = s.Id == current }));
        }).RequireAuthorization();

        app.MapDelete("/v1/me/sessions/{id:guid}", async (Guid id, HttpContext http, [FromServices] IStaffSessions sessions) =>
        {
            if (http.User.FindFirst(TenantClaim) is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
            await WriteAuditAsync(http, "session.revoke", "session", id);
            return await sessions.RevokeAsync(Read(http.User, ClaimTypes.NameIdentifier)!.Value, id, http.RequestAborted) is null
                ? Results.NotFound() : Results.NoContent();
        }).RequireAuthorization();

        // Tenant admin ends a staff member's sessions in this tenant only; their sessions in other clinics are not this tenant's to end.
        app.MapDelete("/v1/staff/{userId:guid}/sessions", async (Guid userId, HttpContext http, [FromServices] IStaffSessions sessions) =>
        {
            await WriteAuditAsync(http, "session.revoke", "staff_user", userId);
            await sessions.RevokeInTenantAsync(userId, Read(http.User, TenantClaim)!.Value, http.RequestAborted);
            return Results.NoContent();
        }).RequireAuthorization(StaffAdminPolicy);
        return app;
    }

    // Written before the change it records: if the audit write fails, the request fails (fail closed), as .Audited() does.
    static Task WriteAuditAsync(HttpContext http, string action, string resourceType, Guid resourceId) =>
        http.RequestServices.GetRequiredService<IAuditLog>().WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            TenantId = Read(http.User, TenantClaim)!.Value,
            ActorUserId = Read(http.User, ClaimTypes.NameIdentifier),
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Purpose = "support",
            Outcome = AuditOutcome.Allowed,
            OccurredAt = DateTimeOffset.UtcNow,
            ClientIp = http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId = http.TraceIdentifier,
        }, http.RequestAborted);

    static Guid? Read(ClaimsPrincipal principal, string claim) => Guid.TryParse(principal.FindFirstValue(claim), out var id) ? id : null;
}

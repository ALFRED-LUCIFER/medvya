using System.Security.Claims;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Identity.Application;
using Medvya.Identity.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Medvya.Identity.Api;

/// <summary>
/// A staff member's own sessions ("My devices"), the app's push token, and a tenant admin ending a staff member's sessions (MDV-P0-012,
/// MDV-P5-033). The host keeps the plumbing that issues and checks the session (cookie, bearer, facility picker, sign-out).
/// </summary>
public static class SessionEndpoints
{
    /// <summary>The claim naming the server-side session row; the host's sign-in writes it.</summary>
    public const string SessionIdClaim = "sid";
    /// <summary>The native app's bearer scheme (MDV-P5-033).</summary>
    public const string NativeScheme = "Native";
    /// <summary>Tenant administrators: Config on "Staff, roles, integrations" in the role matrix (Practice admin, owner-doctor, hospital IT).</summary>
    public const string StaffAdminPolicy = "staff-admin";

    public sealed record PushTokenRequest(string? Token);

    /// <summary>The caller came in on the native app's bearer token, not a browser cookie.</summary>
    public static bool IsNative(ClaimsPrincipal principal) => principal.Identity?.AuthenticationType == NativeScheme;

    static IResult NotAnApp() => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only the app's own session has a push token");

    public static WebApplication MapSessionEndpoints(this WebApplication app)
    {
        // "My devices": the caller's live sessions.
        app.MapGet("/v1/me/sessions", async (ClaimsPrincipal principal, [FromServices] IStaffSessions sessions, CancellationToken ct) =>
        {
            var current = Read(principal, SessionIdClaim);
            var list = await sessions.ListAsync(Read(principal, ClaimTypes.NameIdentifier)!.Value, ct);
            return Results.Ok(list.Select(s => new { s.Id, s.Device, s.CreatedAt, s.LastSeenAt, Current = s.Id == current }));
        }).RequireAuthorization();

        app.MapDelete("/v1/me/sessions/{id:guid}", async (Guid id, HttpContext http, [FromServices] IStaffSessions sessions) =>
        {
            if (http.User.FindFirst(StaffPolicies.TenantClaim) is null) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
            await WriteAuditAsync(http, "session.revoke", "session", id);
            return await sessions.RevokeAsync(Read(http.User, ClaimTypes.NameIdentifier)!.Value, id, http.RequestAborted) is null
                ? Results.NotFound() : Results.NoContent();
        }).RequireAuthorization();

        // Push token (MDV-P5-033, ADR-011 §8): the app registers or replaces its device's token on its own native session, or removes it; any
        // revoke (sign-out, My devices, offboarding, a refresh replay) removes it too. The token travels in the body only and is never logged.
        app.MapPut("/v1/me/push-token", async (PushTokenRequest body, HttpContext http, [FromServices] IStaffSessions sessions) =>
            !IsNative(http.User) ? NotAnApp()
            : !StaffSession.IsPushToken(body.Token) ? Results.ValidationProblem(new Dictionary<string, string[]> { ["token"] = ["invalid"] })
            : await sessions.SetPushTokenAsync(Read(http.User, ClaimTypes.NameIdentifier)!.Value, Read(http.User, SessionIdClaim)!.Value, body.Token, http.RequestAborted)
                ? Results.NoContent() : Results.NotFound()).RequireAuthorization();
        app.MapDelete("/v1/me/push-token", async (HttpContext http, [FromServices] IStaffSessions sessions) =>
            !IsNative(http.User) ? NotAnApp()
            : await sessions.SetPushTokenAsync(Read(http.User, ClaimTypes.NameIdentifier)!.Value, Read(http.User, SessionIdClaim)!.Value, null, http.RequestAborted)
                ? Results.NoContent() : Results.NotFound()).RequireAuthorization();

        // Tenant admin ends a staff member's sessions in this tenant only; their sessions in other clinics are not this tenant's to end.
        // Like every staff change (StaffRoster): only an administrator of staff at a location where the person works; anyone else gets 404.
        app.MapDelete("/v1/staff/{userId:guid}/sessions", async (Guid userId, HttpContext http, [FromServices] IStaffSessions sessions, [FromServices] IStaffRoster roster, [FromServices] IStaffAccess access) =>
        {
            var rows = await roster.ListAsync(http.RequestAborted);
            var target = rows.Where(r => r.UserId == userId).ToList();
            var administers = false;
            foreach (var facility in target.Select(r => r.FacilityId).Distinct())
                administers = administers || await access.AccessAsync(Capability.StaffRolesIntegrations, facility, http.RequestAborted) == Access.Config;
            // As offboarding (MDV-P1-107): an owner or a doctor is signed out only by an owner. SAFETY: mutation-checked in SessionControlTests.
            var me = Read(http.User, ClaimTypes.NameIdentifier);
            var protectedPerson = target.Any(r => r.IsOwner || r.Role is Role.SoloDoctor or Role.HospitalDoctor);
            if (!administers || (protectedPerson && !rows.Any(r => r.UserId == me && r.IsOwner && r.Status == MembershipStatus.Active))) return Results.NotFound();
            await WriteAuditAsync(http, "session.revoke", "staff_user", userId);
            await sessions.RevokeInTenantAsync(userId, Read(http.User, StaffPolicies.TenantClaim)!.Value, http.RequestAborted);
            return Results.NoContent();
        }).RequireAuthorization(StaffAdminPolicy);
        return app;
    }

    /// <summary>A session audit row in the caller's tenant, written before the change it records: if the write fails, the request fails (fail closed), as .Audited() does.</summary>
    // shortcut: sessions live in the Identity DbContext, so this is not one transaction; a failed revoke leaves an event for a change that did not land (the user retries). Share the transaction if session audits must be exact.
    public static Task WriteAuditAsync(HttpContext http, string action, string resourceType, Guid resourceId)
    {
        return http.RequestServices.GetRequiredService<IAuditLog>().WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            TenantId = Read(http.User, StaffPolicies.TenantClaim)!.Value,
            ActorUserId = Read(http.User, ClaimTypes.NameIdentifier),
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Purpose = "support",
            Outcome = AuditOutcome.Allowed,
            OccurredAt = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
            ClientIp = http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId = http.TraceIdentifier,
        }, http.RequestAborted);
    }

    static Guid? Read(ClaimsPrincipal principal, string claim) => Guid.TryParse(principal.FindFirstValue(claim), out var id) ? id : null;
}

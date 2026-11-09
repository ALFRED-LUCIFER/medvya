using System.Security.Claims;
using Medvya.BuildingBlocks.Domain;

namespace Host.Api.Context;

/// <summary>
/// Tenant, facility and user of the current request, from the signed-in principal. The tenant and facility claims are set
/// when the user picks a facility (SessionControl); before that they are null and tenant data stays unreachable.
/// </summary>
public sealed class RequestContext(IHttpContextAccessor accessor, IServiceProvider services) : ICurrentTenant, ICurrentFacility, ICurrentUser, IRequestOrigin, ILinkTenant
{
    Guid? linkTenant;
    HttpContext? connection;

    public Guid? TenantId => linkTenant ?? Read(Host.Api.Auth.SessionControl.TenantClaim);
    public Guid? FacilityId => Read(Host.Api.Auth.SessionControl.FacilityClaim);

    /// <summary>
    /// Scopes this context to the tenant named by a tenant-bound link (an invitation token) instead of the session.
    /// Only on a fresh DI scope created for that link (<see cref="LinkTenantScope"/>), so the request's own tenant never changes.
    /// </summary>
    public void UseLinkTenant(Guid tenantId) => linkTenant = tenantId;

    /// <summary>
    /// Reads a SignalR connection's own HttpContext (the principal it was opened with) instead of the ambient request: hub calls
    /// run outside the request pipeline, on long polling inside another request entirely. Set per hub call (<see cref="HubRequestContext"/>).
    /// </summary>
    internal void UseConnection(HttpContext http) => connection = http;

    HttpContext? Http => connection ?? accessor.HttpContext;
    public Guid? UserId => Read(ClaimTypes.NameIdentifier);
    public DateTimeOffset? ReauthenticatedAt => long.TryParse(Http?.User.FindFirst(Host.Api.Auth.StaffAuthentication.ReauthClaim)?.Value, out var at) ? DateTimeOffset.FromUnixTimeSeconds(at) : null;
    public string? ClientIp => Http?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => Http?.TraceIdentifier;
    string? role;
    bool roleRead;

    // Resolved lazily: IStaffAccess itself depends on this context. One lookup per scope (request or hub call).
    public async Task<string?> RoleAsync(CancellationToken cancellationToken = default)
    {
        if (roleRead) return role;
        role = (await services.GetRequiredService<Medvya.Tenancy.Application.IStaffAccess>().CurrentRoleAsync(cancellationToken))?.ToString();
        roleRead = true;
        return role;
    }

    Guid? Read(string claim) => Guid.TryParse(Http?.User.FindFirst(claim)?.Value, out var id) ? id : null;
}

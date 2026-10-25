using System.Security.Claims;
using Medvya.BuildingBlocks.Domain;

namespace Host.Api.Context;

/// <summary>
/// Tenant, facility and user of the current request, from the signed-in principal. The tenant and facility claims are set
/// when the user picks a facility (SessionControl); before that they are null and tenant data stays unreachable.
/// </summary>
public sealed class RequestContext(IHttpContextAccessor accessor) : ICurrentTenant, ICurrentFacility, ICurrentUser, IRequestOrigin
{
    Guid? linkTenant;
    HttpContext? connection;

    public Guid? TenantId => linkTenant ?? Read(Host.Api.Auth.SessionControl.TenantClaim);
    public Guid? FacilityId => Read(Host.Api.Auth.SessionControl.FacilityClaim);

    /// <summary>
    /// Scopes this context to the tenant named by a tenant-bound link (an invitation token) instead of the session.
    /// Only on a fresh DI scope created for that link (<see cref="LinkTenantScope"/>), so the request's own tenant never changes.
    /// </summary>
    internal void UseLinkTenant(Guid tenantId) => linkTenant = tenantId;

    /// <summary>
    /// Reads a SignalR connection's own HttpContext (the principal it was opened with) instead of the ambient request: hub calls
    /// run outside the request pipeline, on long polling inside another request entirely. Set per hub call (<see cref="HubRequestContext"/>).
    /// </summary>
    internal void UseConnection(HttpContext http) => connection = http;

    HttpContext? Http => connection ?? accessor.HttpContext;
    public Guid? UserId => Read(ClaimTypes.NameIdentifier);
    public string? ClientIp => Http?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => Http?.TraceIdentifier;

    Guid? Read(string claim) => Guid.TryParse(Http?.User.FindFirst(claim)?.Value, out var id) ? id : null;
}

public static class LinkTenantScope
{
    /// <summary>A DI scope whose services (DbContexts, audit, RLS session context) run in <paramref name="tenantId"/>.</summary>
    public static AsyncServiceScope EnterLinkTenant(this IServiceProvider services, Guid tenantId)
    {
        var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().UseLinkTenant(tenantId);
        return scope;
    }
}

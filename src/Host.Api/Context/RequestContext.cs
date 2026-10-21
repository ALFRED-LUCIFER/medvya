using System.Security.Claims;
using Medvya.BuildingBlocks.Domain;

namespace Host.Api.Context;

/// <summary>
/// Tenant and user of the current request, from the signed-in principal. The tenant claim is set when the user picks a
/// facility (SessionControl); before that it is null and tenant data stays unreachable.
/// </summary>
public sealed class RequestContext(IHttpContextAccessor accessor) : ICurrentTenant, ICurrentUser
{
    public Guid? TenantId => Read(Host.Api.Auth.SessionControl.TenantClaim);
    public Guid? UserId => Read(ClaimTypes.NameIdentifier);

    Guid? Read(string claim) => Guid.TryParse(accessor.HttpContext?.User.FindFirst(claim)?.Value, out var id) ? id : null;
}

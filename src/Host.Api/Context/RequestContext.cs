using System.Security.Claims;
using Medvya.BuildingBlocks.Domain;

namespace Host.Api.Context;

/// <summary>
/// Tenant, facility and user of the current request, from the signed-in principal. The tenant and facility claims are set
/// when the user picks a facility (SessionControl); before that they are null and tenant data stays unreachable.
/// </summary>
public sealed class RequestContext(IHttpContextAccessor accessor) : ICurrentTenant, ICurrentFacility, ICurrentUser
{
    public Guid? TenantId => Read(Host.Api.Auth.SessionControl.TenantClaim);
    public Guid? FacilityId => Read(Host.Api.Auth.SessionControl.FacilityClaim);
    public Guid? UserId => Read(ClaimTypes.NameIdentifier);

    Guid? Read(string claim) => Guid.TryParse(accessor.HttpContext?.User.FindFirst(claim)?.Value, out var id) ? id : null;
}

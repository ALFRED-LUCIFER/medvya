using System.Security.Claims;
using Medvya.BuildingBlocks.Domain;

namespace Host.Api.Context;

/// <summary>
/// Tenant, facility and user of the current request, from the signed-in principal.
/// shortcut: tenant_id and facility_id are placeholder claim names until the facility picker issues them (MDV-P0-012); without them both are null and tenant data stays unreachable.
/// </summary>
public sealed class RequestContext(IHttpContextAccessor accessor) : ICurrentTenant, ICurrentFacility, ICurrentUser
{
    public Guid? TenantId => Read("tenant_id");
    public Guid? FacilityId => Read("facility_id");
    public Guid? UserId => Read(ClaimTypes.NameIdentifier);

    Guid? Read(string claim) => Guid.TryParse(accessor.HttpContext?.User.FindFirst(claim)?.Value, out var id) ? id : null;
}

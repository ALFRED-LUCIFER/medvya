using System.Security.Claims;
using Medvya.BuildingBlocks.Domain;

namespace Host.Platform.Context;

/// <summary>
/// Who the organisation review's Tenancy and Audit services act for (MDV-P1-064): the console member as the user, and no tenant until a
/// decision or detail read enters one organisation on a fresh scope (<see cref="ILinkTenant"/>). With no tenant, row-level security shows
/// the review connection nothing but the queue rows.
/// </summary>
public sealed class ReviewContext(IHttpContextAccessor accessor) : ICurrentTenant, ICurrentUser, ILinkTenant
{
    public Guid? TenantId { get; private set; }
    public Guid? UserId => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public void UseLinkTenant(Guid tenantId) => TenantId = tenantId;
}

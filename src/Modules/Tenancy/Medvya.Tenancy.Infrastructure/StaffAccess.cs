using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>The active membership's role at the session's facility, through the facility directory; tenant from filters and RLS.</summary>
public sealed class StaffAccess(TenancyDbContext db, IFacilityDirectory facilities, ICurrentUser user, ICurrentFacility facility) : IStaffAccess
{
    public async Task<Access> AccessAsync(Capability capability, CancellationToken cancellationToken = default)
    {
        if (user.UserId is not { } userId || facility.FacilityId is not { } facilityId) return Access.None;
        return await facilities.ActiveRoleAsync(userId, facilityId, cancellationToken) is { } role ? RoleMatrix.Of(role, capability) : Access.None;
    }

    public Task<string?> TenantCountryAsync(CancellationToken cancellationToken = default) =>
        db.Tenants.Select(t => t.CountryCode).SingleOrDefaultAsync(cancellationToken);
}

using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>One indexed membership lookup per check; the tenant comes from the query filter and row-level security.</summary>
public sealed class StaffAccess(TenancyDbContext db, ICurrentUser user, ICurrentFacility facility) : IStaffAccess
{
    public async Task<Access> AccessAsync(Capability capability, CancellationToken cancellationToken = default)
    {
        if (user.UserId is not { } userId || facility.FacilityId is not { } facilityId) return Access.None;
        var role = await db.Memberships
            .Where(m => m.UserId == userId && m.FacilityId == facilityId && m.Status == MembershipStatus.Active)
            .Select(m => (Role?)m.Role)
            .SingleOrDefaultAsync(cancellationToken);
        return role is { } r ? RoleMatrix.Of(r, capability) : Access.None;
    }

    public Task<string?> TenantCountryAsync(CancellationToken cancellationToken = default) =>
        db.Tenants.Select(t => t.CountryCode).SingleOrDefaultAsync(cancellationToken);
}

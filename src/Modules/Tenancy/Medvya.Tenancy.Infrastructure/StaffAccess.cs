using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>The active membership's role at the session's facility, through the facility directory; tenant from filters and RLS.</summary>
public sealed class StaffAccess(TenancyDbContext db, IFacilityDirectory facilities, ICurrentUser user, ICurrentFacility facility) : IStaffAccess
{
    public Task<Access> AccessAsync(Capability capability, CancellationToken cancellationToken = default) =>
        facility.FacilityId is { } facilityId ? AccessAsync(capability, facilityId, cancellationToken) : Task.FromResult(Access.None);

    public async Task<Role?> CurrentRoleAsync(CancellationToken cancellationToken = default) =>
        user.UserId is { } userId && facility.FacilityId is { } facilityId ? await facilities.ActiveRoleAsync(userId, facilityId, cancellationToken) : null;

    public async Task<Access> AccessAsync(Capability capability, Guid facilityId, CancellationToken cancellationToken = default) =>
        (await AccessAsync([capability], facilityId, cancellationToken))[0];

    public Task<IReadOnlyList<Access>> AccessAsync(IReadOnlyList<Capability> capabilities, Guid facilityId, CancellationToken cancellationToken = default) =>
        AccessCoreAsync(capabilities, facilityId, applyState: true, cancellationToken);

    public Task<IReadOnlyList<Access>> RoleAccessAsync(IReadOnlyList<Capability> capabilities, Guid facilityId, CancellationToken cancellationToken = default) =>
        AccessCoreAsync(capabilities, facilityId, applyState: false, cancellationToken);

    async Task<IReadOnlyList<Access>> AccessCoreAsync(IReadOnlyList<Capability> capabilities, Guid facilityId, bool applyState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (user.UserId is not { } userId || await facilities.ActiveRoleAsync(userId, facilityId, cancellationToken) is not { } role)
            return [.. capabilities.Select(_ => Access.None)];
        var state = applyState ? await OrganisationStateAsync(cancellationToken) : OrganisationState.Approved;
        (bool Read, string? Value) extras = (false, null);
        var result = new List<Access>(capabilities.Count);
        foreach (var capability in capabilities)
        {
            var access = RoleMatrix.Of(role, capability);
            // A practice-defined role may add capabilities its base role lacks (MDV-P1-008); only then is the role row read, once.
            if (access == Access.None && CustomRoleRules.Extras(role).Contains(capability))
            {
                if (!extras.Read)
                    extras = (true, await db.Memberships.Where(m => m.UserId == userId && m.FacilityId == facilityId && m.Status == MembershipStatus.Active)
                        .Select(m => db.CustomRoles.Where(r => r.Id == m.CustomRoleId).Select(r => r.Capabilities).FirstOrDefault()).SingleOrDefaultAsync(cancellationToken));
                access = CustomRoleRules.Effective(role, extras.Value, capability);
            }
            result.Add(OrganisationRules.Apply(access, capability, state));
        }
        return result;
    }

    // Tenant from filters and RLS; no readable tenant row fails closed as Draft (set-up only).
    public async Task<OrganisationState> OrganisationStateAsync(CancellationToken cancellationToken = default) =>
        await db.Tenants.Select(t => (OrganisationState?)t.State).SingleOrDefaultAsync(cancellationToken) ?? OrganisationState.Draft;

    public Task<string?> TenantCountryAsync(CancellationToken cancellationToken = default) =>
        db.Tenants.Select(t => t.CountryCode).SingleOrDefaultAsync(cancellationToken);
}

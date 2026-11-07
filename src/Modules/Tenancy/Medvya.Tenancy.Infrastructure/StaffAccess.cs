using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>The active membership's role at the session's facility, through the facility directory; tenant from filters and RLS.</summary>
public sealed class StaffAccess(TenancyDbContext db, IFacilityDirectory facilities, ICurrentUser user, ICurrentFacility facility, TimeProvider? clock = null) : IStaffAccess
{
    public Task<Access> AccessAsync(Capability capability, CancellationToken cancellationToken = default) =>
        facility.FacilityId is { } facilityId ? AccessAsync(capability, facilityId, cancellationToken) : Task.FromResult(Access.None);

    public async Task<Role?> CurrentRoleAsync(CancellationToken cancellationToken = default) =>
        user.UserId is { } userId && facility.FacilityId is { } facilityId ? await facilities.ActiveRoleAsync(userId, facilityId, cancellationToken) : null;

    public async Task<Access> AccessAsync(Capability capability, Guid facilityId, CancellationToken cancellationToken = default) =>
        (await AccessAsync([capability], facilityId, cancellationToken))[0];

    public async Task<IReadOnlyList<Access>> AccessAsync(IReadOnlyList<Capability> capabilities, Guid facilityId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (user.UserId is not { } userId || await facilities.ActiveRoleAsync(userId, facilityId, cancellationToken) is not { } role)
            return [.. capabilities.Select(_ => Access.None)];
        var pending = await LicencePendingAsync(userId, facilityId, cancellationToken);
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
            result.Add(LicenceRules.Apply(access, capability, pending));
        }
        return result;
    }

    public Task<bool> LicencePendingAsync(CancellationToken cancellationToken = default) =>
        user.UserId is not { } userId || facility.FacilityId is not { } facilityId ? Task.FromResult(false) : LicencePendingAsync(userId, facilityId, cancellationToken);

    Task<bool> LicencePendingAsync(Guid userId, Guid facilityId, CancellationToken cancellationToken) =>
        // A doctor with no verified licence is gated, including one with no licence row at all (invited by another route), so the gate
        // cannot be skipped by not submitting a licence. A verified licence past its expiry date no longer counts (MDV-P1-010).
        db.Memberships.AnyAsync(m => m.UserId == userId && m.FacilityId == facilityId && m.Status == MembershipStatus.Active
            && (m.Role == Role.SoloDoctor || m.Role == Role.HospitalDoctor)
            && !db.DoctorLicences.Any(l => l.MembershipId == m.Id && l.VerifiedAt != null && (l.ExpiresOn == null || l.ExpiresOn >= Today)), cancellationToken);

    DateOnly Today => DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);

    public Task<string?> TenantCountryAsync(CancellationToken cancellationToken = default) =>
        db.Tenants.Select(t => t.CountryCode).SingleOrDefaultAsync(cancellationToken);
}

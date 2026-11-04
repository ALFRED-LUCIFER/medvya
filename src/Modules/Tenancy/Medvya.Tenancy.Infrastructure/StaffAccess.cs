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
        if (await facilities.ActiveRoleAsync(userId, facilityId, cancellationToken) is not { } role) return Access.None;
        return LicenceRules.Apply(RoleMatrix.Of(role, capability), capability, await LicencePendingAsync(cancellationToken));
    }

    public Task<bool> LicencePendingAsync(CancellationToken cancellationToken = default) =>
        user.UserId is not { } userId || facility.FacilityId is not { } facilityId ? Task.FromResult(false)
        // A doctor with no verified licence is gated, including one with no licence row at all (invited by another route), so the gate
        // cannot be skipped by not submitting a licence.
        : db.Memberships.AnyAsync(m => m.UserId == userId && m.FacilityId == facilityId && m.Status == MembershipStatus.Active
            && (m.Role == Role.SoloDoctor || m.Role == Role.HospitalDoctor)
            && !db.DoctorLicences.Any(l => l.MembershipId == m.Id && l.VerifiedAt != null), cancellationToken);

    public Task<string?> TenantCountryAsync(CancellationToken cancellationToken = default) =>
        db.Tenants.Select(t => t.CountryCode).SingleOrDefaultAsync(cancellationToken);
}

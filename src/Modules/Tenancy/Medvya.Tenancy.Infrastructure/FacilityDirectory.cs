using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

public sealed class FacilityDirectory(TenancyDbContext db) : IFacilityDirectory
{
    static readonly Role[] Doctors = [Role.SoloDoctor, Role.HospitalDoctor];

    public Task<FacilityInfo?> FindAsync(Guid facilityId, CancellationToken cancellationToken = default) =>
        db.Facilities.AsNoTracking().Where(f => f.Id == facilityId)
            .Select(f => new FacilityInfo(f.Id, f.Name, f.TimeZone)).SingleOrDefaultAsync(cancellationToken);

    public Task<Role?> ActiveRoleAsync(Guid userId, Guid facilityId, CancellationToken cancellationToken = default) =>
        db.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.FacilityId == facilityId && m.Status == MembershipStatus.Active)
            .Select(m => (Role?)m.Role).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ActiveDoctorsAsync(Guid facilityId, CancellationToken cancellationToken = default) =>
        await db.Memberships.AsNoTracking()
            .Where(m => m.FacilityId == facilityId && m.Status == MembershipStatus.Active && Doctors.Contains(m.Role))
            .Select(m => m.UserId!.Value).ToListAsync(cancellationToken); // Active memberships always have a user (CK_Memberships_UserUnlessInvited)
}

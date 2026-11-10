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

    public async Task<PrescriberInfo?> PrescriberAsync(Guid userId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        var row = await (
            from m in db.Memberships.AsNoTracking()
            join p in db.DoctorProfiles.AsNoTracking() on m.Id equals p.MembershipId
            join l in db.DoctorLicences.AsNoTracking() on m.Id equals l.MembershipId
            where m.UserId == userId && m.FacilityId == facilityId
            select new { p.FullName, p.Qualifications, l.Number, l.Authority }).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        // The authority by its name in the country's list (the code is what is stored); an unknown code prints as stored.
        var country = await db.Tenants.AsNoTracking().Select(t => t.CountryCode).SingleAsync(cancellationToken);
        var name = OnboardingRules.For(country)?.Authorities.FirstOrDefault(a => a.Code == row.Authority)?.Name ?? row.Authority;
        return new PrescriberInfo(row.FullName, row.Qualifications, row.Number, name);
    }

    public async Task<IReadOnlyDictionary<DateOnly, string>> ClosuresAsync(Guid facilityId, DateOnly first, DateOnly last, CancellationToken cancellationToken = default) =>
        await db.Closures.AsNoTracking().Where(c => c.FacilityId == facilityId && c.Date >= first && c.Date <= last)
            .ToDictionaryAsync(c => c.Date, c => c.Name, cancellationToken);
}

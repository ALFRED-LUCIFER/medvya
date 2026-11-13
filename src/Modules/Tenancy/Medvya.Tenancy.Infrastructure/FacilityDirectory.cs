using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

public sealed class FacilityDirectory(TenancyDbContext db, ILogoStore store) : IFacilityDirectory
{
    static readonly Role[] Doctors = [Role.SoloDoctor, Role.HospitalDoctor];

    public async Task<FacilityInfo?> FindAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        var facility = await db.Facilities.AsNoTracking().Where(f => f.Id == facilityId).Select(f => new { f.Id, f.Name, f.TimeZone }).SingleOrDefaultAsync(cancellationToken);
        if (facility is null) return null;
        // The tenant filter and row-level security leave exactly this tenant's row.
        var tenant = await db.Tenants.AsNoTracking().Select(t => new { t.Name, t.QueueOrder, t.BookedFirst }).SingleAsync(cancellationToken);
        return new FacilityInfo(facility.Id, facility.Name, facility.TimeZone, tenant.Name, new(tenant.QueueOrder, tenant.BookedFirst));
    }

    public Task<Role?> ActiveRoleAsync(Guid userId, Guid facilityId, CancellationToken cancellationToken = default) =>
        db.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.FacilityId == facilityId && m.Status == MembershipStatus.Active)
            .Select(m => (Role?)m.Role).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ActiveDoctorsAsync(Guid facilityId, CancellationToken cancellationToken = default) =>
        await db.Memberships.AsNoTracking()
            .Where(m => m.FacilityId == facilityId && m.Status == MembershipStatus.Active && Doctors.Contains(m.Role))
            .Select(m => m.UserId!.Value).ToListAsync(cancellationToken); // Active memberships always have a user (CK_Memberships_UserUnlessInvited)

    public async Task<PrescriberInfo?> PrescriberAsync(Guid userId, Guid facilityId, CancellationToken cancellationToken = default) =>
        (await PrescribersAsync(facilityId, [userId], cancellationToken)).GetValueOrDefault(userId);

    public async Task<PrintImages> PrintImagesAsync(Guid userId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        var logo = await db.Profiles.AsNoTracking().Select(p => p.LogoKey).SingleOrDefaultAsync(cancellationToken);
        var signature = await (from m in db.Memberships.AsNoTracking()
                               join p in db.DoctorProfiles.AsNoTracking() on m.Id equals p.MembershipId
                               where m.FacilityId == facilityId && m.UserId == userId
                               select p.SignatureKey).SingleOrDefaultAsync(cancellationToken);
        return new(logo is null ? null : await store.ReadAsync(logo, cancellationToken), signature is null ? null : await store.ReadAsync(signature, cancellationToken));
    }

    public async Task<IReadOnlyDictionary<Guid, PrescriberInfo>> PrescribersAsync(Guid facilityId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from m in db.Memberships.AsNoTracking()
            join p in db.DoctorProfiles.AsNoTracking() on m.Id equals p.MembershipId
            join l in db.DoctorLicences.AsNoTracking() on m.Id equals l.MembershipId
            where m.FacilityId == facilityId && userIds.Contains(m.UserId!.Value)
            select new { UserId = m.UserId!.Value, p.FullName, p.Qualifications, l.Number, l.Authority }).ToListAsync(cancellationToken);
        if (rows.Count == 0) return new Dictionary<Guid, PrescriberInfo>();
        // The authority by its name in the country's list (the code is what is stored); an unknown code prints as stored.
        var country = await db.Tenants.AsNoTracking().Select(t => t.CountryCode).SingleAsync(cancellationToken);
        var authorities = OnboardingRules.For(country)?.Authorities;
        return rows.ToDictionary(r => r.UserId, r => new PrescriberInfo(r.FullName, r.Qualifications, r.Number, authorities?.FirstOrDefault(a => a.Code == r.Authority)?.Name ?? r.Authority));
    }

    public async Task<IReadOnlyDictionary<DateOnly, string>> ClosuresAsync(Guid facilityId, DateOnly first, DateOnly last, CancellationToken cancellationToken = default) =>
        await db.Closures.AsNoTracking().Where(c => c.FacilityId == facilityId && c.Date >= first && c.Date <= last)
            .ToDictionaryAsync(c => c.Date, c => c.Name, cancellationToken);
}

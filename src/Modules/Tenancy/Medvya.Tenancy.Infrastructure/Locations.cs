using System.Data.Common;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// A person works at several locations through one membership per location (MDV-P1-071): same user, role and custom role, so the
/// access rule stays "an active membership at that facility" and every module's per-facility scope is unchanged.
/// </summary>
static class Locations
{
    /// <summary>
    /// Adds <paramref name="source"/>'s person to <paramref name="facilityId"/> (staged, not saved): a new Active membership, or the offboarded one
    /// there made Active again. A doctor gets their own profile and licence record there, copied, so fees and the doctor list work per location; the
    /// pair goes in <paramref name="doctors"/> for <see cref="CopyFeesAsync"/> once the rows are saved.
    /// The caller has checked the facility and the granter's rights, and holds the plan lock if a seat is taken (none is: the person already has one).
    /// </summary>
    // shortcut: the signature image is not copied (one Blob key per profile); the doctor uploads it again at the new location, until a shared signature exists.
    public static async Task<Membership> AddAsync(TenancyDbContext db, Membership source, Guid facilityId, ICollection<(Guid From, Guid To)> doctors, CancellationToken cancellationToken)
    {
        var existing = await db.Memberships.SingleOrDefaultAsync(m => m.UserId == source.UserId && m.FacilityId == facilityId, cancellationToken);
        if (existing is not null)
        {
            existing.Status = MembershipStatus.Active;
            (existing.Role, existing.CustomRoleId, existing.IsOwner) = (source.Role, source.CustomRoleId, source.IsOwner);
            return existing;
        }
        var added = new Membership
        {
            Id = Guid.CreateVersion7(), TenantId = source.TenantId, UserId = source.UserId, FacilityId = facilityId, Role = source.Role,
            Status = MembershipStatus.Active, CustomRoleId = source.CustomRoleId, IsOwner = source.IsOwner, // the owner is the owner at every location (MDV-P1-107)
        };
        db.Memberships.Add(added);
        if (await db.DoctorProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.MembershipId == source.Id, cancellationToken) is { } p)
        {
            db.DoctorProfiles.Add(new DoctorProfile
            {
                MembershipId = added.Id, TenantId = p.TenantId, FullName = p.FullName, Specialty = p.Specialty, Qualifications = p.Qualifications,
                Languages = p.Languages, ChangedBy = p.ChangedBy, ChangedAt = p.ChangedAt,
            });
            doctors.Add((source.Id, added.Id));
        }
        if (await db.DoctorLicences.AsNoTracking().SingleOrDefaultAsync(l => l.MembershipId == source.Id, cancellationToken) is { } l)
            db.DoctorLicences.Add(new DoctorLicence
            {
                MembershipId = added.Id, TenantId = l.TenantId, Authority = l.Authority, Number = l.Number, Category = l.Category, ExpiresOn = l.ExpiresOn,
                CertificateName = l.CertificateName, SubmittedAt = l.SubmittedAt, AttestedBy = l.AttestedBy, AttestedAt = l.AttestedAt,
            });
        return added;
    }

    /// <summary>Gives each doctor added at another location by <see cref="AddAsync"/> the fees they have where they came from, in the caller's transaction (MDV-P4-002).</summary>
    public static async Task CopyFeesAsync(IDoctorFees? fees, IReadOnlyCollection<(Guid From, Guid To)> doctors, DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (doctors.Count == 0) return;
        if (fees is null) throw new InvalidOperationException("Doctor fees (the Billing module) are not registered.");
        foreach (var (from, to) in doctors) await fees.CopyAsync(from, to, transaction, cancellationToken);
    }
}

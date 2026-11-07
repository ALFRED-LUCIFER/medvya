using Medvya.Audit.Application;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// The quarterly access review (MDV-P1-010). The review of a quarter is opened the first time it is read; it is complete once every
/// active member (a person who has joined, not a pending invitee) has a decision. Each decision, and the completion, is saved with its
/// audit event in one transaction (action access_review.confirm / .offboard / .complete).
/// shortcut: a member added after the review completed is not asked about until next quarter; reopen the review when a new membership is created if the owner needs that.
/// </summary>
public sealed class AccessReviews(TenancyDbContext db, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock, IStaffAccess access) : IAccessReviews
{
    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<AccessReviewView> CurrentAsync(CancellationToken cancellationToken = default)
    {
        var review = await OpenAsync(cancellationToken);
        var members = await (
            from m in db.Memberships.AsNoTracking()
            where m.Status == MembershipStatus.Active && m.UserId != null && m.Role != Role.Patient
            join f in db.Facilities on m.FacilityId equals f.Id
            orderby f.Name, m.Role
            select new AccessReviewMember(m.Id, m.UserId!.Value, m.Role, f.Id, f.Name,
                db.AccessReviewDecisions.Where(d => d.ReviewId == review.Id && d.MembershipId == m.Id).Select(d => (AccessDecision?)d.Decision).FirstOrDefault())).ToListAsync(cancellationToken);
        var last = await db.AccessReviews.AsNoTracking().Where(r => r.CompletedAt != null).MaxAsync(r => r.CompletedAt, cancellationToken);
        return new AccessReviewView(review.Quarter, review.CompletedAt, last, members);
    }

    public async Task<bool> ConfirmAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        var facilityId = await db.Memberships.Where(m => m.Id == membershipId && m.Status == MembershipStatus.Active && m.UserId != null && m.Role != Role.Patient)
            .Select(m => (Guid?)m.FacilityId).SingleOrDefaultAsync(cancellationToken);
        // Confirmed only by someone who administers staff at that member's facility (#877).
        if (facilityId is null || await access.AccessAsync(Capability.StaffRolesIntegrations, facilityId.Value, cancellationToken) != Access.Config) return false;
        await DecideAsync(membershipId, AccessDecision.Confirmed, cancellationToken);
        return true;
    }

    public Task RecordOffboardedAsync(Guid membershipId, CancellationToken cancellationToken = default) => DecideAsync(membershipId, AccessDecision.Offboarded, cancellationToken);

    async Task DecideAsync(Guid membershipId, AccessDecision decision, CancellationToken ct)
    {
        var review = await OpenAsync(ct);
        var now = clock.GetUtcNow();
        if (await db.AccessReviewDecisions.AnyAsync(d => d.ReviewId == review.Id && d.MembershipId == membershipId, ct)) return; // the first decision stands
        db.AccessReviewDecisions.Add(new AccessReviewDecision { ReviewId = review.Id, MembershipId = membershipId, TenantId = TenantId, Decision = decision, DecidedBy = user.UserId, DecidedAt = now });
        await db.SaveChangesAsync(ct); // so the completeness check below sees it; the audited save follows
        var open = await db.Memberships.AnyAsync(m => m.Status == MembershipStatus.Active && m.UserId != null && m.Role != Role.Patient
            && !db.AccessReviewDecisions.Any(d => d.ReviewId == review.Id && d.MembershipId == m.Id), ct);
        var completes = !open && review.CompletedAt is null;
        if (completes) review.CompletedAt = now;
        var action = completes ? "access_review.complete" : decision == AccessDecision.Confirmed ? "access_review.confirm" : "access_review.offboard";
        await SettingsAudit.SaveAsync(db, audit, TenantId, user.UserId, now, action, "access_review", membershipId, null, new { review.Quarter, decision = decision.ToString(), completed = completes }, ct);
    }

    async Task<AccessReview> OpenAsync(CancellationToken ct)
    {
        var quarter = AccessReviewQuarter.Of(Today);
        if (await db.AccessReviews.SingleOrDefaultAsync(r => r.Quarter == quarter, ct) is { } existing) return existing;
        var review = new AccessReview { Id = Guid.CreateVersion7(), TenantId = TenantId, Quarter = quarter, StartedAt = clock.GetUtcNow() };
        db.AccessReviews.Add(review);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two people opened it at once (unique index on tenant and quarter): use the one that won.
            db.Entry(review).State = EntityState.Detached;
            return await db.AccessReviews.SingleAsync(r => r.Quarter == quarter, ct);
        }
        return review;
    }
}

/// <summary>What the compliance reminders read from one tenant (MDV-P1-010); the Notifications module turns it into notifications.</summary>
public sealed class ComplianceFactsReader(TenancyDbContext db) : IComplianceFacts
{
    /// <summary>Reminders start this long before a licence expires (the largest of 60/30/7/0).</summary>
    const int HorizonDays = 60;

    public async Task<ComplianceFacts> ReadAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        var horizon = today.AddDays(HorizonDays);
        var licences = await (
            from l in db.DoctorLicences.AsNoTracking()
            join m in db.Memberships on l.MembershipId equals m.Id
            where m.Status == MembershipStatus.Active && l.VerifiedAt != null && l.ExpiresOn != null && l.ExpiresOn <= horizon
            select new ExpiringLicence(l.MembershipId, m.FacilityId, l.ExpiresOn!.Value)).ToListAsync(cancellationToken);
        var quarter = AccessReviewQuarter.Of(today);
        var done = await db.AccessReviews.AnyAsync(r => r.Quarter == quarter && r.CompletedAt != null, cancellationToken);
        var facilities = await db.Facilities.AsNoTracking().Select(f => f.Id).ToListAsync(cancellationToken);
        var clinic = await db.Tenants.AsNoTracking().Select(t => t.Name).SingleAsync(cancellationToken);
        var contact = await db.Profiles.AsNoTracking().Select(p => p.Email).SingleOrDefaultAsync(cancellationToken);
        return new ComplianceFacts(clinic, contact, licences, done ? null : quarter, facilities);
    }

    public async Task<IReadOnlyList<Guid>> TenantsAsync(CancellationToken cancellationToken = default) =>
        await db.TenantIndex.AsNoTracking().Select(t => t.TenantId).ToListAsync(cancellationToken);
}

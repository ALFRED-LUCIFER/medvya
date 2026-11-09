using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// The one place the plan cap lives (MDV-P1-069): 1 doctor and 2 other staff per organisation, in total across its locations.
/// Patients are never counted. A seat is held by an active or suspended member and by an invited member whose invitation is not revoked;
/// offboarding or revoking frees it.
/// </summary>
// shortcut: one hard-coded plan, and one person at two locations counts twice (a membership is per facility); replaced by entitlements when MDV-P5-042 lands.
static class StarterPlan
{
    const int Doctors = 1, Staff = 2;

    /// <summary>Call inside the transaction that adds the seat, before saving it. The per-tenant advisory lock makes parallel adds count one after the other.</summary>
    public static async Task EnsureRoomAsync(TenancyDbContext db, Guid tenantId, Role adding, CancellationToken cancellationToken)
    {
        var key = $"plan:{tenantId}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        var held = await db.Memberships.Where(m => m.Role != Role.Patient && (m.Status == MembershipStatus.Active || m.Status == MembershipStatus.Suspended
                || (m.Status == MembershipStatus.Invited && db.Invitations.Any(i => i.MembershipId == m.Id && i.RevokedAt == null))))
            .Select(m => m.Role).ToListAsync(cancellationToken);
        var doctors = held.Count(IsDoctor);
        if (IsDoctor(adding) ? doctors >= Doctors : held.Count - doctors >= Staff) throw new PlanLimitException();
    }

    static bool IsDoctor(Role role) => role is Role.SoloDoctor or Role.HospitalDoctor;
}

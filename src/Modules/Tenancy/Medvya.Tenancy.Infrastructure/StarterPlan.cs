using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// The one place the plan cap lives (MDV-P1-069): 1 doctor and 2 other staff per organisation, in total across its locations.
/// Patients are never counted. A seat is held by an active or suspended member and by an invited member whose invitation is not revoked;
/// offboarding or revoking frees it. A person is one seat however many locations they work at (a membership is per location, MDV-P1-071).
/// </summary>
// shortcut: one hard-coded plan; replaced by entitlements when MDV-P5-042 lands.
static class StarterPlan
{
    const int Doctors = 1, Staff = 2;
    // Test seam, internal and keyed by tenant id so parallel test classes never see each other's cap. Empty in production: nothing calls it.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> Raised = new();
    internal static void RaiseCapForTests(Guid tenantId) => Raised[tenantId] = true;

    /// <summary>Call inside the transaction that adds the seat, before saving it. The per-tenant advisory lock makes parallel adds count one after the other.</summary>
    public static async Task EnsureRoomAsync(TenancyDbContext db, Guid tenantId, Role adding, CancellationToken cancellationToken, Guid? person = null)
    {
        if (Raised.ContainsKey(tenantId)) return;
        var key = $"plan:{tenantId}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        var held = await db.Memberships.Where(m => m.Role != Role.Patient && (m.Status == MembershipStatus.Active || m.Status == MembershipStatus.Suspended
                || (m.Status == MembershipStatus.Invited && db.Invitations.Any(i => i.MembershipId == m.Id && i.RevokedAt == null))))
            .Select(m => new { m.Role, m.UserId }).ToListAsync(cancellationToken);
        // The person already holds a seat (another location, or coming back to one): no new seat.
        if (person is { } p && held.Any(m => m.UserId == p)) return;
        // One seat per signed-in person; an invitee has no user yet and is their own seat.
        var seats = held.Where(m => m.UserId is null).Select(m => m.Role).Concat(held.Where(m => m.UserId is not null).GroupBy(m => m.UserId).Select(g => g.First().Role)).ToList();
        var doctors = seats.Count(IsDoctor);
        if (IsDoctor(adding) ? doctors >= Doctors : seats.Count - doctors >= Staff) throw new PlanLimitException();
    }

    static bool IsDoctor(Role role) => role is Role.SoloDoctor or Role.HospitalDoctor;
}

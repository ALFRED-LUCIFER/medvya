using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// The one place the plan cap lives (MDV-P1-069): 1 doctor and 2 other staff per organisation unless the environment sets other numbers
/// (settings Plan:Doctors and Plan:Staff, checked at start-up), in total across its locations.
/// Patients are never counted. A seat is held by an active or suspended member and by an invited member whose invitation is not revoked;
/// offboarding or revoking frees it. A person is one seat however many locations they work at (a membership is per location, MDV-P1-071).
/// </summary>
// shortcut: one plan for every organisation of an environment, set by configuration; the console has versioned plans now (MDV-P5-042, ADR-019),
// but they reach the clinic database only with the per-country write path of MDV-P5-041. Replace this with the organisation's plan entitlements then.
public sealed class StarterPlan
{
    public int Doctors { get; set; } = 1;
    public int Staff { get; set; } = 2;
    // Test seam, internal and keyed by tenant id so parallel test classes never see each other's cap. Empty in production: nothing calls it.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> Raised = new();
    internal static void RaiseCapForTests(Guid tenantId) => Raised[tenantId] = true;

    /// <summary>Call inside the transaction that adds the seat, before saving it. The per-tenant advisory lock makes parallel adds count one after the other.</summary>
    public async Task EnsureRoomAsync(TenancyDbContext db, Guid tenantId, Role adding, CancellationToken cancellationToken, Guid? person = null)
    {
        if (Raised.ContainsKey(tenantId)) return;
        var key = $"plan:{tenantId}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        var (doctors, others, holds) = await HeldAsync(db, person, cancellationToken);
        // The person already holds a seat (another location, or coming back to one): no new seat.
        if (holds) return;
        if (IsDoctor(adding) ? doctors >= Doctors : others >= Staff) throw new PlanLimitException(Doctors, Staff);
    }

    /// <summary>
    /// The plan's numbers and whether one more doctor, or one more member of staff, still fits (#1365 F-039): the count the refusal uses, so
    /// a wizard can say "no seat" at its first step. Not a reservation: the add checks again under the lock.
    /// </summary>
    public async Task<PlanSeats> SeatsAsync(TenancyDbContext db, Guid tenantId, CancellationToken cancellationToken)
    {
        if (Raised.ContainsKey(tenantId)) return new(Doctors, Staff, true, true);
        var (doctors, others, _) = await HeldAsync(db, null, cancellationToken);
        return new(Doctors, Staff, doctors < Doctors, others < Staff);
    }

    static async Task<(int Doctors, int Others, bool PersonHolds)> HeldAsync(TenancyDbContext db, Guid? person, CancellationToken cancellationToken)
    {
        var held = await db.Memberships.Where(m => m.Role != Role.Patient && (m.Status == MembershipStatus.Active || m.Status == MembershipStatus.Suspended
                || (m.Status == MembershipStatus.Invited && db.Invitations.Any(i => i.MembershipId == m.Id && i.RevokedAt == null))))
            .Select(m => new { m.Role, m.UserId }).ToListAsync(cancellationToken);
        // One seat per signed-in person; an invitee has no user yet and is their own seat.
        var seats = held.Where(m => m.UserId is null).Select(m => m.Role).Concat(held.Where(m => m.UserId is not null).GroupBy(m => m.UserId).Select(g => g.First().Role)).ToList();
        var doctors = seats.Count(IsDoctor);
        return (doctors, seats.Count - doctors, person is { } p && held.Any(m => m.UserId == p));
    }

    static bool IsDoctor(Role role) => role is Role.SoloDoctor or Role.HospitalDoctor;
}

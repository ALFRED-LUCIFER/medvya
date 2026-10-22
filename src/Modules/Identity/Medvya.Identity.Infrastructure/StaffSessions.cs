using Medvya.Identity.Application;
using Medvya.Identity.Domain;
using Medvya.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Identity.Infrastructure;

/// <summary>
/// One primary-key read per request, no cache, so a revocation is seen by the very next request.
/// shortcut: last-seen is written at most once a minute per session, so idle timeout is accurate to about a minute; add a &lt;= 60 s read cache if the lookup shows in latency.
/// </summary>
sealed class StaffSessions(IdentityDbContext db) : IStaffSessions
{
    static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    public async Task<StaffSession> StartAsync(Guid userId, string? userAgent, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new StaffSession { Id = Guid.CreateVersion7(), UserId = userId, Device = StaffSession.DeviceLabel(userAgent), CreatedAt = now, LastSeenAt = now };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<StaffSession?> ValidateAsync(Guid sessionId, Guid userId, bool activity, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken);
        if (session is null || !session.IsActive(now)) return null;
        if (activity && now - session.LastSeenAt >= TouchInterval)
        {
            await db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeenAt, now), cancellationToken);
            session.LastSeenAt = now;
        }
        return session;
    }

    public Task ScopeAsync(Guid sessionId, Guid tenantId, Guid membershipId, int idleTimeoutMinutes, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.TenantId, tenantId)
            .SetProperty(s => s.MembershipId, membershipId)
            .SetProperty(s => s.IdleTimeoutMinutes, idleTimeoutMinutes)
            .SetProperty(s => s.LastSeenAt, now), cancellationToken);
    }

    public async Task<IReadOnlyList<StaffSession>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - StaffSession.MaxLifetime;
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.CreatedAt > cutoff)
            .OrderByDescending(s => s.CreatedAt).ToListAsync(cancellationToken);
        return sessions.Where(s => s.IsActive(now)).ToList();
    }

    public async Task<StaffSession?> RevokeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken);
        if (session is null || !session.IsActive(now)) return null;
        session.RevokedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public Task<int> RevokeInTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return db.Sessions.Where(s => s.UserId == userId && s.TenantId == tenantId && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), cancellationToken);
    }
}

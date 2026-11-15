using System.Security.Cryptography;
using Medvya.Identity.Application;
using Medvya.Identity.Domain;
using Medvya.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Identity.Infrastructure;

/// <summary>One primary-key read per request, no cache, so a sign-out or a replay is seen by the very next request (as <see cref="StaffSessions"/>).</summary>
sealed class PatientSessions(IdentityDbContext db, TimeProvider clock) : IPatientSessions
{
    static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    public async Task StartAsync(Guid sessionId, byte[] phoneHash, string device, byte[] refreshTokenHash, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        db.PatientSessions.Add(new PatientSession { Id = sessionId, PhoneHash = phoneHash, Device = device, CreatedAt = now, LastSeenAt = now, RefreshTokenHash = refreshTokenHash });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PatientSession?> ValidateAsync(Guid sessionId, bool activity, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var session = await db.PatientSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null || !session.IsActive(now)) return null;
        if (activity && now - session.LastSeenAt >= TouchInterval)
        {
            await db.PatientSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeenAt, now), cancellationToken);
            session.LastSeenAt = now;
        }
        return session;
    }

    public async Task<RefreshRotation> RotateRefreshAsync(Guid sessionId, byte[] current, byte[] replacement, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var session = await db.PatientSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null || !session.IsActive(now)) return RefreshRotation.Refused;
        // Compared in SQL on the stored value, so two concurrent uses of one token cannot both succeed.
        if (await db.PatientSessions.Where(s => s.Id == sessionId && s.RevokedAt == null && s.RefreshTokenHash == current)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.PreviousRefreshTokenHash, current).SetProperty(s => s.RefreshTokenHash, replacement)
                    .SetProperty(s => s.LastSeenAt, now), cancellationToken) == 1)
            return RefreshRotation.Rotated;
        // The token the last rotation replaced came back: someone holds a copy. Anything else was never issued (a forgery or garbage).
        var previous = await db.PatientSessions.AsNoTracking().Where(s => s.Id == sessionId).Select(s => s.PreviousRefreshTokenHash).SingleOrDefaultAsync(cancellationToken);
        return previous is not null && CryptographicOperations.FixedTimeEquals(previous, current) ? RefreshRotation.Replayed : RefreshRotation.Refused;
    }

    public Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        return db.PatientSessions.Where(s => s.Id == sessionId && s.RevokedAt == null).ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), cancellationToken);
    }
}

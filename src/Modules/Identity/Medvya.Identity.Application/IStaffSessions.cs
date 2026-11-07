using Medvya.Identity.Domain;

namespace Medvya.Identity.Application;

/// <summary>Server-side staff sessions (MDV-P0-012): checked on every request, so revocation and idle timeout take effect at once.</summary>
public interface IStaffSessions
{
    Task<StaffSession> StartAsync(Guid userId, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>The session if it is still active, else null. <paramref name="activity"/> false (background polls) does not extend the idle timeout.</summary>
    Task<StaffSession?> ValidateAsync(Guid sessionId, Guid userId, bool activity, CancellationToken cancellationToken = default);

    /// <summary>Scopes the session to a membership (facility picker); the tenant's idle timeout applies from now on.</summary>
    Task ScopeAsync(Guid sessionId, Guid tenantId, Guid membershipId, int idleTimeoutMinutes, CancellationToken cancellationToken = default);

    /// <summary>The user's live sessions, newest first.</summary>
    Task<IReadOnlyList<StaffSession>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Revokes one of the user's sessions; returns the revoked session (for its tenant), or null if it is not theirs or already over.</summary>
    Task<StaffSession?> RevokeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Swaps the native refresh-token hash from <paramref name="current"/> (null: the browser session becomes native) to
    /// <paramref name="replacement"/> on a live session. Conditional, so of two uses of the same token only one wins; false otherwise.
    /// </summary>
    Task<bool> RotateRefreshAsync(Guid sessionId, byte[]? current, byte[] replacement, CancellationToken cancellationToken = default);

    /// <summary>When each of these users last signed in (the newest session start, live or ended); users who never did are left out.</summary>
    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LastSignInAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>Revokes every live session of the user that is scoped to the tenant (admin action); returns how many.</summary>
    Task<int> RevokeInTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}

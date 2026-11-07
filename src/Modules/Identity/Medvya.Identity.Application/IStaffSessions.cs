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
    /// <paramref name="replacement"/> on a live session, and counts it as activity. Conditional, so of two uses of the same token only one
    /// wins. <see cref="RefreshRotation.Replayed"/> only when <paramref name="current"/> is the token the last rotation replaced.
    /// </summary>
    Task<RefreshRotation> RotateRefreshAsync(Guid sessionId, byte[]? current, byte[] replacement, CancellationToken cancellationToken = default);

    /// <summary>When each of these users last signed in (the newest session start, live or ended); users who never did are left out.</summary>
    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LastSignInAsync(IReadOnlyCollection<Guid> userIds, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>The membership the user's newest scoped session was scoped to (live or ended), for "last used" on the facility picker.</summary>
    Task<Guid?> LastMembershipAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Revokes every live session of the user that is scoped to the tenant (admin action); returns how many.</summary>
    Task<int> RevokeInTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the user's sessions in the tenant that are on <paramref name="membershipId"/> or on no membership yet (offboarding one facility,
    /// #877): a session at the person's other facility of the same practice keeps working.
    /// </summary>
    Task<int> RevokeForMembershipAsync(Guid userId, Guid tenantId, Guid membershipId, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a refresh-token rotation (#883): a replay ends the session; a token that was never issued changes nothing.</summary>
public enum RefreshRotation { Rotated, Replayed, Refused }

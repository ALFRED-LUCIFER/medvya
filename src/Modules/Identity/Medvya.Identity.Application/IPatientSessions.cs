using Medvya.Identity.Domain;

namespace Medvya.Identity.Application;

/// <summary>
/// Patient app sessions after a right SMS code (MDV-P5-033, ADR-020): one row per signed-in phone, checked on every request, with the staff
/// app's refresh rotation. Every call names the session; nothing here reaches a clinic or a patient record.
/// </summary>
public interface IPatientSessions
{
    /// <summary>Starts a session for the verified number with its first refresh-token hash.</summary>
    Task StartAsync(Guid sessionId, byte[] phoneHash, string device, byte[] refreshTokenHash, CancellationToken cancellationToken = default);

    /// <summary>The session if it is still active, else null. <paramref name="activity"/> false does not extend the idle timeout.</summary>
    Task<PatientSession?> ValidateAsync(Guid sessionId, bool activity, CancellationToken cancellationToken = default);

    /// <summary>As <see cref="IStaffSessions.RotateRefreshAsync"/>: conditional, so of two uses of one token only one wins.</summary>
    Task<RefreshRotation> RotateRefreshAsync(Guid sessionId, byte[] current, byte[] replacement, CancellationToken cancellationToken = default);

    /// <summary>Ends the session (sign-out, a replayed refresh token).</summary>
    Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

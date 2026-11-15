using Medvya.BuildingBlocks.Domain;

namespace Medvya.Identity.Domain;

/// <summary>
/// The patient app's session after a right SMS code (MDV-P5-033, ADR-020). The patient is the verified mobile number, known only by its hash:
/// no clinic, no tenant and no patient record, so a patient session reaches no clinic or patient data (linking records is MDV-P5-036). Refresh
/// rotation, replay and lifetimes are the staff app's (<see cref="StaffSession"/>). Not row-level secured, like staff sessions: every query names
/// the session.
/// shortcut: ended rows are kept forever, as for staff sessions; purge them with the data-retention jobs.
/// </summary>
public sealed class PatientSession
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    /// <summary>The number's hash, as in <see cref="SmsSignIn.PhoneHash"/>: reversible by trying every number, so personal data, never logged.</summary>
    [Classification(DataClass.Personal)] public required byte[] PhoneHash { get; init; }
    /// <summary>"Medvya app 1.2.0 on Android" (<see cref="StaffSession.AppLabel"/>), for the patient's own list of devices.</summary>
    [Classification(DataClass.Internal)] public required string Device { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset LastSeenAt { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>SHA-256 of the current refresh token.</summary>
    [Classification(DataClass.Internal)] public required byte[] RefreshTokenHash { get; set; }
    /// <summary>SHA-256 of the token the last rotation replaced: only that one coming back is a replay (#883).</summary>
    [Classification(DataClass.Internal)] public byte[]? PreviousRefreshTokenHash { get; set; }

    /// <summary>The staff app's native lifetimes: 30 days at most, 7 days unused (ADR-004, ADR-011).</summary>
    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && now < CreatedAt + StaffSession.NativeMaxLifetime && now < LastSeenAt + StaffSession.NativeIdleTimeout;
}

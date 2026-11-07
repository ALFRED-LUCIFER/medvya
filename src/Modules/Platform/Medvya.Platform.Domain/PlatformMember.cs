using Medvya.BuildingBlocks.Domain;

namespace Medvya.Platform.Domain;

/// <summary>What a Medvya team member may do in the platform console (MDV-P5-040). Never a practice role.</summary>
public enum PlatformRole { Owner, Operations, Support, Finance, Reviewer }

/// <summary>
/// A person at Medvya who may use the platform console. Bound to one company sign-on account (issuer and subject) the first time
/// they sign in with their invitation link; until then <see cref="Subject"/> is null and nobody can sign in as them.
/// </summary>
public sealed class PlatformMember
{
    public const int NameMax = 100, EmailMax = 254;

    [Classification(DataClass.Internal)] public Guid Id { get; init; } = Guid.CreateVersion7();
    [Classification(DataClass.Personal)] public required string DisplayName { get; init; }
    [Classification(DataClass.Personal)] public required string Email { get; init; }
    [Classification(DataClass.Internal)] public PlatformRole Role { get; set; }
    [Classification(DataClass.Internal)] public string? Issuer { get; set; }
    [Classification(DataClass.Internal)] public string? Subject { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset InvitedAt { get; init; }
    /// <summary>Null only for the first owner, who was configured rather than invited.</summary>
    [Classification(DataClass.Internal)] public Guid? InvitedBy { get; init; }
    /// <summary>Least access by default: access stops at the end of this day (UTC) unless renewed. Null = until removed.</summary>
    [Classification(DataClass.Internal)] public DateOnly? AccessEndsOn { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? LastActiveAt { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? RemovedAt { get; set; }

    public bool HasAccess(DateTimeOffset now) =>
        RemovedAt is null && (AccessEndsOn is not { } ends || DateOnly.FromDateTime(now.UtcDateTime) <= ends);
}

/// <summary>A registered WebAuthn security key. Revoked, never deleted, so the audit log can always name the key it refers to.</summary>
public sealed class SecurityKey
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; } = Guid.CreateVersion7();
    [Classification(DataClass.Internal)] public Guid MemberId { get; init; }
    [Classification(DataClass.Internal)] public required byte[] CredentialId { get; init; }
    /// <summary>COSE public key. Public by nature, but only useful together with the credential id.</summary>
    [Classification(DataClass.Internal)] public required byte[] PublicKey { get; init; }
    /// <summary>The authenticator's signature counter; a value that does not grow points to a cloned key.</summary>
    [Classification(DataClass.Internal)] public long SignCount { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? LastUsedAt { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? RevokedAt { get; set; }
}

public enum KeyLinkPurpose { Invitation, Recovery }

/// <summary>
/// A one-hour, single-use link that lets one member register a security key: sent with an invitation, or issued by another owner after a
/// video call when a key is lost. Only the SHA-256 of the token is stored; the token itself is shown once, to the owner who issued it.
/// </summary>
public sealed class KeyLink
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    [Classification(DataClass.Internal)] public Guid Id { get; init; } = Guid.CreateVersion7();
    [Classification(DataClass.Internal)] public Guid MemberId { get; init; }
    [Classification(DataClass.Internal)] public KeyLinkPurpose Purpose { get; init; }
    [Classification(DataClass.Internal)] public required byte[] TokenHash { get; init; }
    [Classification(DataClass.Internal)] public Guid IssuedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset IssuedAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ExpiresAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? UsedAt { get; set; }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;
}

/// <summary>
/// The server-side row behind a console cookie (ADR-004). A session starts at company sign-on with <see cref="KeyVerifiedAt"/> null:
/// it then reaches only the security-key steps. Every other endpoint needs the key step done in this session.
/// </summary>
public sealed class PlatformSession
{
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    [Classification(DataClass.Internal)] public Guid Id { get; init; } = Guid.CreateVersion7();
    [Classification(DataClass.Internal)] public Guid MemberId { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset LastSeenAt { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? KeyVerifiedAt { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>The key link this session signed on with, if any: registration reads it here, so the browser never keeps the token.</summary>
    [Classification(DataClass.Internal)] public Guid? KeyLinkId { get; init; }
    /// <summary>The pending WebAuthn ceremony (its options as JSON, with the challenge), cleared when it is answered. One at a time.</summary>
    [Classification(DataClass.Internal)] public string? PendingCeremony { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < CreatedAt + MaxLifetime && now < LastSeenAt + IdleTimeout;
}

/// <summary>
/// One row of the append-only platform audit log (the database refuses UPDATE, DELETE and TRUNCATE). Every console action carries the
/// reason its author typed; the practice an action concerns, if any, is <see cref="SubjectTenantId"/>.
/// </summary>
public sealed class PlatformAuditEntry
{
    public const int ReasonMin = 5, ReasonMax = 500;

    [Classification(DataClass.Internal)] public Guid Id { get; init; } = Guid.CreateVersion7();
    [Classification(DataClass.Internal)] public Guid? ActorMemberId { get; init; }
    /// <summary>Stable code, e.g. "team.invite", "key.recovery.issue", "auth.signin".</summary>
    [Classification(DataClass.Internal)] public required string Action { get; init; }
    [Classification(DataClass.Internal)] public required string ResourceType { get; init; }
    [Classification(DataClass.Internal)] public Guid? ResourceId { get; init; }
    [Classification(DataClass.Internal)] public Guid? SubjectTenantId { get; init; }
    /// <summary>Typed by a Medvya team member. The console never shows patient data, and the form asks for none.</summary>
    [Classification(DataClass.Internal)] public string? Reason { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset OccurredAt { get; init; }
    [Classification(DataClass.Personal)] public string? ClientIp { get; init; }
    [Classification(DataClass.Internal)] public string? CorrelationId { get; init; }
}

public static class PlatformReason
{
    /// <summary>The trimmed reason, or null when it is missing, too short or too long.</summary>
    public static string? Valid(string? reason) =>
        reason?.Trim() is { Length: >= PlatformAuditEntry.ReasonMin and <= PlatformAuditEntry.ReasonMax } r ? r : null;
}

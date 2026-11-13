using Medvya.BuildingBlocks.Domain;

namespace Medvya.Audit.Domain;

/// <summary>
/// Allowed: answered or done. Denied: refused (401/403). NotFound: nothing the caller may see there (404), e.g. another facility's
/// record. Rejected: the request was understood but not done (400/409/422: invalid input, an unresolved duplicate, a slot taken).
/// </summary>
public enum AuditOutcome { Allowed, Denied, NotFound, Rejected }

/// <summary>
/// One append-only audit record (PRD v2 P0-06). Opaque ids and fixed codes only: no names, identifiers or free text,
/// so nothing sensitive can leak through the audit trail itself.
/// </summary>
public sealed class AuditEvent
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid? ActorUserId { get; init; }
    /// <summary>Stable action code, e.g. "patient.read", "patient.update", "auth.signin", "export.create".</summary>
    [Classification(DataClass.Internal)] public required string Action { get; init; }
    /// <summary>Resource type code, e.g. "patient", "appointment".</summary>
    [Classification(DataClass.Internal)] public required string ResourceType { get; init; }
    [Classification(DataClass.Internal)] public Guid? ResourceId { get; init; }
    /// <summary>Opaque patient id when the event touches patient data.</summary>
    [Classification(DataClass.Internal)] public Guid? PatientId { get; init; }
    /// <summary>Purpose of use: one of <see cref="AuditPurpose.All"/>.</summary>
    [Classification(DataClass.Internal)] public required string Purpose { get; init; }
    [Classification(DataClass.Internal)] public AuditOutcome Outcome { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset OccurredAt { get; init; }
    /// <summary>Role code held in the session when the event happened (null on rows written before it was recorded). Never inferred later.</summary>
    [Classification(DataClass.Internal)] public string? Role { get; init; }
    [Classification(DataClass.Internal)] public Guid? FacilityId { get; init; }
    /// <summary>Codes and opaque ids only, e.g. the filters of an export. Never names or free text.</summary>
    [Classification(DataClass.Internal)] public string? Detail { get; init; }
    [Classification(DataClass.Personal)] public string? ClientIp { get; init; }
    [Classification(DataClass.Internal)] public string? CorrelationId { get; init; }
}

/// <summary>
/// Purposes of use (MDV-P2-024): every audited read and write records one, and only these exist. Operations is running the practice
/// (schedules, staff, settings); support is Medvya's own work on the tenant (provisioning, flags, terms).
/// </summary>
/// <summary>
/// Events with no tenant (a signed-in caller refused before any facility is chosen) go in one reserved "platform" chain of the same ledger:
/// same hash chain, row-level security and digests, and no practice can read it. The id is never a real tenant's.
/// </summary>
public static class PlatformTenant { public static readonly Guid Id = Guid.Empty; }

public static class AuditPurpose
{
    public const string Treatment = "treatment", Registration = "registration", Billing = "billing", Audit = "audit", Operations = "operations", Support = "support";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Treatment, Registration, Billing, Audit, Operations, Support };

    /// <summary>The purpose, or an exception for any other value: an unknown purpose is a bug, never recorded.</summary>
    public static string Require(string purpose) =>
        purpose is not null && All.Contains(purpose) ? purpose : throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Not a purpose of use.");
}

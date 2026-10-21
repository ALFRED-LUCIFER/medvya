using Medvya.BuildingBlocks.Domain;

namespace Medvya.Audit.Domain;

public enum AuditOutcome { Allowed, Denied }

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
    /// <summary>Purpose of use code: treatment, registration, billing, audit, support.</summary>
    [Classification(DataClass.Internal)] public required string Purpose { get; init; }
    [Classification(DataClass.Internal)] public AuditOutcome Outcome { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset OccurredAt { get; init; }
    [Classification(DataClass.Personal)] public string? ClientIp { get; init; }
    [Classification(DataClass.Internal)] public string? CorrelationId { get; init; }
}

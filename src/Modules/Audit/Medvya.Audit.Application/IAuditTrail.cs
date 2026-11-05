using Medvya.Audit.Domain;

namespace Medvya.Audit.Application;

/// <summary>Filters for reading the current tenant's audit events. <see cref="After"/> is the <see cref="AuditPage.Next"/> of the previous page.</summary>
public sealed record AuditQuery(Guid? ActorUserId, Guid? PatientId, string? Action, DateTimeOffset? From, DateTimeOffset? To, string? After, int Limit)
{
    /// <summary>Only events at this facility (for staff limited to one facility). Events written before the facility was recorded are never matched.</summary>
    public Guid? FacilityId { get; init; }
}

/// <summary>Newest first. <see cref="Actions"/> are the distinct action codes in the same date range, for the filter.</summary>
public sealed record AuditPage(IReadOnlyList<AuditEvent> Items, string? Next, IReadOnlyList<string> Actions);

/// <summary>
/// Reads the audit ledger for the practice access log (MDV-P1-035). Tenant scoping is the context's filter plus row-level security;
/// events hold opaque ids and codes only, so names are added by the caller from the modules that own them.
/// </summary>
public interface IAuditTrail
{
    Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
}

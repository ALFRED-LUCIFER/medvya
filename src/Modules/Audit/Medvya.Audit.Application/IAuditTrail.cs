using Medvya.Audit.Domain;

namespace Medvya.Audit.Application;

/// <summary>Filters for reading the current tenant's audit events. <see cref="After"/> is the <see cref="AuditPage.Next"/> of the previous page.</summary>
public sealed record AuditQuery(Guid? ActorUserId, Guid? PatientId, string? Action, DateTimeOffset? From, DateTimeOffset? To, string? After, int Limit)
{
    /// <summary>Only events at this facility (for staff limited to one facility). Events written before the facility was recorded are never matched.</summary>
    public Guid? FacilityId { get; init; }

    /// <summary>Only events about a patient's record (MDV audit UX-13): settings and other page views do not push them down the list.</summary>
    public bool PatientRecordsOnly { get; init; }

    /// <summary>
    /// The log's default view (MDV-P2-050): a patient's chart opened or changed, without the routine reads a list screen writes for every patient
    /// it shows (<see cref="RoutineReads"/>). A view only: nothing is left out of what is recorded or exported.
    /// </summary>
    public bool ChartOnly { get; init; }

    /// <summary>Patient-scoped reads written in bulk by list screens or automatic checks, not by someone opening that patient's chart.</summary>
    public static readonly string[] RoutineReads =
    [
        "patient.chart.allergy-flag", // one per patient on Today and the queue
        "note.pending",               // one per patient with an unsigned note on Today
        "rx.check",                   // the safety check run as a prescription is edited
        "patient.lookup", "patient.search", "patient.duplicate_check", // finding a patient at the desk
        "patient.visits.list", "patient.access.queue", "intake.escalations.read",
    ];
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

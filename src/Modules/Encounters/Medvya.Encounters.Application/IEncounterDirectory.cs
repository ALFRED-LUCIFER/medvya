namespace Medvya.Encounters.Application;

/// <summary>A diagnosis recorded in a consultation; <see cref="Provisional"/> until the doctor verifies it.</summary>
public sealed record PlanDiagnosis(string Description, bool Provisional);

/// <summary>What the consultation recorded for the patient's paper: its diagnoses and the day the follow-up is due (null: none).</summary>
public sealed record EncounterPlan(IReadOnlyList<PlanDiagnosis> Diagnoses, DateOnly? FollowUpOn);

/// <summary>
/// Encounters for other modules (orders, referrals): which patient, facility and clinician an encounter belongs to. Not audited here and
/// no access check: the calling use case checks clinical access to the patient and writes its own audit event, as with the patient directory.
/// Tenant filters and row-level security apply, so another tenant's encounter is never found.
/// </summary>
public interface IEncounterDirectory
{
    Task<EncounterView?> FindAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>The consultation's diagnoses and follow-up, for a printed paper.</summary>
    Task<EncounterPlan> PlanAsync(Guid encounterId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A module that adds its own entries to the patient timeline (orders in Prescribing). The timeline checks clinical access and audits the
/// read; a source only reads its own tables. Newest first; at most <paramref name="take"/> entries at or before <paramref name="upTo"/>.
/// </summary>
public interface ITimelineSource
{
    Task<IReadOnlyList<TimelineEntry>> EntriesAsync(Guid patientId, DateTimeOffset? upTo, int take, CancellationToken cancellationToken = default);
}

/// <summary>
/// Whether a document is one of this patient's, for a status move or a filed letter that points at it (MDV-P2-019). Tenant filters and row-level security
/// apply; a document that failed its malware scan is not a document of the record.
/// </summary>
public interface IDocumentDirectory
{
    Task<bool> BelongsToAsync(Guid documentId, Guid patientId, CancellationToken cancellationToken = default);
}

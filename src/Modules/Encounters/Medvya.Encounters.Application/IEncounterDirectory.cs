namespace Medvya.Encounters.Application;

/// <summary>
/// Encounters for other modules (orders, referrals): which patient, facility and clinician an encounter belongs to. Not audited here and
/// no access check: the calling use case checks clinical access to the patient and writes its own audit event, as with the patient directory.
/// Tenant filters and row-level security apply, so another tenant's encounter is never found.
/// </summary>
public interface IEncounterDirectory
{
    Task<EncounterView?> FindAsync(Guid encounterId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A module that adds its own entries to the patient timeline (orders in Prescribing). The timeline checks clinical access and audits the
/// read; a source only reads its own tables. Newest first; at most <paramref name="take"/> entries at or before <paramref name="upTo"/>.
/// </summary>
public interface ITimelineSource
{
    Task<IReadOnlyList<TimelineEntry>> EntriesAsync(Guid patientId, DateTimeOffset? upTo, int take, CancellationToken cancellationToken = default);
}

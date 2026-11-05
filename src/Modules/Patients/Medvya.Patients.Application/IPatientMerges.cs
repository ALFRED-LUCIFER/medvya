using System.Data.Common;
using Medvya.Patients.Domain;

namespace Medvya.Patients.Application;

/// <summary>One side of the P-03 comparison. Numbers are masked to the last four, like the opened record.</summary>
public sealed record MergeSide(
    Guid PatientId, string FullName, string Mrn, DateOnly DateOfBirth, SexAtBirth SexAtBirth, string Mobile,
    IReadOnlyList<MaskedIdentifier> Identifiers, int Visits);

/// <summary>
/// A request with both records side by side. <see cref="Survivor"/> keeps its MRN and absorbs <see cref="Merged"/>.
/// <see cref="ApprovalsRequired"/> is 2 when both records hold clinical data (a second clinician must approve), otherwise 1.
/// </summary>
public sealed record MergeReview(Guid Id, MergeRequestStatus Status, MergeSide Survivor, MergeSide Merged, int ApprovalsRequired, int Approvals, bool CanUndo);

/// <summary>A row of the review queue: ids and MRNs only; the comparison opens one request.</summary>
public sealed record MergeQueueRow(Guid Id, MergeRequestStatus Status, string SurvivorMrn, string MergedMrn, DateTimeOffset DecidedAt);

/// <summary>One line of a patient's merge history. <see cref="OtherMrn"/> is the other record's MRN.</summary>
public sealed record MergeHistoryEntry(Guid Id, MergeRequestStatus Status, bool ThisIsSurvivor, string OtherMrn, DateTimeOffset? MergedAt, DateTimeOffset? UnmergedAt);

/// <summary>
/// Other modules that hold data keyed by patient (the schedule today; the chart from P2) take part in a merge in the merge's own
/// database transaction, so a merge moves everything or nothing. <see cref="RestoreAsync"/> reverses <see cref="MoveAsync"/>.
/// </summary>
public interface IPatientMergeParticipant
{
    /// <summary>Whether the patient has clinical data; a merge of two such records needs a second clinician (MDV-P1-017).</summary>
    Task<bool> HasClinicalDataAsync(Guid patientId, CancellationToken cancellationToken);

    /// <summary>Visits (appointments) of the patient, for the comparison.</summary>
    Task<int> VisitsAsync(Guid patientId, CancellationToken cancellationToken);

    Task MoveAsync(Guid fromPatientId, Guid toPatientId, DbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Moves back what <see cref="MoveAsync"/> moved from <paramref name="fromPatientId"/>; later records of the survivor stay.</summary>
    Task RestoreAsync(Guid fromPatientId, Guid toPatientId, DbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>
/// Merge review (MDV-P1-017) for the current facility. Every change is audited in its own transaction (fail closed). Refusals are
/// <see cref="PatientValidationException"/> with field "merge" and a code; a request or patient that does not exist at this facility is null.
/// </summary>
public interface IPatientMerges
{
    /// <summary>Requests still to decide (Proposed or AwaitingApproval), newest first.</summary>
    Task<IReadOnlyList<MergeQueueRow>> QueueAsync(CancellationToken cancellationToken = default);

    Task<MergeReview?> GetAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>Asks for a review of two records of this facility; the older one survives. Refused for a pair that already has a request ("not the same" included).</summary>
    Task<Guid?> RequestAsync(Guid patientId, Guid otherPatientId, CancellationToken cancellationToken = default);

    /// <summary>"Not the same person": closes the request and suppresses the pair (no new request can be made for it).</summary>
    Task<bool> NotSameAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>"Request clinician approval": Proposed becomes AwaitingApproval.</summary>
    Task<bool> RequestApprovalAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A clinician's approval. The merge runs only when the required approvals are in (one; two different clinicians when both
    /// records hold clinical data). Returns the status afterwards.
    /// </summary>
    Task<MergeRequestStatus?> ApproveAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>An admin undoes a merge within <see cref="PatientMerge.UndoWindow"/>; both original records are restored.</summary>
    Task<bool> UndoAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>Completed and undone merges that involve the patient.</summary>
    Task<IReadOnlyList<MergeHistoryEntry>?> HistoryAsync(Guid patientId, CancellationToken cancellationToken = default);
}

public static class PatientMerge
{
    /// <remarks>shortcut: a fixed 90 days stands in for the practice's retention period; read it from practice settings when retention is configurable.</remarks>
    public static readonly TimeSpan UndoWindow = TimeSpan.FromDays(90);
}

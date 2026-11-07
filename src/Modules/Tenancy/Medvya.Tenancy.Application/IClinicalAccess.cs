using System.Data.Common;
using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>
/// One module's answer to "does this clinician have a care relationship with this patient at this facility?" (MDV-P2-024). Each module
/// answers from its own tables (Patients: registered here; Scheduling: visits; Encounters: encounters), so none reads another's.
/// </summary>
public interface ICareRelationshipSource
{
    Task<bool> RelatedAsync(Guid patientId, Guid facilityId, Guid clinicianUserId, CareScope scope, CancellationToken cancellationToken);
}

/// <summary>
/// Refused: the role does not reach the capability (<see cref="CanRequest"/> false), or it does but there is no care relationship and no
/// live grant, so the clinician may ask for access (<see cref="CanRequest"/> true; <see cref="RequestPending"/> when they already did).
/// </summary>
public sealed class ClinicalAccessDeniedException(bool canRequest, bool requestPending) : Exception("No clinical access to this patient.")
{
    public bool CanRequest { get; } = canRequest;
    public bool RequestPending { get; } = requestPending;
}

/// <summary>
/// Clinical authorisation (MDV-P2-024): tenant, facility and role (the role matrix through <see cref="IStaffAccess"/>), then a care
/// relationship under the clinic's policy or a live grant. Read from the database on every call. The purpose of each read is recorded by
/// its audit event; this check is for treatment.
/// </summary>
public interface IClinicalAccess
{
    /// <summary>The caller's level for <paramref name="capability"/> on this patient, when it is one of <paramref name="levels"/>.</summary>
    /// <exception cref="ClinicalAccessDeniedException"/>
    Task<Access> RequireAsync(Guid patientId, Guid facilityId, Capability capability, IReadOnlyList<Access> levels, CancellationToken cancellationToken = default);
}

public sealed record AccessRequestView(
    Guid Id, Guid FacilityId, Guid PatientId, Guid RequestedBy, AccessRequestReason Reason, AccessRequestStatus Status, DateTimeOffset RequestedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>Why a decision was refused: not pending any more, an invalid duration, or the owner's own request.</summary>
public sealed class AccessRequestRejectedException(string code) : Exception($"Access request refused: {code}")
{
    public string Code { get; } = code;
}

/// <summary>
/// "Request access" (MDV-P2-024): a clinician asks, an owner (Staff, roles, integrations: Config) grants for a fixed time or declines.
/// Every step is audited in the transaction of its change.
/// </summary>
public interface IAccessRequests
{
    /// <summary>
    /// The caller's pending request for the patient at the facility, or a new one. The caller (the Patients endpoint) has checked that the
    /// patient is one of the tenant's. Refused (<see cref="ClinicalAccessDeniedException"/>) when the caller's role cannot treat patients there.
    /// </summary>
    /// <exception cref="SettingsValidationException">Not a listed reason.</exception>
    Task<AccessRequestView> RequestAsync(Guid patientId, Guid facilityId, AccessRequestReason reason, CancellationToken cancellationToken = default);

    /// <summary>Pending requests at the facility, oldest first. The caller administers staff (checked by the endpoint).</summary>
    Task<IReadOnlyList<AccessRequestView>> PendingAsync(Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>Grants for one of <see cref="ClinicalAccessRules.GrantDays"/>; null when the request is not the tenant's.</summary>
    /// <exception cref="AccessRequestRejectedException"/>
    /// <exception cref="SettingsValidationException">Not one of the listed durations.</exception>
    Task<AccessRequestView?> GrantAsync(Guid requestId, int days, CancellationToken cancellationToken = default);

    /// <exception cref="AccessRequestRejectedException"/>
    Task<AccessRequestView?> DeclineAsync(Guid requestId, CancellationToken cancellationToken = default);
}

/// <summary>Another module's part of a new access request (the owner's notification), inside the request's transaction.</summary>
public interface IAccessRequestParticipant
{
    Task RequestedAsync(Guid requestId, Guid facilityId, DbTransaction transaction, CancellationToken cancellationToken);
}

using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Tenancy.Domain;

/// <summary>
/// Which clinicians of a facility treat its patients (MDV-P2-024). The default suits a small clinic: every clinician of the
/// facility treats every patient of it. "Assigned only" limits doctors to their own patients and the nurse to the day's visits.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClinicalAccessPolicy>))]
public enum ClinicalAccessPolicy { AllFacilityClinicians, AssignedOnly }

/// <summary>
/// What counts as a care relationship for one check (<see cref="ClinicalAccessRules.Scope"/>): the patient belongs to the facility
/// (registered there or with a visit there), has a visit at the facility today, or has a visit or encounter with this clinician.
/// </summary>
public enum CareScope { Facility, FacilityToday, Assigned }

/// <summary>Why a clinician asks for access: a short list only, so no clinical detail can be typed into the request.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccessRequestReason>))]
public enum AccessRequestReason { CoveringColleague, SeeingPatientToday, ReviewingResults, Referral }

[JsonConverter(typeof(JsonStringEnumConverter<AccessRequestStatus>))]
/// <summary>Withdrawn: the clinician who asked was offboarded before an owner decided (MDV-P1-009).</summary>
public enum AccessRequestStatus { Pending, Granted, Declined, Withdrawn }

/// <summary>
/// ACCESS_REQUEST (MDV-P2-024): a clinician without a care relationship asks for one patient at one facility; an owner grants it for
/// a fixed time or declines it. A grant is a care relationship until <see cref="ExpiresAt"/>. Rows are kept as the record of who saw what.
/// </summary>
public sealed class AccessRequest
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    /// <summary>Opaque id of the patient in the Patients module.</summary>
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid RequestedBy { get; init; }
    [Classification(DataClass.Internal)] public AccessRequestReason Reason { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RequestedAt { get; init; }
    [Classification(DataClass.Internal)] public AccessRequestStatus Status { get; set; }
    [Classification(DataClass.Internal)] public Guid? DecidedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? DecidedAt { get; set; }
    /// <summary>Set by a grant: the relationship ends at this instant.</summary>
    [Classification(DataClass.Internal)] public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>The rules of clinical authorisation on top of the role matrix (MDV-P2-024), kept pure so each branch is tested.</summary>
public static class ClinicalAccessRules
{
    /// <summary>A grant lasts one day, a week or a month: time-limited, never open-ended.</summary>
    public static readonly IReadOnlyList<int> GrantDays = [1, 7, 30];

    /// <summary>
    /// Levels that read clinical data for treatment. Released is the patient's own portal and Own a patient's own records; break-glass
    /// comes with hospitals (P7), so until then it opens nothing here.
    /// </summary>
    public static bool Treats(Access access) => access is Access.Read or Access.Limited or Access.Full or Access.Sign;

    /// <summary>
    /// Where the relationship must be found. "All facility clinicians": the facility's patients. "Assigned only": a doctor's own visits and
    /// encounters; a role with Limited Patient 360 (nurse, care coordinator) works the day's queue, so today's visits at the facility.
    /// </summary>
    public static CareScope Scope(ClinicalAccessPolicy policy, Access patient360) =>
        policy == ClinicalAccessPolicy.AllFacilityClinicians ? CareScope.Facility
        : patient360 == Access.Limited ? CareScope.FacilityToday
        : CareScope.Assigned;
}

using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<EncounterStatus>))]
public enum EncounterStatus { Active, Ended }

/// <summary>Which system holds the record of this encounter. Medvya for clinics; a hospital's HIS comes with P7.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AuthorityMode>))]
public enum AuthorityMode { Medvya }

/// <summary>
/// ENCOUNTER (MDV-P2-001): one consultation of one patient, bound to one visit (appointment) at one facility and led by the clinician who
/// started it. Exactly one per visit: starting again returns it. Notes, recordings and orders (P2/P3) belong to exactly one encounter.
/// </summary>
public sealed class Encounter
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    /// <summary>Opaque id of the patient in the Patients module.</summary>
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    /// <summary>Set while a patient merge (MDV-P1-017) holds this encounter on the surviving record: the record it came from.</summary>
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    /// <summary>Opaque id of the visit in the Scheduling module.</summary>
    [Classification(DataClass.Internal)] public Guid AppointmentId { get; init; }
    [Classification(DataClass.Internal)] public Guid LeadClinicianId { get; init; }
    [Classification(DataClass.Internal)] public EncounterStatus Status { get; set; }
    [Classification(DataClass.Internal)] public AuthorityMode AuthorityMode { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset StartedAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? EndedAt { get; set; }
    [Classification(DataClass.Internal)] public Guid? EndedBy { get; set; }
    /// <summary>MDV-P1-091: the treating doctor confirmed name and date of birth with the patient (one tap); null until then. Set once, never cleared.</summary>
    [Classification(DataClass.Internal)] public DateTimeOffset? IdentityConfirmedAt { get; set; }
    [Classification(DataClass.Internal)] public Guid? IdentityConfirmedBy { get; set; }
}

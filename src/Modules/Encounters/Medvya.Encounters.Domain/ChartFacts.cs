using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>Where a chart fact came from. Every fact carries one: no value is shown without it (MDV-P2-004).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChartSource>))]
public enum ChartSource { ClinicianVerified, PatientReported, Imported, SyncedFromHis }

/// <summary>A substance the patient is allergic to, or the recorded statement that they have none. No rows at all means "not recorded".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AllergyKind>))]
public enum AllergyKind { Substance, NoKnownAllergies }

[JsonConverter(typeof(JsonStringEnumConverter<AllergyStatus>))]
public enum AllergyStatus { Confirmed, PatientReported, Unknown, NotAnAllergy }

/// <summary>ALLERGY. Written by nurse intake and the doctor (MDV-P2-008, P2-010); read by Patient 360 (MDV-P2-004).</summary>
public sealed class Allergy
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public AllergyKind Kind { get; init; }
    /// <summary>Null for <see cref="AllergyKind.NoKnownAllergies"/>.</summary>
    [Classification(DataClass.SensitiveHealth)] public string? Substance { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Reaction { get; init; }
    [Classification(DataClass.SensitiveHealth)] public AllergyStatus Status { get; set; }
    [Classification(DataClass.Internal)] public ChartSource Source { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RecordedAt { get; init; }
    // Set only for an allergy written at nurse intake (MDV-P2-008); who set the current status and when.
    [Classification(DataClass.Internal)] public Guid? FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public Guid? StatusSetBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? StatusSetAt { get; set; }
}

/// <summary>How sure the clinic is of a problem. Provisional is a working diagnosis: shown apart and never passed on as a confirmed one (MDV-P2-010).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProblemVerification>))]
public enum ProblemVerification { Verified, PatientReported, Provisional }

[JsonConverter(typeof(JsonStringEnumConverter<ProblemStatus>))]
public enum ProblemStatus { Active, Resolved }

/// <summary>
/// PROBLEM: a diagnosis on the problem list with its verification state. A doctor codes it in a consultation (MDV-P2-010): the code keeps the
/// code system and version it was chosen from, and the encounter and author it was recorded in. Uncoded is allowed: an unknown code stays blank.
/// </summary>
public sealed class Problem
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Icd10Code { get; init; }
    /// <summary>The code set <see cref="Icd10Code"/> was chosen from, e.g. "ICD-10" and "2019-medvya-starter-1" (see <see cref="DiagnosisCodeRules"/>).</summary>
    [Classification(DataClass.Internal)] public string? CodeSystem { get; init; }
    [Classification(DataClass.Internal)] public string? CodeSystemVersion { get; init; }
    [Classification(DataClass.SensitiveHealth)] public required string Description { get; init; }
    [Classification(DataClass.SensitiveHealth)] public ProblemVerification Verification { get; set; }
    [Classification(DataClass.SensitiveHealth)] public ProblemStatus Status { get; set; }
    [Classification(DataClass.SensitiveHealth)] public DateOnly? OnsetOn { get; set; }
    [Classification(DataClass.Internal)] public ChartSource Source { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RecordedAt { get; init; }
    // Set for a problem recorded in a consultation (MDV-P2-010); imports and older rows leave them empty.
    [Classification(DataClass.Internal)] public Guid? EncounterId { get; init; }
    [Classification(DataClass.Internal)] public Guid? RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public Guid? UpdatedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? UpdatedAt { get; set; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<MedicationState>))]
public enum MedicationState { Active, Stopped }

/// <summary>MEDICATION_STATEMENT: a medicine on the chart, active or stopped. The doctor's reconciliation (MDV-P2-011) is its only writer for chart medicines.</summary>
public sealed class MedicationStatement
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public required string Name { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Dose { get; set; }
    [Classification(DataClass.SensitiveHealth)] public MedicationState State { get; set; }
    [Classification(DataClass.Internal)] public ChartSource Source { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RecordedAt { get; init; }
    // Set only for a medicine the patient reported at nurse intake (MDV-P2-008). NeedsClarification: the name could not be identified and is kept as said.
    [Classification(DataClass.Internal)] public Guid? FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? AppointmentId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Frequency { get; init; }
    [Classification(DataClass.Internal)] public bool NeedsClarification { get; init; }
    [Classification(DataClass.Internal)] public Guid? RecordedBy { get; init; }
    // Set by the doctor's reconciliation (MDV-P2-011): when the clinic last reviewed this medicine, and when it was stopped (Patient 360 and the timeline show it).
    [Classification(DataClass.Internal)] public DateTimeOffset? ReviewedAt { get; set; }
    [Classification(DataClass.Internal)] public Guid? ReviewedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? StoppedAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ObservationCode>))]
public enum ObservationCode { BloodPressureSystolic, BloodPressureDiastolic, Pulse, Temperature, OxygenSaturation, Weight, HbA1c }

/// <summary>OBSERVATION: one measured value (vitals from MDV-P2-007, key labs from MDV-P2-005). Units are fixed per code (see <see cref="Units"/>).</summary>
public sealed class Observation
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public ObservationCode Code { get; init; }
    /// <summary>The reading in hundredths of <see cref="Units"/> (7.4 % is 740): whole numbers, never a float or decimal in the domain.</summary>
    [Classification(DataClass.SensitiveHealth)] public int ValueHundredths { get; init; }
    [Classification(DataClass.SensitiveHealth)] public DateTimeOffset TakenAt { get; init; }
    [Classification(DataClass.Internal)] public ChartSource Source { get; init; }
    // Set only for a vital taken at a visit's nurse intake (MDV-P2-007); labs and imports leave them empty. A re-measure is a new row.
    [Classification(DataClass.Internal)] public Guid? FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? AppointmentId { get; init; }
    [Classification(DataClass.Internal)] public VitalAlert? Flag { get; init; }
    /// <summary>The patient's last reading of this code before this visit, in tenths, to show beside the new one.</summary>
    [Classification(DataClass.SensitiveHealth)] public int? PreviousTenths { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? PreviousAt { get; init; }
    /// <summary>A flagged reading is only final once the nurse kept it on purpose.</summary>
    [Classification(DataClass.Internal)] public bool FlagKept { get; set; }
    [Classification(DataClass.Internal)] public Guid? RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public Guid? VerifiedBy { get; init; }
    /// <summary>Nurse readings have one decimal place; the stored value is hundredths. Not a column.</summary>
    [Classification(DataClass.SensitiveHealth)] public int ValueTenths => ValueHundredths / 10;

    public static string Units(ObservationCode code) => code switch
    {
        ObservationCode.BloodPressureSystolic or ObservationCode.BloodPressureDiastolic => "mmHg",
        ObservationCode.Pulse => "/min",
        ObservationCode.Temperature => "°C",
        ObservationCode.OxygenSaturation or ObservationCode.HbA1c => "%",
        ObservationCode.Weight => "kg",
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };
}

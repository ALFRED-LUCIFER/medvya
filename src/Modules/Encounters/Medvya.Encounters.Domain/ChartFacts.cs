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
}

[JsonConverter(typeof(JsonStringEnumConverter<ProblemVerification>))]
public enum ProblemVerification { Verified, PatientReported, Provisional }

/// <summary>PROBLEM: a diagnosis on the problem list with its verification state (coding arrives with MDV-P2-010).</summary>
public sealed class Problem
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Icd10Code { get; init; }
    [Classification(DataClass.SensitiveHealth)] public required string Description { get; init; }
    [Classification(DataClass.SensitiveHealth)] public ProblemVerification Verification { get; set; }
    [Classification(DataClass.SensitiveHealth)] public DateOnly? OnsetOn { get; init; }
    [Classification(DataClass.Internal)] public ChartSource Source { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RecordedAt { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<MedicationState>))]
public enum MedicationState { Active, Stopped }

/// <summary>MEDICATION_STATEMENT: a medicine on the chart, active or stopped (reconciliation arrives with MDV-P2-011).</summary>
public sealed class MedicationStatement
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public required string Name { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Dose { get; init; }
    [Classification(DataClass.SensitiveHealth)] public MedicationState State { get; set; }
    [Classification(DataClass.Internal)] public ChartSource Source { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RecordedAt { get; init; }
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

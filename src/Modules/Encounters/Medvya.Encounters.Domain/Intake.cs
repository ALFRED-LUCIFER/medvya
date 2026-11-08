using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<VitalKind>))]
public enum VitalKind { SystolicBp, DiastolicBp, Pulse, Temperature, OxygenSaturation, Weight }

/// <summary>Why a reading needs a decision: above or below the usual range, or a weight that moved a lot since the last one. Words in the UI, never colour alone.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VitalAlert>))]
public enum VitalAlert { High, Low, Changed }

/// <summary>
/// The nurse intake of one visit (MDV-P2-007): the chief complaint in the patient's words and whether the nurse has handed the patient to the doctor.
/// Exists from the nurse's first save, before any encounter (the doctor starts that). One per visit.
/// </summary>
public sealed class Intake
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid AppointmentId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? ChiefComplaint { get; set; }
    [Classification(DataClass.Internal)] public Guid? HandedOffBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? HandedOffAt { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The nurse raised the patient to the treating doctor, with a reason. Open until the doctor starts the consultation.</summary>
public sealed class IntakeEscalation
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid AppointmentId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string Reason { get; init; } = "";
    [Classification(DataClass.Internal)] public Guid EscalatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset EscalatedAt { get; init; }
}

/// <summary>
/// What a reading may be and when it is flagged (adult ranges). A value no living person can have is refused; a usual-range miss is saved
/// and flagged.
/// shortcut: one adult range set for every facility and patient; per-facility, paediatric and pregnancy ranges when clinics ask for them.
/// </summary>
public static class VitalRules
{
    /// <summary>Hard limits: outside them the reading is a typing or device mistake and is refused.</summary>
    public static (decimal Min, decimal Max) Possible(VitalKind kind) => kind switch
    {
        VitalKind.SystolicBp => (50, 300),
        VitalKind.DiastolicBp => (20, 200),
        VitalKind.Pulse => (20, 300),
        VitalKind.Temperature => (30, 44),
        VitalKind.OxygenSaturation => (50, 100),
        VitalKind.Weight => (0.3m, 500),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Usual range: below Low or at/above High is flagged.</summary>
    static (decimal Low, decimal High) Usual(VitalKind kind) => kind switch
    {
        VitalKind.SystolicBp => (90, 140),
        VitalKind.DiastolicBp => (60, 90),
        VitalKind.Pulse => (50, 101),
        VitalKind.Temperature => (36.0m, 38.0m),
        VitalKind.OxygenSaturation => (94, 101),
        _ => (decimal.MinValue, decimal.MaxValue),
    };

    /// <summary>A weight that moved this much (kg) since the last one is worth a second look.</summary>
    public const decimal WeightChangeKg = 2.0m;

    /// <summary>The chart's code for a bedside vital (the same OBSERVATION table holds labs; MDV-P2-004).</summary>
    public static ObservationCode Code(VitalKind kind) => kind switch
    {
        VitalKind.SystolicBp => ObservationCode.BloodPressureSystolic,
        VitalKind.DiastolicBp => ObservationCode.BloodPressureDiastolic,
        VitalKind.Pulse => ObservationCode.Pulse,
        VitalKind.Temperature => ObservationCode.Temperature,
        VitalKind.OxygenSaturation => ObservationCode.OxygenSaturation,
        VitalKind.Weight => ObservationCode.Weight,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Null for a lab, which is not a vital.</summary>
    public static VitalKind? Kind(ObservationCode code) => Enum.GetValues<VitalKind>().Cast<VitalKind?>().FirstOrDefault(k => Code(k!.Value) == code);

    public static int ToTenths(decimal value) => (int)decimal.Round(value * 10);

    public static decimal FromTenths(int tenths) => tenths / 10m;

    public static bool IsPossible(VitalKind kind, decimal value) => Possible(kind) is var (min, max) && value >= min && value <= max;

    public static VitalAlert? Flag(VitalKind kind, decimal value, decimal? previous)
    {
        if (kind == VitalKind.Weight) return previous is { } p && Math.Abs(value - p) >= WeightChangeKg ? VitalAlert.Changed : null;
        var (low, high) = Usual(kind);
        return value < low ? VitalAlert.Low : value >= high ? VitalAlert.High : null;
    }
}

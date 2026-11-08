using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<VitalKind>))]
public enum VitalKind { SystolicBp, DiastolicBp, Pulse, Temperature, OxygenSaturation, Weight }

/// <summary>Why a reading needs a decision: above or below the usual range, or a weight that moved a lot since the last one. Words in the UI, never colour alone.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VitalAlert>))]
public enum VitalAlert { High, Low, Changed }

[JsonConverter(typeof(JsonStringEnumConverter<ObservationSource>))]
public enum ObservationSource { NurseIntake }

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

/// <summary>
/// One vital sign reading (an OBSERVATION): source Nurse intake, recorded and verified by the nurse who entered it. A re-measure is a new row;
/// the latest of each kind for the visit is the current one. A flagged reading is only final once the nurse kept it on purpose (<see cref="FlagKept"/>).
/// </summary>
public sealed class Observation
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid AppointmentId { get; init; }
    [Classification(DataClass.Internal)] public VitalKind Kind { get; init; }
    /// <summary>The reading in tenths of its unit (368 = 36.8 °C): one decimal place is all a bedside reading has, and no column is a fraction (AGENTS.md 3.4).</summary>
    [Classification(DataClass.SensitiveHealth)] public int ValueTenths { get; init; }
    [Classification(DataClass.Internal)] public VitalAlert? Flag { get; init; }
    /// <summary>The patient's last reading of this kind before this visit, to show beside the new one.</summary>
    [Classification(DataClass.SensitiveHealth)] public int? PreviousTenths { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? PreviousAt { get; init; }
    [Classification(DataClass.Internal)] public bool FlagKept { get; set; }
    [Classification(DataClass.Internal)] public ObservationSource Source { get; init; }
    [Classification(DataClass.Internal)] public Guid RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public Guid VerifiedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RecordedAt { get; init; }
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

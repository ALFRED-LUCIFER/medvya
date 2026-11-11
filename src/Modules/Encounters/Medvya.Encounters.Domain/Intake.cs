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
    /// <summary>The nurse asked the patient at this visit and confirmed on purpose that they have no known allergies (MDV-P2-008). Never a default; asked again at every visit.</summary>
    [Classification(DataClass.Internal)] public Guid? NoKnownAllergiesBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? NoKnownAllergiesAt { get; set; }
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
/// What a reading may be and when it is flagged, by age band (MDV-P2-007, #956). A value no living person can have is refused; a usual-range
/// miss is saved and flagged. Children: AHA PALS 2020 awake heart rate and blood pressure ranges, low systolic at the PALS hypotension line
/// (under 1 month 60, under 1 year 70, then 70 + 2 x age to 90 from 10 years). Adults (18 and over): the ranges used since MDV-P2-007.
/// Temperature and SpO2 use the same lines at every age. The references give no sex-specific values for these six measurements, so sex does
/// not change a range; sex- and height-specific blood-pressure centiles are not used.
/// shortcut: one range set for every facility, no pregnancy ranges, and the weight-change line is 2 kg at every age; per-facility and
/// pregnancy ranges and a relative weight line for children when the clinical reviewer asks for them.
/// </summary>
public static class VitalRules
{
    /// <summary>The clinician who reviewed these ranges and when. Null: the ranges are a draft awaiting clinical sign-off (owner decision 11 Nov 2026).</summary>
    public static readonly ClinicalSignOff? SignOff; // set to new(name, registration, date) when signed

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

    /// <summary>Usual range for a patient this many whole months old: below Low or at/above High is flagged.</summary>
    public static (decimal Low, decimal High) Usual(VitalKind kind, int ageInMonths) => (kind, ageInMonths) switch
    {
        (VitalKind.Temperature, _) => (36.0m, 38.0m),
        (VitalKind.OxygenSaturation, _) => (94, 101),
        (VitalKind.Weight, _) => (decimal.MinValue, decimal.MaxValue),
        (_, < 1) => Band(kind, pulse: (100, 206), sys: (60, 85), dia: (31, 54)),
        (_, < 12) => Band(kind, pulse: (100, 181), sys: (70, 105), dia: (37, 57)),
        (_, < 36) => Band(kind, pulse: (98, 141), sys: (72, 107), dia: (42, 64)),
        (_, < 72) => Band(kind, pulse: (80, 121), sys: (76, 113), dia: (46, 73)),
        (_, < 120) => Band(kind, pulse: (75, 119), sys: (82, 116), dia: (57, 77)),
        (_, < 216) => Band(kind, pulse: (60, 101), sys: (90, 132), dia: (64, 84)),
        _ => Band(kind, pulse: (50, 101), sys: (90, 140), dia: (60, 90)),
    };

    static (decimal, decimal) Band(VitalKind kind, (decimal, decimal) pulse, (decimal, decimal) sys, (decimal, decimal) dia) => kind switch
    {
        VitalKind.Pulse => pulse,
        VitalKind.SystolicBp => sys,
        VitalKind.DiastolicBp => dia,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Whole months between the date of birth and the day of the reading.</summary>
    public static int AgeInMonths(DateOnly dateOfBirth, DateOnly on) =>
        Math.Max(0, (on.Year - dateOfBirth.Year) * 12 + on.Month - dateOfBirth.Month - (on.Day < dateOfBirth.Day ? 1 : 0));

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

    public static VitalAlert? Flag(VitalKind kind, decimal value, decimal? previous, int ageInMonths)
    {
        if (kind == VitalKind.Weight) return previous is { } p && Math.Abs(value - p) >= WeightChangeKg ? VitalAlert.Changed : null;
        var (low, high) = Usual(kind, ageInMonths);
        return value < low ? VitalAlert.Low : value >= high ? VitalAlert.High : null;
    }
}

/// <summary>Who signed the vital ranges off clinically: name, registration number and the day.</summary>
public sealed record ClinicalSignOff(
    [property: Classification(DataClass.Personal)] string Reviewer, [property: Classification(DataClass.Personal)] string Registration,
    [property: Classification(DataClass.Internal)] DateOnly SignedOn);

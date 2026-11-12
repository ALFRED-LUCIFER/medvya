using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>Which date the due date comes from (MDV-P2-034).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DatingMethod>))]
public enum DatingMethod { Lmp, Scan }

/// <summary>Why: only one date was given, or both were and the scan agrees with the LMP within the dating rule (LMP kept) or differs by more (scan used).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DatingReason>))]
public enum DatingReason { LmpOnly, ScanOnly, ScanAgrees, ScanDiffers }

/// <summary>How a pregnancy ended. <see cref="EnteredInError"/> closes a pregnancy recorded for the wrong patient or by mistake.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PregnancyOutcome>))]
public enum PregnancyOutcome { LiveBirth, Stillbirth, Miscarriage, Termination, Ectopic, EnteredInError }

/// <summary>An antenatal contact: a visit is linked to it, it falls in the current week, it is still to come, or its week passed with no visit.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContactState>))]
public enum ContactState { Done, Today, ToCome, Missed }

/// <summary>The danger signs a woman is told to come in for at once (shown in words; the screen lists them all).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DangerSign>))]
public enum DangerSign { VaginalBleeding, SevereHeadacheOrBlurredVision, SwellingOfFaceOrHands, LeakingFluid, FewerBabyMovements, FeverOrBurningUrine }

/// <summary>
/// PREGNANCY_EPISODE (MDV-P2-034): one pregnancy of one patient, open until an outcome closes it (at most one open per patient, a unique
/// index). The due date is worked out by <see cref="PregnancyRules.Date"/>; when new dates move it, <see cref="PreviousDueDate"/> keeps the
/// one it replaced. Gravida, para, abortions and living children stay null when not given (never guessed).
/// </summary>
public sealed class PregnancyEpisode
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public DateOnly? Lmp { get; set; }
    [Classification(DataClass.SensitiveHealth)] public DateOnly? ScanDate { get; set; }
    [Classification(DataClass.SensitiveHealth)] public int? ScanGestationDays { get; set; }
    [Classification(DataClass.SensitiveHealth)] public DateOnly DueDate { get; set; }
    [Classification(DataClass.SensitiveHealth)] public DatingMethod DatedBy { get; set; }
    [Classification(DataClass.SensitiveHealth)] public DatingReason DatingReason { get; set; }
    [Classification(DataClass.SensitiveHealth)] public DateOnly? PreviousDueDate { get; set; }
    [Classification(DataClass.SensitiveHealth)] public int? Gravida { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? Para { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? Abortions { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int? Living { get; init; }
    [Classification(DataClass.Internal)] public Guid StartedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset StartedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid? DangerSignsCounselledBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? DangerSignsCounselledAt { get; set; }
    [Classification(DataClass.SensitiveHealth)] public PregnancyOutcome? Outcome { get; set; }
    [Classification(DataClass.Internal)] public Guid? ClosedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>A clinician's statement that the patient is not pregnant, with who and when; it counts only on the facility day it was made (see <see cref="PregnancyRules.RuledOutHolds"/>).</summary>
public sealed class PregnancyRuledOut
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid RecordedBy { get; init; }
    [Classification(DataClass.SensitiveHealth)] public DateTimeOffset RecordedAt { get; init; }
}

/// <summary>
/// The pregnancy dates and the contact plan (MDV-P2-034). Due date: LMP + 280 days (Naegele). A dating scan replaces it when the two due dates
/// differ by more than the ACOG Committee Opinion 700 (2017) line for the gestation at the scan: up to 8 w 6 d more than 5 days, to 15 w 6 d
/// more than 7, to 21 w 6 d more than 10, to 27 w 6 d more than 14, after that more than 21. Antenatal contacts at 8, 12, 16, 20, 24, 28, 32,
/// 36, 38 and 40 weeks (the design's plan, after the WHO 2016 eight-contact model). Trimesters: first to 13 w 6 d, second to 27 w 6 d.
/// shortcut: the dating rule, the contact plan and the one-day "not pregnant" window ship unverified (SignOff null) and wait for clinical
/// sign-off; a facility plan or a longer window when the clinical reviewer sets one.
/// </summary>
public static class PregnancyRules
{
    /// <summary>Null: the rules are a draft awaiting clinical sign-off, like <see cref="VitalRules.SignOff"/>.</summary>
    public static readonly ClinicalSignOff? SignOff; // set to new(name, registration, date) when signed

    public const int TermDays = 280, MaxGestationDays = 44 * 7, MaxCount = 30;
    public static readonly IReadOnlyList<int> ContactWeeks = [8, 12, 16, 20, 24, 28, 32, 36, 38, 40];

    /// <summary>The largest difference (days) between the scan and LMP due dates that still keeps the LMP date.</summary>
    public static int ScanTolerance(int scanGestationDays) => scanGestationDays switch
    {
        < 9 * 7 => 5,
        < 16 * 7 => 7,
        < 22 * 7 => 10,
        < 28 * 7 => 14,
        _ => 21,
    };

    /// <summary>The due date and why. At least one of the LMP or the scan (date with gestation) is given; the caller validates that.</summary>
    public static (DateOnly Due, DatingMethod Method, DatingReason Reason) Date(DateOnly? lmp, DateOnly? scanDate, int? scanGestationDays)
    {
        DateOnly? byLmp = lmp?.AddDays(TermDays);
        DateOnly? byScan = scanDate is { } s && scanGestationDays is { } g ? s.AddDays(TermDays - g) : null;
        if (byScan is null) return (byLmp ?? throw new ArgumentException("No date to work from."), DatingMethod.Lmp, DatingReason.LmpOnly);
        if (byLmp is null) return (byScan.Value, DatingMethod.Scan, DatingReason.ScanOnly);
        return Math.Abs(byScan.Value.DayNumber - byLmp.Value.DayNumber) > ScanTolerance(scanGestationDays!.Value)
            ? (byScan.Value, DatingMethod.Scan, DatingReason.ScanDiffers)
            : (byLmp.Value, DatingMethod.Lmp, DatingReason.ScanAgrees);
    }

    /// <summary>Days of gestation on <paramref name="on"/> (the facility's date), from the due date: 171 is 24 w 3 d.</summary>
    public static int GestationDays(DateOnly due, DateOnly on) => on.DayNumber - (due.DayNumber - TermDays);

    /// <summary>
    /// Each planned contact with its state. A visit is linked to the contact nearest its gestation (the earlier on a tie); a contact with a
    /// visit is done, otherwise it is today in its own week, to come after today, or missed. Visits before the pregnancy are ignored.
    /// </summary>
    public static IReadOnlyList<(int Week, ContactState State, Guid? VisitId)> Contacts(DateOnly due, DateOnly today, IEnumerable<(Guid Id, DateOnly On)> visits)
    {
        var linked = new Dictionary<int, Guid>();
        foreach (var (id, on) in visits.OrderBy(v => v.On))
        {
            var days = GestationDays(due, on);
            if (days < 0) continue;
            var nearest = ContactWeeks.MinBy(w => Math.Abs(w * 7 - days)); // MinBy keeps the first, so a tie goes to the earlier contact
            linked.TryAdd(nearest, id);
        }
        var weeks = GestationDays(due, today) / 7;
        return [.. ContactWeeks.Select(w => linked.TryGetValue(w, out var v) ? (w, ContactState.Done, (Guid?)v)
            : (w, w == weeks ? ContactState.Today : w > weeks ? ContactState.ToCome : ContactState.Missed, (Guid?)null))];
    }

    /// <summary>A "not pregnant" statement counts only on the facility day it was made: the current visit.</summary>
    public static bool RuledOutHolds(DateOnly recordedOn, DateOnly today) => recordedOn == today;

    /// <summary>Hard limits for the antenatal readings (outside them the value is a typing or device mistake and is refused).</summary>
    public static (decimal Min, decimal Max) Possible(ObservationCode code) => code switch
    {
        ObservationCode.FundalHeight => (5, 50),
        ObservationCode.FetalHeartRate => (50, 250),
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };
}

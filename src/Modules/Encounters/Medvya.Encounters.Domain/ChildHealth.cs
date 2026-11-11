using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<DoseOutcome>))]
public enum DoseOutcome { Given, Deferred }

/// <summary>Where a scheduled dose stands on a day. A deferral does not change it: a deferred dose still becomes due and overdue.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DoseState>))]
public enum DoseState { Given, Overdue, DueNow, Upcoming }

[JsonConverter(typeof(JsonStringEnumConverter<MilestoneAnswer>))]
public enum MilestoneAnswer { Yes, No, Unsure }

[JsonConverter(typeof(JsonStringEnumConverter<MilestoneAnswerer>))]
public enum MilestoneAnswerer { Parent, Clinician }

/// <summary>
/// IMMUNIZATION (MDV-P2-036): one dose of the schedule given (date, batch, who gave it) or deferred with a reason. Append-only: nothing is
/// edited or deleted; the latest row of a vaccine and dose is its answer, and a deferred dose can be given later.
/// </summary>
public sealed class Immunisation
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    /// <summary>A vaccine code of the schedule (<see cref="ScheduledDose.Vaccine"/>).</summary>
    [Classification(DataClass.SensitiveHealth)] public required string Vaccine { get; init; }
    [Classification(DataClass.SensitiveHealth)] public int Dose { get; init; }
    [Classification(DataClass.SensitiveHealth)] public DoseOutcome Outcome { get; init; }
    /// <summary>Set for a given dose only; the day in the facility's zone.</summary>
    [Classification(DataClass.SensitiveHealth)] public DateOnly? GivenOn { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Batch { get; init; }
    /// <summary>Who gave it, when not the person recording it (another clinic, a colleague). Blank: the recorder gave it.</summary>
    [Classification(DataClass.Personal)] public string? GivenBy { get; init; }
    /// <summary>Required for a deferral, empty for a given dose.</summary>
    [Classification(DataClass.SensitiveHealth)] public string? DeferReason { get; init; }
    [Classification(DataClass.Internal)] public Guid RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RecordedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
}

/// <summary>DEVELOPMENTAL_MILESTONE (MDV-P2-036): one milestone of an age band answered yes, no or unsure, and who answered. Append-only; the latest answer counts.</summary>
public sealed class MilestoneResponse
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public required string Band { get; init; }
    [Classification(DataClass.Internal)] public required string Milestone { get; init; }
    [Classification(DataClass.SensitiveHealth)] public MilestoneAnswer Answer { get; init; }
    [Classification(DataClass.Internal)] public MilestoneAnswerer AnsweredBy { get; init; }
    [Classification(DataClass.Internal)] public Guid RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RecordedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
}

/// <summary>An age as weeks plus months after birth: "6 weeks", "9 months", "4 years" (48 months).</summary>
public readonly record struct AgeAt([property: Classification(DataClass.Internal)] int Weeks, [property: Classification(DataClass.Internal)] int Months)
{
    public static AgeAt W(int weeks) => new(weeks, 0);
    public static AgeAt M(int months) => new(0, months);
    public static AgeAt Y(int years) => new(0, years * 12);
    /// <summary>The day the age is reached. Months are calendar months (a 31 January birth is a month old on 28 or 29 February).</summary>
    public DateOnly On(DateOnly born) => born.AddMonths(Months).AddDays(7 * Weeks);
}

public sealed record ScheduledDose([property: Classification(DataClass.Internal)] string Vaccine, [property: Classification(DataClass.Internal)] int Dose);

/// <summary>
/// One age band of a schedule. A dose of it is upcoming before <see cref="From"/>, due from that day, and overdue from <see cref="Until"/>:
/// the band's upper age, or the next band's start where the schedule names a single age.
/// </summary>
public sealed record ScheduleBand(
    [property: Classification(DataClass.Internal)] string Key, [property: Classification(DataClass.Internal)] AgeAt From,
    [property: Classification(DataClass.Internal)] AgeAt Until, [property: Classification(DataClass.Internal)] IReadOnlyList<ScheduledDose> Doses)
{
    public DoseState StateOn(DateOnly born, DateOnly today, bool given) =>
        given ? DoseState.Given : today < From.On(born) ? DoseState.Upcoming : today < Until.On(born) ? DoseState.DueNow : DoseState.Overdue;
}

/// <summary>
/// The childhood immunisation schedule per country (AGENTS.md 3.3): one subclass per country, picked by the facility's country, no country
/// checks elsewhere. The doses are what the clinic records against; the days come from the date of birth (<see cref="ScheduleBand.StateOn"/>).
/// shortcut: India is the design's reading of IAP 2023 (yearly influenza and the catch-up rules are left out) and the UAE holds only the doses the
/// MOHAP 2019 decree names; both stay unverified until <see cref="SignOff"/> is set by a paediatrician, and the screen says so.
/// </summary>
public abstract class ImmunisationSchedule
{
    public static readonly ImmunisationSchedule India = new IndiaSchedule();
    public static readonly ImmunisationSchedule Uae = new UaeSchedule();

    /// <summary>Null: the schedules are a draft awaiting clinical sign-off, like <see cref="VitalRules.SignOff"/>.</summary>
    public static readonly ClinicalSignOff? SignOff; // set to new(name, registration, date) when signed

    public static ImmunisationSchedule For(string countryCode) => countryCode switch
    {
        "IN" => India,
        "AE" => Uae,
        _ => throw new ArgumentOutOfRangeException(nameof(countryCode), "Unsupported country."),
    };

    /// <summary>The schedule's name as the card shows it ("IAP 2023").</summary>
    public abstract string Name();
    public abstract IReadOnlyList<ScheduleBand> Bands();

    /// <summary>The band that holds a vaccine's dose, or null when the schedule has no such dose.</summary>
    public ScheduleBand? BandOf(string vaccine, int dose) => Bands().FirstOrDefault(b => b.Doses.Contains(new ScheduledDose(vaccine, dose)));

    static ScheduledDose[] Doses(int dose, params string[] vaccines) => [.. vaccines.Select(v => new ScheduledDose(v, dose))];

    /// <summary>
    /// Indian Academy of Pediatrics, ACVIP immunisation schedule 2023, as the 12 Nov design reads it (`imm` in chartVals, Medvya Web.dc.html):
    /// birth; 6, 10 and 14 weeks; 6–9 months; 9 months; 12–18 months; 16–18 months; 4–6 years. Single ages are due until the next band starts.
    /// </summary>
    sealed class IndiaSchedule : ImmunisationSchedule
    {
        static readonly ScheduleBand[] All =
        [
            new("birth", AgeAt.W(0), AgeAt.W(6), [new("BCG", 1), new("OPV", 0), new("HepB", 1)]),
            new("w6", AgeAt.W(6), AgeAt.W(10), [.. Doses(1, "DTP", "IPV", "Hib", "Rotavirus", "PCV"), new("HepB", 2)]),
            new("w10", AgeAt.W(10), AgeAt.W(14), [.. Doses(2, "DTP", "IPV", "Hib", "Rotavirus", "PCV"), new("HepB", 3)]),
            new("w14", AgeAt.W(14), AgeAt.M(6), [.. Doses(3, "DTP", "IPV", "Hib", "Rotavirus", "PCV"), new("HepB", 4)]),
            new("m6to9", AgeAt.M(6), AgeAt.M(9), [new("Influenza", 1), new("Influenza", 2), new("TCV", 1)]),
            new("m9", AgeAt.M(9), AgeAt.M(12), [new("MMR", 1)]),
            new("m12to18", AgeAt.M(12), AgeAt.M(18), [new("HepA", 1), new("MMR", 2), new("Varicella", 1), new("PCV", 4)]),
            new("m16to18", AgeAt.M(16), AgeAt.M(18), [new("DTP", 4), new("IPV", 4), new("Hib", 4)]),
            new("y4to6", AgeAt.Y(4), AgeAt.Y(6), [new("DTP", 5), new("MMR", 3)]),
        ];

        public override string Name() => "IAP 2023";
        public override IReadOnlyList<ScheduleBand> Bands() => All;
    }

    /// <summary>
    /// UAE National Immunization Program, only what MOHAP's 16 Oct 2019 update states: pneumococcal at 2, 4 and 6 months, and the hexavalent
    /// vaccine (Infanrix-hexa) at 4 months (https://mohap.gov.ae/en/w/ministry-of-health-and-prevention-adopted-4-updates-on-the-national-immunization-program).
    /// shortcut: the rest of the national schedule is not loaded (no official table could be read); load it from MOHAP's programme handbook at sign-off.
    /// </summary>
    sealed class UaeSchedule : ImmunisationSchedule
    {
        static readonly ScheduleBand[] All =
        [
            new("m2", AgeAt.M(2), AgeAt.M(4), [new("PCV", 1)]),
            new("m4", AgeAt.M(4), AgeAt.M(6), [new("Hexavalent", 1), new("PCV", 2)]),
            new("m6", AgeAt.M(6), AgeAt.M(12), [new("PCV", 3)]),
        ];

        public override string Name() => "UAE NIP";
        public override IReadOnlyList<ScheduleBand> Bands() => All;
    }
}

public sealed record MilestoneBand(
    [property: Classification(DataClass.Internal)] string Key, [property: Classification(DataClass.Internal)] AgeAt From,
    [property: Classification(DataClass.Internal)] AgeAt Until, [property: Classification(DataClass.Internal)] IReadOnlyList<string> Milestones);

/// <summary>
/// Developmental milestones per age band (MDV-P2-036). The child's band is the one whose ages hold today.
/// shortcut: only the design's 4-year band (`miles` in chartVals) is loaded; the other bands wait for the clinical reviewer to choose an
/// instrument (for example CDC 2022 or the Trivandrum chart), and until <see cref="SignOff"/> is set the screen says the list is unverified.
/// </summary>
public static class DevelopmentalMilestones
{
    /// <summary>Null: the milestone list is a draft awaiting clinical sign-off.</summary>
    public static readonly ClinicalSignOff? SignOff; // set to new(name, registration, date) when signed

    public static readonly IReadOnlyList<MilestoneBand> Bands =
    [
        new("y4", AgeAt.Y(4), AgeAt.Y(5), ["hopsOnOneFoot", "tellsAStory", "drawsAPerson", "dressesWithLittleHelp", "playsCooperatively"]),
    ];

    /// <summary>The band for a child born on <paramref name="born"/> on <paramref name="today"/>, or null when no band covers the age.</summary>
    public static MilestoneBand? BandOn(DateOnly born, DateOnly today) => Bands.FirstOrDefault(b => today >= b.From.On(born) && today < b.Until.On(born));
}

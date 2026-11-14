using Medvya.BuildingBlocks.Domain;

namespace Medvya.Platform.Domain;

/// <summary>
/// One version of a Medvya subscription plan (MDV-P5-042, ADR-019). Append-only: an edit adds the next version of the same code, and the
/// database refuses UPDATE and DELETE on this table and on <see cref="PlanPrice"/>. A limit of null means unlimited.
/// </summary>
public sealed class PlanVersion
{
    public const int FeatureKeyMax = 64, FeaturesMax = 50, TrialDaysMax = 90;

    [Classification(DataClass.Internal)] public Guid Id { get; init; } = Guid.CreateVersion7();
    /// <summary>"solo", "clinic", "hospital": the copy key on the public site (site.pricing.plan.&lt;Code&gt;.*) and the value sign-up carries.</summary>
    [Classification(DataClass.Internal)] public required string Code { get; init; }
    [Classification(DataClass.Internal)] public int Version { get; init; }
    /// <summary>The price is per doctor (Clinic) rather than per organisation.</summary>
    [Classification(DataClass.Internal)] public bool PerDoctor { get; init; }
    /// <summary>Priced by contract (Hospital): the yearly price is a starting price, and the buyer talks to Medvya instead of starting a trial.</summary>
    [Classification(DataClass.Internal)] public bool Custom { get; init; }
    [Classification(DataClass.Internal)] public int? Doctors { get; init; }
    /// <summary>Staff other than doctors, included in the price. Beyond it the owner is told to talk to Medvya (no seat price yet, ADR-019).</summary>
    [Classification(DataClass.Internal)] public int? Staff { get; init; }
    [Classification(DataClass.Internal)] public int? Locations { get; init; }
    /// <summary>Medvya Assist visits per doctor a month, included or with the add-on. Above it the clinic is warned, never blocked.</summary>
    [Classification(DataClass.Internal)] public int? AssistVisitsPerDoctorMonth { get; init; }
    [Classification(DataClass.Internal)] public int TrialDays { get; init; }
    /// <summary>Feature keys the plan includes ("ai.ambient_notes"). A key on no plan is included in every plan (ADR-019).</summary>
    [Classification(DataClass.Internal)] public required string[] Features { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    /// <summary>The console member who saved it; null for the seeded version 1.</summary>
    [Classification(DataClass.Internal)] public Guid? CreatedByMemberId { get; init; }
    [Classification(DataClass.Internal)] public string? Reason { get; init; }
    [Classification(DataClass.Internal)] public List<PlanPrice> Prices { get; init; } = [];
}

/// <summary>A plan version's price in one country deployment, in integer minor units (paise, fils) with the ISO currency. Null: no approved price.</summary>
public sealed class PlanPrice
{
    /// <summary>The country deployments and the one currency each bills in (ADR-003: a country's data and money stay in that country).</summary>
    public static readonly IReadOnlyDictionary<string, string> Currencies = new Dictionary<string, string>(StringComparer.Ordinal) { ["IN"] = "INR", ["AE"] = "AED" };

    [Classification(DataClass.Internal)] public Guid PlanVersionId { get; init; }
    [Classification(DataClass.Internal)] public required string Country { get; init; }
    [Classification(DataClass.Internal)] public required string Currency { get; init; }
    [Classification(DataClass.Internal)] public long? Monthly { get; init; }
    [Classification(DataClass.Internal)] public long? Yearly { get; init; }
    /// <summary>The Medvya Assist add-on per doctor a month, on a plan that does not include it; null where it is included.</summary>
    [Classification(DataClass.Internal)] public long? AssistMonthly { get; init; }
}

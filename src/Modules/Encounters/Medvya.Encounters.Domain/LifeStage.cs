using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>The patient's life stage: it picks the chart module, the consultation template and the safety checks (MDV-P2-032). Shown with an icon and words.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LifeStage>))]
public enum LifeStage { Adult, Child, Pregnancy, OlderAdult }

/// <summary>A history section of the chart. <see cref="BloodGroup"/> is kept the same way (author, time, earlier values) and shown in the banner.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HistorySection>))]
public enum HistorySection { PastMedical, Surgical, Family, Social, WomensHealth, Obstetric, Birth, Feeding, Function, Falls, BloodGroup }

/// <summary>
/// HISTORY_ENTRY: one saved version of a history section. Append-only: a save adds a row, so the earlier text, its author and time stay;
/// the newest row of a section is its current text (MDV-P2-032).
/// </summary>
public sealed class HistoryEntry
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public HistorySection Section { get; init; }
    [Classification(DataClass.SensitiveHealth)] public required string Text { get; init; }
    [Classification(DataClass.Internal)] public Guid RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RecordedAt { get; init; }
}

public static class LifeStages
{
    public const int AdultFromYears = 18, OlderAdultFromYears = 65;

    /// <summary>
    /// Child under 18; an open pregnancy; older adult from 65; otherwise adult. Ages are whole years on <paramref name="today"/> (the
    /// facility's date), so the 18th birthday is the first adult day. An open pregnancy wins over every age, a pregnant adolescent too: the
    /// pregnancy module and its safety checks must not be hidden behind the child one.
    /// shortcut: <paramref name="openPregnancy"/> is always false until the pregnancy record exists (MDV-P2-034), which passes it.
    /// </summary>
    public static LifeStage Of(DateOnly dateOfBirth, DateOnly today, bool openPregnancy) =>
        openPregnancy ? LifeStage.Pregnancy
        : dateOfBirth.AddYears(AdultFromYears) > today ? LifeStage.Child
        : dateOfBirth.AddYears(OlderAdultFromYears) <= today ? LifeStage.OlderAdult
        : LifeStage.Adult;
}

/// <summary>What the history sections accept (MDV-P2-032).</summary>
public static class HistoryRules
{
    public const int MaxText = 2000;

    /// <summary>The history sections the chart offers at this stage, in order. Sections already written are shown as well, whatever the stage.</summary>
    public static IReadOnlyList<HistorySection> Sections(LifeStage stage, bool female) => stage switch
    {
        LifeStage.Child => [HistorySection.Birth, HistorySection.Feeding, HistorySection.PastMedical, HistorySection.Surgical, HistorySection.Family, HistorySection.Social],
        LifeStage.Pregnancy => [HistorySection.Obstetric, HistorySection.PastMedical, HistorySection.Surgical, HistorySection.Family, HistorySection.Social],
        LifeStage.OlderAdult => [HistorySection.Function, HistorySection.Falls, HistorySection.PastMedical, HistorySection.Surgical, HistorySection.Family, HistorySection.Social,
            .. female ? [HistorySection.WomensHealth] : Array.Empty<HistorySection>()],
        _ => [HistorySection.PastMedical, HistorySection.Surgical, HistorySection.Family, HistorySection.Social, .. female ? [HistorySection.WomensHealth] : Array.Empty<HistorySection>()],
    };

    /// <summary>The blood groups the chart accepts, ABO with RhD, exactly as written.</summary>
    public static readonly IReadOnlyList<string> BloodGroups = ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];
}

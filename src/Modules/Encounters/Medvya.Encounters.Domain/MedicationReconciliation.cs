using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>What the doctor decided for one medicine. Keep, Change and Stop are for a medicine active on the chart; Add and Ignore for one only a patient or another source reports.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MedicationDecisionKind>))]
public enum MedicationDecisionKind { Keep, Change, Stop, Add, Ignore }

/// <summary>
/// One decision of a reconciliation (MDV-P2-011). Append-only: a changed mind is a new row and the earlier ones stay as history; the latest per
/// medicine of the encounter is the current one. Applied to the chart only when the doctor confirms the whole table.
/// </summary>
public sealed class MedicationDecision
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid EncounterId { get; init; }
    /// <summary>The medicine's name, lower-cased and trimmed (<see cref="MedicationReconciliationRules.Key"/>): what the sources are matched on.</summary>
    [Classification(DataClass.SensitiveHealth)] public required string MedicineKey { get; init; }
    [Classification(DataClass.SensitiveHealth)] public required string Name { get; init; }
    [Classification(DataClass.SensitiveHealth)] public MedicationDecisionKind Kind { get; init; }
    /// <summary>The chart's dose when a Change was decided, and the dose it changes to.</summary>
    [Classification(DataClass.SensitiveHealth)] public string? FromDose { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? NewDose { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Reason { get; init; }
    [Classification(DataClass.Internal)] public Guid DecidedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset DecidedAt { get; init; }
}

/// <summary>The doctor confirmed the encounter's reconciliation: every decision was applied to the chart in that moment. One per encounter.</summary>
public sealed class MedicationReconciliation
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid EncounterId { get; init; }
    [Classification(DataClass.Internal)] public Guid ConfirmedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ConfirmedAt { get; init; }
}

/// <summary>A medicine as one source says it, for the reconciliation table. Pure data, no ids.</summary>
public sealed record MedicineSide(
    [property: Classification(DataClass.SensitiveHealth)] string? Dose, [property: Classification(DataClass.SensitiveHealth)] string? Frequency,
    [property: Classification(DataClass.SensitiveHealth)] MedicationState State, [property: Classification(DataClass.Internal)] bool NeedsClarification);

/// <summary>Why the sources disagree about a medicine, as codes the screen turns into words (never colour alone).</summary>
public enum MedicineConflict { State, Dose, NotOnChart }

/// <summary>The rules of the reconciliation table (MDV-P2-011): what disagrees, what the doctor may decide, what a decision needs. Unknowns stay blank.</summary>
public static class MedicationReconciliationRules
{
    public const int MaxReason = 500, MaxDose = 100;

    public static string Key(string name) => name.Trim().ToLowerInvariant();

    /// <summary>Null when the sources agree. A blank dose is unknown, not a different dose.</summary>
    public static MedicineConflict? Conflict(MedicineSide? chart, MedicineSide? patient, MedicineSide? other)
    {
        MedicineSide[] others = [.. new[] { patient, other }.OfType<MedicineSide>()];
        if (chart is null) return MedicineConflict.NotOnChart;
        if (others.Any(o => o.State != chart.State)) return MedicineConflict.State;
        var doses = new[] { chart }.Concat(others).Select(s => s.Dose?.Trim()).Where(d => !string.IsNullOrEmpty(d)).Select(d => d!.ToLowerInvariant()).Distinct().Count();
        return doses > 1 ? MedicineConflict.Dose : null;
    }

    /// <summary>A medicine active on the chart is kept, changed or stopped; anything else (not on the chart, or stopped there) is added or ignored.</summary>
    public static IReadOnlyList<MedicationDecisionKind> Allowed(MedicineSide? chart) => chart is { State: MedicationState.Active }
        ? [MedicationDecisionKind.Keep, MedicationDecisionKind.Change, MedicationDecisionKind.Stop]
        : [MedicationDecisionKind.Add, MedicationDecisionKind.Ignore];

    /// <summary>Error codes for a decision the table would refuse (empty: fine). Stop and Change say why; Change says to what.</summary>
    public static IReadOnlyList<(string Field, string Code)> Validate(MedicationDecisionKind kind, MedicineSide? chart, string? newDose, string? reason)
    {
        var errors = new List<(string, string)>();
        if (!Enum.IsDefined(kind) || !Allowed(chart).Contains(kind)) { errors.Add(("kind", "not_allowed")); return errors; }
        if (reason?.Length > MaxReason) errors.Add(("reason", "too_long"));
        if (kind is MedicationDecisionKind.Stop or MedicationDecisionKind.Change && string.IsNullOrWhiteSpace(reason)) errors.Add(("reason", "required"));
        if (kind == MedicationDecisionKind.Change)
        {
            if (string.IsNullOrWhiteSpace(newDose)) errors.Add(("newDose", "required"));
            else if (newDose.Trim().Length > MaxDose) errors.Add(("newDose", "too_long"));
        }
        return errors;
    }
}

using Medvya.Scheduling.Domain;

namespace Medvya.Scheduling.Application;

/// <summary>
/// Desk check-in time over the window: the median for returning patients (exit gate 3: 60 s or less, else the gap is shown) and
/// for every timed check-in. A returning patient had an earlier attended visit at the practice in Medvya. Null without samples.
/// Unmeasured counts the check-ins of the window with no trustworthy time (MDV-P1-106): they are in neither median, never as zero.
/// </summary>
public sealed record CheckInMetric(double? ReturningMedianSeconds, int ReturningCount, double? MedianSeconds, int Count, int TargetSeconds, int Unmeasured);

/// <summary>Booked visits (not walk-ins) that started in the window: no-shows against visits the patient came to. Rate in percent.</summary>
public sealed record NoShowMetric(int NoShows, int Attended, double? Percent);

public sealed record BaselineView(PilotMetric Metric, int Value, string Method, DateOnly FromDate, DateOnly ToDate, DateTimeOffset RecordedAt);

/// <summary>
/// The owner's pilot metrics of one facility over [From, To) (MDV-P1-041): counts, medians and rates only, never a patient.
/// DoubleBookingRefusals counts the times the database refused a double booking. Baselines are the newest per metric.
/// </summary>
public sealed record PilotMetrics(DateTimeOffset From, DateTimeOffset To, CheckInMetric CheckIn, NoShowMetric NoShows, int DoubleBookingRefusals, IReadOnlyList<BaselineView> Baselines);

/// <summary>Records a clinic's pre-Medvya value of one metric, measured by a person, with the method and dates.</summary>
public sealed class RecordBaseline
{
    public Guid FacilityId { get; init; }
    public PilotMetric? Metric { get; init; }
    public int? Value { get; init; }
    public string? Method { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
}

public static class PilotMetricRules
{
    /// <summary>Exit gate 3: median check-in of a returning patient.</summary>
    public const int CheckInTargetSeconds = 60;

    /// <summary>Under a second no desk found a patient and issued a token: a script, a double tap or an old zero row, not a measurement.</summary>
    public const int MinCheckInMs = 1000;

    /// <summary>Past half an hour the screen was left open: not a check-in time either.</summary>
    public const int MaxCheckInMs = 30 * 60 * 1000;

    /// <summary>
    /// The desk's check-in time when it can be trusted, else null (not measured). The number comes from the client, so this is the
    /// one rule both for what is stored and for what the median reads (rows stored before the lower bound existed included).
    /// </summary>
    public static int? TimedMs(long? elapsedMs) => elapsedMs is >= MinCheckInMs and <= MaxCheckInMs ? (int)elapsedMs : null;

    /// <summary>The median, or null for no values.</summary>
    public static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return null;
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>Part of the whole in percent, one decimal; null when the whole is zero.</summary>
    public static double? Percent(int part, int whole) => whole == 0 ? null : Math.Round(100d * part / whole, 1);
}

/// <summary>Pilot metrics for the practice owner (MDV-P1-041). Practice configuration only (owner doctor, practice admin, hospital IT).</summary>
public interface IPilotMetrics
{
    /// <exception cref="SchedulingAccessDeniedException">Not allowed to configure the facility.</exception>
    Task<PilotMetrics> GetAsync(Guid facilityId, int days, CancellationToken cancellationToken = default);

    /// <exception cref="SchedulingValidationException">A field is missing or out of range.</exception>
    /// <exception cref="SchedulingAccessDeniedException">Not allowed to configure the facility.</exception>
    Task<BaselineView> RecordBaselineAsync(RecordBaseline command, CancellationToken cancellationToken = default);
}

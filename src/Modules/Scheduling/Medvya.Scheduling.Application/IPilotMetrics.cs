using Medvya.Scheduling.Domain;

namespace Medvya.Scheduling.Application;

/// <summary>
/// Desk check-in time over the window: the median for returning patients (exit gate 3: 60 s or less, else the gap is shown) and
/// for every timed check-in. A returning patient had an earlier attended visit at the practice in Medvya. Null without samples.
/// </summary>
public sealed record CheckInMetric(double? ReturningMedianSeconds, int ReturningCount, double? MedianSeconds, int Count, int TargetSeconds);

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

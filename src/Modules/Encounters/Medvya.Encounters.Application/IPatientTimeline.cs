using System.Text.Json.Serialization;
using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

[JsonConverter(typeof(JsonStringEnumConverter<TimelineKind>))]
public enum TimelineKind { Encounter, Lab, Medicine, Document }

/// <summary>
/// One line of the timeline (MDV-P2-005). <see cref="Code"/> names what it is (the screen translates it); <see cref="Title"/> and
/// <see cref="Detail"/> are the data to show with it (a medicine's name and dose, a result's value and unit).
/// </summary>
public sealed record TimelineEntry(Guid Id, TimelineKind Kind, DateTimeOffset At, string Code, string? Title, string? Detail, Guid? AuthorUserId, ChartSource Source);

/// <summary>Filters combine: kinds (none = all), one source (none = all). <see cref="Before"/> continues after the last entry of the previous page.</summary>
public sealed record TimelineQuery(IReadOnlySet<TimelineKind> Kinds, ChartSource? Source, TimelineCursor? Before, int Limit = TimelineQuery.PageSize)
{
    public const int PageSize = 20, MaxLimit = 50;
}

/// <summary>Where the previous page ended: entries are newest first, ties broken by id, so a page never repeats or skips an entry.</summary>
public sealed record TimelineCursor(DateTimeOffset At, Guid Id);

public sealed record TimelinePage(IReadOnlyList<TimelineEntry> Entries, bool HasMore);

/// <summary>
/// The longitudinal timeline and trends of one patient (MDV-P2-005). Same gate as the chart: clinical access (role, care relationship) first,
/// then the read, then the audit event with the patient.
/// </summary>
public interface IPatientTimeline
{
    Task<TimelinePage> PageAsync(Guid patientId, Guid facilityId, TimelineQuery query, CancellationToken cancellationToken = default);

    /// <summary>The last readings of HbA1c, blood pressure and weight, oldest first (more than the chart's mini-charts show).</summary>
    Task<IReadOnlyList<TrendView>> TrendsAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);
}

using Medvya.Scheduling.Domain;

namespace Medvya.Scheduling.Application;

/// <summary>Replaces one calendar's weekly template. Lists default to empty so a partial body fails validation, not binding.</summary>
public sealed record SaveTemplate
{
    public Guid FacilityId { get; init; }
    public Guid CalendarId { get; init; }
    public CalendarKind CalendarKind { get; init; }
    public int SlotMinutes { get; init; }
    public IReadOnlyList<WorkingHours> Hours { get; init; } = [];
    public IReadOnlyList<ScheduleBreak> Breaks { get; init; } = [];
    public IReadOnlyList<VisitLength> VisitLengths { get; init; } = [];
}

public sealed record AddResource
{
    public Guid FacilityId { get; init; }
    public string? Name { get; init; }
}

public sealed record ResourceView(Guid Id, Guid FacilityId, string Name);

/// <summary>A calendar of the facility (doctor or resource) with its template, or null fields when none is set yet.</summary>
public sealed record TemplateView(
    Guid CalendarId, CalendarKind CalendarKind, string Name, int? SlotMinutes,
    IReadOnlyList<WorkingHours> Hours, IReadOnlyList<ScheduleBreak> Breaks, IReadOnlyList<VisitLength> VisitLengths, DateTimeOffset? ChangedAt);

/// <summary>One column of the day view: bookable slots and unavailable blocks within the day frame.</summary>
public sealed record ScheduleColumn(
    Guid CalendarId, CalendarKind CalendarKind, string Name, int? SlotMinutes,
    IReadOnlyList<WorkingWindow> Hours, IReadOnlyList<Slot> Slots, IReadOnlyList<UnavailableBlock> Unavailable);

/// <summary>
/// The day view (P-04) of one facility. Instants are UTC; the client shows them in <see cref="TimeZone"/> (IANA), the
/// facility's zone, whatever the browser's zone is. Start and End frame the day for every column.
/// </summary>
public sealed record ScheduleDay(Guid FacilityId, string TimeZone, DateOnly Date, DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<ScheduleColumn> Columns);

/// <summary>
/// Schedule templates and the calendar (MDV-P1-011). Every call checks the user's active membership at the facility against
/// <see cref="SchedulingAccess"/>; tenant query filters and row-level security scope all data.
/// </summary>
public interface IScheduleService
{
    /// <summary>Doctor and resource columns for the facility-local date (today in the facility when null).</summary>
    /// <exception cref="SchedulingAccessDeniedException">No calendar access at this facility.</exception>
    Task<ScheduleDay> GetDayAsync(Guid facilityId, DateOnly? day, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TemplateView>> ListTemplatesAsync(Guid facilityId, CancellationToken cancellationToken = default);

    /// <exception cref="SchedulingValidationException">With field/code pairs only.</exception>
    Task<TemplateView> SaveTemplateAsync(SaveTemplate command, CancellationToken cancellationToken = default);

    Task<ResourceView> AddResourceAsync(AddResource command, CancellationToken cancellationToken = default);
}

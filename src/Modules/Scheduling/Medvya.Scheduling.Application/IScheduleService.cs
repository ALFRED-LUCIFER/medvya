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

/// <summary>An appointment on the schedule: name, type, time and state. Never the free-text reason.</summary>
public sealed record AppointmentChip(
    Guid Id, string Reference, Guid PatientId, string PatientName, VisitType VisitType, DateTimeOffset Start, DateTimeOffset End, AppointmentState State);

/// <summary>
/// A calendar of the facility (doctor or resource) with its template, or null fields when none is set yet. After a save,
/// AppointmentsOutsideHours lists future appointments the new hours no longer cover, for reception to act on.
/// </summary>
public sealed record TemplateView(
    Guid CalendarId, CalendarKind CalendarKind, string Name, int? SlotMinutes,
    IReadOnlyList<WorkingHours> Hours, IReadOnlyList<ScheduleBreak> Breaks, IReadOnlyList<VisitLength> VisitLengths, DateTimeOffset? ChangedAt,
    IReadOnlyList<AppointmentChip> AppointmentsOutsideHours);

/// <summary>A free slot. Held: someone else is booking it right now ("being booked"), so it cannot be held or booked.</summary>
public sealed record SlotView(DateTimeOffset Start, DateTimeOffset End, bool Held);

/// <summary>
/// One column of the day view: free slots, appointments and unavailable blocks within the day frame, and the length of each
/// visit type in this calendar (the booking panel shows it). Capacity is the number of template slots that day, booked or free.
/// </summary>
public sealed record ScheduleColumn(
    Guid CalendarId, CalendarKind CalendarKind, string Name, int? SlotMinutes,
    IReadOnlyList<WorkingWindow> Hours, IReadOnlyList<SlotView> Slots, IReadOnlyList<UnavailableBlock> Unavailable,
    IReadOnlyList<AppointmentChip> Appointments, IReadOnlyList<VisitLength> VisitLengths, int Capacity);

/// <summary>A week of one calendar (MDV-P1-023): seven days from Monday, each a day view with that calendar's column only.</summary>
public sealed record ScheduleWeek(Guid FacilityId, string TimeZone, Guid CalendarId, IReadOnlyList<ScheduleDay> Days);

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

    /// <summary>The Monday-to-Sunday week containing the facility-local date (this week when null) for one doctor or resource.</summary>
    /// <exception cref="SchedulingValidationException">The calendar is not a doctor or resource of the facility.</exception>
    Task<ScheduleWeek> GetWeekAsync(Guid facilityId, Guid calendarId, DateOnly? day, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TemplateView>> ListTemplatesAsync(Guid facilityId, CancellationToken cancellationToken = default);

    /// <exception cref="SchedulingValidationException">With field/code pairs only.</exception>
    Task<TemplateView> SaveTemplateAsync(SaveTemplate command, CancellationToken cancellationToken = default);

    Task<ResourceView> AddResourceAsync(AddResource command, CancellationToken cancellationToken = default);
}

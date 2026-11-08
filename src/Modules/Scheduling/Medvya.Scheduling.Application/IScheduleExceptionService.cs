using System.Text.Json.Serialization;
using Medvya.Scheduling.Domain;

namespace Medvya.Scheduling.Application;

/// <summary>What reception does for a booked visit the leave or shortened day leaves without a doctor. Offer: the patient is sent a rebooking message. Manual: the desk calls and moves it themselves.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RebookAction>))]
public enum RebookAction { Offer, Manual }

public sealed record AppointmentAction(Guid AppointmentId, RebookAction Action);

/// <summary>
/// Add leave (From..To) or a one-off extra session / shortened day (one date, Start and End facility-local). When booked visits
/// are affected, Actions must name every one of them or nothing is saved (<see cref="ExceptionActionsRequiredException"/>).
/// </summary>
public sealed record SaveAdjustment
{
    public Guid FacilityId { get; init; }
    public Guid CalendarId { get; init; }
    public ExceptionKind Kind { get; init; }
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public TimeOnly? Start { get; init; }
    public TimeOnly? End { get; init; }
    public string? Reason { get; init; }
    /// <summary>Set (by PUT) to edit this saved exception of the same calendar instead of adding one.</summary>
    public Guid? ReplacesId { get; init; }
    /// <summary>With an edit: the <see cref="ExceptionView.Version"/> the editor saw; a different saved version is refused (<c>id:stale</c>).</summary>
    public string? Version { get; init; }
    public IReadOnlyList<AppointmentAction> Actions { get; init; } = [];
}

/// <summary>
/// A saved exception. <paramref name="Reason"/> is null for staff who may not manage the calendar; <paramref name="CanEdit"/> says whether
/// the caller may change it; <paramref name="Version"/> is sent back with an edit or delete so a change made meanwhile is not overwritten.
/// </summary>
public sealed record ExceptionView(Guid Id, Guid CalendarId, ExceptionKind Kind, DateOnly FromDate, DateOnly ToDate, TimeOnly? Start, TimeOnly? End, string? Reason,
    bool CanEdit = false, string? Version = null);

public sealed record SavedAdjustment(ExceptionView Exception, int Offered, int Manual);

/// <summary>Booked visits the change leaves without a doctor: each needs an action before it can be saved.</summary>
public sealed class ExceptionActionsRequiredException(IReadOnlyList<AppointmentChip> affected) : Exception("Every affected appointment needs an action.")
{
    public IReadOnlyList<AppointmentChip> Affected { get; } = affected;
}

/// <summary>Leave and schedule exceptions (MDV-P1-012). Needs book access at the facility; audited in the change's own transaction.</summary>
public interface IScheduleExceptionService
{
    /// <summary>Current and future exceptions of the facility (of one calendar when given), by date.</summary>
    Task<IReadOnlyList<ExceptionView>> ListAsync(Guid facilityId, Guid? calendarId, CancellationToken cancellationToken = default);

    /// <exception cref="ExceptionActionsRequiredException">Booked visits are affected and not every one has an action; nothing was saved.</exception>
    /// <exception cref="SchedulingValidationException">Invalid dates or window, an overlapping exception, or an action for a visit that is not affected.</exception>
    Task<SavedAdjustment> SaveAsync(SaveAdjustment command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a saved exception: deleted leave or a shortened day gives the bookable slots back. Visits already moved to "needs rebooking"
    /// stay as they are (nobody is re-booked silently). An extra session that still has booked visits in it cannot be deleted.
    /// </summary>
    /// <exception cref="SchedulingValidationException">Unknown exception, an extra session with booked visits (<c>id:has_bookings</c>), or changed since <paramref name="version"/> (<c>id:stale</c>).</exception>
    Task DeleteAsync(Guid facilityId, Guid exceptionId, string? version, CancellationToken cancellationToken = default);
}

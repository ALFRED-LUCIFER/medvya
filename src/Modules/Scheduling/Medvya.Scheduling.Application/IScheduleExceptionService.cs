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
    public IReadOnlyList<AppointmentAction> Actions { get; init; } = [];
}

public sealed record ExceptionView(Guid Id, Guid CalendarId, ExceptionKind Kind, DateOnly FromDate, DateOnly ToDate, TimeOnly? Start, TimeOnly? End, string? Reason);

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
    /// A doctor left the practice (MDV-P1-009): their future booked visits become "needs rebooking" for reception to move (slots freed, audited,
    /// no patient message). The caller has already authorised staff administration; nothing here is open to a doctor's own session.
    /// </summary>
    Task<int> ReleaseDoctorAsync(Guid doctorUserId, CancellationToken cancellationToken = default);
}

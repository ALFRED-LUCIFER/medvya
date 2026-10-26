using Medvya.Scheduling.Domain;

namespace Medvya.Scheduling.Application;

/// <summary>
/// A visit on the reception board. Nonclinical only: the reason is its category, never the patient's words. Name and date of
/// birth are there to confirm identity at check-in; RegistrationIncomplete flags a record to finish at the desk. Since is
/// when the visit entered its current state (the "Waiting 12 min" timer); Room is where the patient was called in to; Token is
/// the waiting-room token issued at check-in (MDV-P1-031).
/// </summary>
public sealed record BoardCard(
    Guid Id, string Reference, Guid PatientId, string PatientName, DateOnly? DateOfBirth, bool RegistrationIncomplete,
    Guid CalendarId, string CalendarName, VisitType VisitType, ReasonCategory ReasonCategory,
    DateTimeOffset Start, DateTimeOffset End, AppointmentState State, DateTimeOffset Since, string? Room, string? Token, bool WalkIn = false, bool Unscheduled = false);

/// <summary>
/// Today's board (P-01) of one facility: every visit of the facility-local day that is not cancelled or a no-show, in time
/// order. Now is the server's clock, so wait timers do not depend on the device's clock.
/// </summary>
public sealed record FrontDeskBoard(Guid FacilityId, string TimeZone, DateOnly Date, DateTimeOffset Now, IReadOnlyList<BoardCard> Cards);

/// <summary>
/// Check in a visit. ElapsedMs is how long the desk took from tapping "Check in" to confirming (exit gate 3 measures the
/// median); it is a number only, never patient data.
/// </summary>
public sealed record CheckIn
{
    public long? ElapsedMs { get; init; }
}

/// <summary>Move a visit to the next column of the board. Nullable so a missing state fails validation instead of meaning "Booked".</summary>
public sealed record MoveVisit
{
    public AppointmentState? State { get; init; }
}

/// <summary>
/// The reception board (MDV-P1-028). Moves follow <see cref="VisitFlow"/>; each move and its status event are written in one
/// transaction, and repeating a move that already happened changes nothing (a double tap is not a second check-in).
/// </summary>
public interface IFrontDeskService
{
    /// <exception cref="SchedulingAccessDeniedException">No board access at this facility.</exception>
    Task<FrontDeskBoard> GetBoardAsync(Guid facilityId, CancellationToken cancellationToken = default);

    /// <exception cref="AppointmentNotFoundException">No such appointment in this tenant.</exception>
    /// <exception cref="VisitTransitionException">Not booked any more, or not today.</exception>
    Task<BoardCard> CheckInAsync(Guid appointmentId, CheckIn command, CancellationToken cancellationToken = default);

    /// <exception cref="VisitTransitionException">Not the next step from the visit's current state.</exception>
    Task<BoardCard> MoveAsync(Guid appointmentId, MoveVisit command, CancellationToken cancellationToken = default);

    /// <summary>The real-time group of the facility's board, after the same access check as <see cref="GetBoardAsync"/>.</summary>
    /// <exception cref="SchedulingAccessDeniedException">No board access at this facility.</exception>
    Task<string> WatchGroupAsync(Guid facilityId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A visit changed on a facility's board: ids, state and time only, never patient data. Boards refetch on it (MDV-P1-029).
/// TenantId and FacilityId route the message; they are not sent.
/// </summary>
public sealed record BoardChange(Guid TenantId, Guid FacilityId, Guid AppointmentId, AppointmentState State, DateTimeOffset ChangedAt);

/// <summary>Pushes board changes to the open boards of the facility (SignalR in the host; nothing without it).</summary>
public interface IBoardNotifier
{
    Task ChangedAsync(BoardChange change, CancellationToken cancellationToken = default);
}

/// <summary>Real-time groups. Opaque ids only, and the tenant is part of the name, so a group never spans tenants.</summary>
public static class BoardGroups
{
    public static string Of(Guid tenantId, Guid facilityId) => $"board:{tenantId:N}:{facilityId:N}";
}

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

/// <summary>Move a waiting visit to another doctor's queue today (MDV-P1-030), for one of the listed reasons.</summary>
public sealed record ReassignVisit
{
    public Guid? CalendarId { get; init; }
    public ReassignReason? Reason { get; init; }
}

/// <summary>
/// Clinic overview (D-04, MDV-P1-030): every doctor working today with visits per state, how late they are running and their
/// next patients, and the shared waiting room. Nonclinical, like the board: names to call patients, tokens, times and states.
/// </summary>
public sealed record ClinicOverview(Guid FacilityId, string TimeZone, DateTimeOffset Now, IReadOnlyList<DoctorLoad> Doctors, IReadOnlyList<WaitingVisit> Waiting);

/// <summary>One doctor's day: visits per board state, minutes running late (an estimate, <see cref="ClinicDelay"/>), next patients.</summary>
public sealed record DoctorLoad(Guid CalendarId, string Name, IReadOnlyDictionary<AppointmentState, int> Counts, int DelayMinutes, IReadOnlyList<NextVisit> Next);

public sealed record NextVisit(Guid Id, DateTimeOffset Start, AppointmentState State, string PatientName, string? Token);

/// <summary>A patient in the waiting room (checked in, with the nurse or ready), in whose queue, since when.</summary>
public sealed record WaitingVisit(Guid Id, string? Token, string PatientName, Guid CalendarId, AppointmentState State, DateTimeOffset Start, DateTimeOffset Since);

/// <summary>
/// How late a doctor is running, in whole minutes: the later of how late the current consultation started and how long the
/// earliest waiting patient is past their appointment time; 0 when on time.
/// shortcut: from the board's states and times only; learn per-doctor consultation lengths when visit timings exist (P2).
/// </summary>
public static class ClinicDelay
{
    public static int Minutes(IEnumerable<(DateTimeOffset Start, AppointmentState State, DateTimeOffset Since)> visits, DateTimeOffset now)
    {
        var list = visits.ToList();
        var consult = list.Where(v => v.State == AppointmentState.InConsult).Select(v => v.Since - v.Start).DefaultIfEmpty(TimeSpan.Zero).Max();
        var waiting = list.Where(v => VisitFlow.CanReassign(v.State) && v.Start <= now).Select(v => now - v.Start).DefaultIfEmpty(TimeSpan.Zero).Max();
        return (int)Math.Max(0, Math.Max(consult.TotalMinutes, waiting.TotalMinutes));
    }
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

    /// <exception cref="SchedulingAccessDeniedException">No board access at this facility.</exception>
    Task<ClinicOverview> GetOverviewAsync(Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>Moves a waiting visit to another active doctor of the facility, atomically and audited; its token stays.</summary>
    /// <exception cref="AppointmentNotFoundException">No such appointment in this tenant.</exception>
    /// <exception cref="VisitTransitionException">Not waiting (any more), not today, or changed by someone else.</exception>
    Task<BoardCard> ReassignAsync(Guid appointmentId, ReassignVisit command, CancellationToken cancellationToken = default);
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

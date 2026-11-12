using System.Data.Common;
using Medvya.Scheduling.Domain;

namespace Medvya.Scheduling.Application;

/// <summary>What the Encounters module needs of a visit: who, where, its state and room (ids and codes only).</summary>
public sealed record ConsultVisit(Guid Id, Guid FacilityId, Guid PatientId, AppointmentState State, string? Room, DateTimeOffset Start);

/// <summary>
/// The visit side of a consultation (MDV-P2-001). The Encounters module never reads scheduling tables; it asks here. Start and finish run
/// inside the encounter's transaction on its connection, so the encounter and the board state commit or roll back together, and return
/// what to tell the open boards after the commit.
/// </summary>
public interface IConsultVisits
{
    /// <summary>The visit, or null when it is not one of the tenant's.</summary>
    Task<ConsultVisit?> FindAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The visit the doctor could start now (MDV-1089): this patient's, at this facility, on this doctor's own calendar, today, and still waiting
    /// (checked in, with the nurse or ready). Null when there is none; the earliest when there are several. The start itself checks clinical access again.
    /// </summary>
    Task<Guid?> StartableTodayAsync(Guid patientId, Guid facilityId, Guid calendarId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The doctor calls the patient in: a waiting visit (checked in, with the nurse or ready) moves to In consult with the room of the
    /// doctor's session; a visit already in consult is left as it is.
    /// </summary>
    /// <exception cref="VisitTransitionException">"not_arrived", "finished" or "not_today".</exception>
    Task<ConsultVisit> StartAsync(Guid appointmentId, DbTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>The consultation is over: In consult moves on to Billing. A visit someone already moved on is left as it is.</summary>
    Task<ConsultVisit> FinishAsync(Guid appointmentId, DbTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// The nurse hands the patient to the doctor (MDV-P2-007): With nurse moves to Ready for doctor; a visit already there is left as it is.
    /// The one way past <see cref="IHandoffGate"/>: the intake use case has checked its own blockers first.
    /// </summary>
    /// <exception cref="VisitTransitionException">"invalid_transition" from any other state.</exception>
    Task<ConsultVisit> HandOffAsync(Guid appointmentId, DbTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>Tells the facility's open boards about the visit's current state (after the commit; a failed push loses nothing).</summary>
    Task NotifyAsync(ConsultVisit visit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Another module's say over the board's With nurse to Ready for doctor step (MDV-P2-007). When any gate refuses, the generic move
/// (status endpoint, overview) is refused with "intake_required", so the handoff cannot be done around the nurse intake.
/// </summary>
public interface IHandoffGate
{
    /// <summary>True when the visit may move on from the nurse without the nurse handing it off (the gate is off for this tenant, or it was handed off).</summary>
    Task<bool> MayMoveOnAsync(Guid appointmentId, CancellationToken cancellationToken = default);
}

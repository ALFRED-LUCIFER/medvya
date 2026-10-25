using Medvya.Scheduling.Domain;

namespace Medvya.Scheduling.Application;

/// <summary>
/// Scheduling settings. SlotHold is how long a slot stays reserved while the booking form is open. LockTimeout is how long one
/// booking attempt waits for the doctor-day lock before it is retried (a few attempts, then "busy").
/// </summary>
public sealed class SchedulingOptions
{
    public TimeSpan SlotHold { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Reserve a slot (and the slots a longer visit type needs) while the booking form is open.</summary>
public sealed record HoldSlot
{
    public Guid FacilityId { get; init; }
    public Guid CalendarId { get; init; }
    public DateTimeOffset Start { get; init; }
    public VisitType VisitType { get; init; }
}

public sealed record HoldView(Guid Id, Guid FacilityId, Guid CalendarId, VisitType VisitType, DateTimeOffset Start, DateTimeOffset End, DateTimeOffset ExpiresAt);

/// <summary>Confirm the caller's hold as an appointment.</summary>
public sealed record BookAppointment
{
    public Guid HoldId { get; init; }
    /// <summary>Opaque id of a patient registered at the hold's facility, chosen with front-desk search (MDV-P1-018).</summary>
    public Guid PatientId { get; init; }
    public ReasonCategory ReasonCategory { get; init; }
    /// <summary>Optional, in the patient's words; stored for clinicians and never returned to reception screens.</summary>
    public string? ReasonText { get; init; }
    public BookingChannel Channel { get; init; }
}

/// <summary>An appointment as reception sees it: no free-text reason.</summary>
public sealed record AppointmentView(
    Guid Id, string Reference, Guid FacilityId, Guid CalendarId, Guid PatientId, string PatientName, string Mrn,
    VisitType VisitType, DateTimeOffset Start, DateTimeOffset End, AppointmentState State, ReasonCategory ReasonCategory, BookingChannel Channel);

public sealed record PatientView(Guid Id, string FullName, string Mrn);

/// <summary>Move a booked visit to another slot: a calendar of the same kind at the visit's facility, and a start of its template.</summary>
public sealed record RescheduleAppointment
{
    public Guid CalendarId { get; init; }
    public DateTimeOffset Start { get; init; }
}

/// <summary>Cancel with a reason from the list; the note is optional except for Other.</summary>
public sealed record CancelAppointment
{
    public CancelReason? Reason { get; init; }
    /// <summary>Up to 500 characters; may mention health, so it is never shown on reception screens.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// Holding slots and booking (MDV-P1-021). A hold belongs to one user, covers every slot of the visit type and expires after
/// <see cref="SchedulingOptions.SlotHold"/>; booking needs an unexpired hold of the caller and re-checks the template.
/// </summary>
public interface IBookingService
{
    /// <exception cref="SlotUnavailableException">Taken, being booked, not a slot of the template or in the past.</exception>
    Task<HoldView> HoldAsync(HoldSlot command, CancellationToken cancellationToken = default);

    /// <summary>Releases the caller's hold; releasing a hold that is gone does nothing.</summary>
    Task ReleaseHoldAsync(Guid holdId, CancellationToken cancellationToken = default);

    /// <exception cref="SlotUnavailableException">The hold expired, is not the caller's, or the slot is no longer bookable.</exception>
    Task<AppointmentView> BookAsync(BookAppointment command, CancellationToken cancellationToken = default);

    /// <summary>Name and MRN of a patient of this tenant, to confirm the patient before booking; null when unknown.</summary>
    Task<PatientView?> FindPatientAsync(Guid facilityId, Guid patientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a booked visit to another slot (MDV-P1-024) on the booking path: the doctor-days of both slots locked, the new slot
    /// re-checked against the template, other appointments and other users' holds, the old slot freed and the new one taken in
    /// one transaction, with the history (old slot, new slot, who, when) and AppointmentRescheduled.v1.
    /// </summary>
    /// <exception cref="AppointmentNotFoundException"/>
    /// <exception cref="VisitTransitionException">Not Booked ("invalid_transition"), or changed meanwhile ("changed").</exception>
    /// <exception cref="SlotUnavailableException">The new slot is taken, being booked, not a slot, in the past, or the calendar busy.</exception>
    Task<AppointmentChip> RescheduleAsync(Guid appointmentId, RescheduleAppointment command, CancellationToken cancellationToken = default);

    /// <summary>Cancels a Booked or Checked-in visit with a reason and frees its slots (MDV-P1-024, MDV-P1-022).</summary>
    /// <exception cref="SchedulingValidationException">Reason missing or invalid, note too long, or Other without a note.</exception>
    /// <exception cref="VisitTransitionException">Not Booked or Checked in ("invalid_transition"), or changed meanwhile ("changed").</exception>
    Task<AppointmentChip> CancelAsync(Guid appointmentId, CancelAppointment command, CancellationToken cancellationToken = default);

    /// <summary>Marks a Booked visit whose start has passed as a no-show and frees its slots; the desk is then offered a rebook.</summary>
    /// <exception cref="VisitTransitionException">Not Booked ("invalid_transition"), not started yet ("not_started"), or changed meanwhile ("changed").</exception>
    Task<AppointmentChip> MarkNoShowAsync(Guid appointmentId, CancellationToken cancellationToken = default);
}

public static class AppointmentReference
{
    /// <summary>Short reference for the desk, e.g. "APT-7F3A91C2": the random tail of the id, not a count of visits.</summary>
    public static string Of(Guid id) => "APT-" + id.ToString("N")[^8..].ToUpperInvariant();
}

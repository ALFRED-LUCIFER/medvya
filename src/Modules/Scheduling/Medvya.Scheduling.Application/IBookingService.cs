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
    /// <summary>How long a slot offered to a waitlisted patient (MDV-P1-027) stays reserved before the next candidate can be offered.</summary>
    public TimeSpan OfferHold { get; set; } = TimeSpan.FromMinutes(30);
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

/// <summary>
/// A walk-in (MDV-P1-025): the patient is at the desk now. CalendarId null means "next available doctor". The visit takes the
/// next free slot today through the booking path; with none free it is queued unscheduled under the chosen doctor.
/// </summary>
public sealed record WalkIn
{
    public Guid FacilityId { get; init; }
    public Guid? CalendarId { get; init; }
    public Guid PatientId { get; init; }
    public VisitType VisitType { get; init; } = VisitType.FollowUp;
    public ReasonCategory ReasonCategory { get; init; }
    /// <summary>
    /// False (the default): refused with "already_today" when the patient already has an open visit today at this facility (booked or
    /// not yet finished), so the desk is warned before a second visit is made (MDV-P1-052). True once the desk confirms it.
    /// </summary>
    public bool AllowRepeat { get; init; }
}

/// <summary>An appointment as reception sees it: no free-text reason.</summary>
public sealed record AppointmentView(
    Guid Id, string Reference, Guid FacilityId, Guid CalendarId, Guid PatientId, string PatientName, string Mrn,
    VisitType VisitType, DateTimeOffset Start, DateTimeOffset End, AppointmentState State, ReasonCategory ReasonCategory, BookingChannel Channel,
    bool WalkIn = false, bool Unscheduled = false);

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
/// A recurring series (MDV-P1-026): the same local time every <see cref="IntervalWeeks"/> weeks, or on the same date each month when
/// <see cref="Monthly"/> (then <see cref="IntervalWeeks"/> is ignored), <see cref="Count"/> visits.
/// </summary>
public sealed record SeriesPattern
{
    public const int MaxCount = 52, MaxIntervalWeeks = 12;
    public Guid FacilityId { get; init; }
    public Guid CalendarId { get; init; }
    public DateTimeOffset FirstStart { get; init; }
    public VisitType VisitType { get; init; } = VisitType.FollowUp;
    public int IntervalWeeks { get; init; } = 1;
    public bool Monthly { get; init; }
    /// <summary>When not empty: the visits fall on these days of the week (every <see cref="IntervalWeeks"/>-th week) instead; not with <see cref="Monthly"/>.</summary>
    public IReadOnlyList<DayOfWeek> Weekdays { get; init; } = [];
    public int Count { get; init; }
}

/// <summary>One planned occurrence; <paramref name="Conflict"/> is null when it can be booked, else the code (e.g. slot_unavailable, closed).</summary>
public sealed record SeriesOccurrence(int Index, DateTimeOffset Start, string? Conflict);

/// <summary>Book the occurrences the desk confirmed after the preview: skipped ones are left out, moved ones carry their new start.</summary>
public sealed record BookSeries
{
    public Guid FacilityId { get; init; }
    public Guid CalendarId { get; init; }
    public Guid PatientId { get; init; }
    public VisitType VisitType { get; init; } = VisitType.FollowUp;
    public ReasonCategory ReasonCategory { get; init; }
    public string? ReasonText { get; init; }
    public BookingChannel Channel { get; init; }
    public IReadOnlyList<DateTimeOffset> Starts { get; init; } = [];
}

/// <summary>Each requested start, booked (<paramref name="Appointment"/>) or refused with <paramref name="Conflict"/>.</summary>
public sealed record SeriesBooked(DateTimeOffset Start, AppointmentView? Appointment, string? Conflict);

public sealed record SeriesResult(Guid SeriesId, IReadOnlyList<SeriesBooked> Occurrences);

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

    /// <summary>
    /// With an <paramref name="idempotencyKey"/> (MDV-P1-047), a repeat of the same request by the same user within 24 hours returns
    /// the first appointment and books nothing; the same key with another request throws <see cref="IdempotencyKeyException"/>.
    /// </summary>
    /// <exception cref="SlotUnavailableException">The hold expired, is not the caller's, or the slot is no longer bookable.</exception>
    Task<AppointmentView> BookAsync(BookAppointment command, string? idempotencyKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// A walk-in (MDV-P1-025): books the earliest free slot from now today of the chosen doctor (or of any active doctor) on the
    /// same path as <see cref="BookAsync"/> (doctor-day lock, template and overlap re-check, SCHEDULE_SLOT rows); with no free
    /// slot, queues it unscheduled under the chosen doctor, or for "next available" the least busy doctor working today (no slot
    /// taken, so no double booking). The visit is Booked and tagged walk-in; the desk then checks it in through the board's check-in.
    /// </summary>
    /// <exception cref="SlotUnavailableException">"no_free_slot": nothing free today, no doctor chosen and nobody working today; or "busy".</exception>
    Task<AppointmentView> WalkInAsync(WalkIn command, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Recurring appointments (MDV-P1-026): the occurrences of a pattern with the ones that cannot be booked, so the desk can skip or
    /// move them before confirming. A read; nothing is held.
    /// </summary>
    /// <exception cref="SchedulingValidationException">Count or interval out of range, an unknown visit type, or invalid weekdays.</exception>
    Task<IReadOnlyList<SeriesOccurrence>> PreviewSeriesAsync(SeriesPattern pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Books each confirmed start as an independent appointment on the booking path (doctor-day lock, template and overlap re-check,
    /// SCHEDULE_SLOT rows), all with one series id, in one transaction: all are booked or none. When any start is unavailable (taken meanwhile, past, closed, busy)
    /// nothing is saved and every start is returned with its conflict ("series_not_booked" for a free one held back by the others).
    /// </summary>
    Task<SeriesResult> BookSeriesAsync(BookSeries command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels this visit and every later Booked or Checked-in visit of its series with one reason; the patient gets one
    /// notification (the first visit's), the rest only clear their queued reminders. Returns the visits cancelled.
    /// </summary>
    /// <exception cref="SchedulingValidationException">Reason missing or invalid, or the visit is not in a series ("seriesId").</exception>
    /// <exception cref="VisitTransitionException">This visit cannot be cancelled.</exception>
    Task<IReadOnlyList<AppointmentChip>> CancelFollowingAsync(Guid appointmentId, CancelAppointment command, CancellationToken cancellationToken = default);

    /// <summary>Marks a Booked visit whose start has passed as a no-show and frees its slots; the desk is then offered a rebook.</summary>
    /// <exception cref="VisitTransitionException">Not Booked ("invalid_transition"), not started yet ("not_started"), or changed meanwhile ("changed").</exception>
    Task<AppointmentChip> MarkNoShowAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The visit's moves, oldest first (MDV-P1-024): each reschedule and each same-day move to another doctor's queue (MDV-P1-030),
    /// with the old and new slot, who moved it and when. Calendar and staff display names, times and a reason code only.
    /// </summary>
    /// <exception cref="AppointmentNotFoundException">No such appointment in this tenant.</exception>
    /// <exception cref="SchedulingAccessDeniedException">No calendar access at the visit's facility.</exception>
    Task<IReadOnlyList<RescheduleView>> HistoryAsync(Guid appointmentId, CancellationToken cancellationToken = default);
}

/// <summary>One move of a visit. Names are empty for a calendar or person no longer known; ReassignReason is set for a queue move.</summary>
public sealed record RescheduleView(
    string FromCalendarName, DateTimeOffset FromStart, DateTimeOffset FromEnd, string ToCalendarName, DateTimeOffset ToStart, DateTimeOffset ToEnd,
    string ActorName, DateTimeOffset OccurredAt, ReassignReason? ReassignReason);

public static class AppointmentReference
{
    /// <summary>Short reference for the desk, e.g. "APT-7F3A91C2": the random tail of the id, not a count of visits.</summary>
    public static string Of(Guid id) => "APT-" + id.ToString("N")[^8..].ToUpperInvariant();
}

using System.Data.Common;
using Medvya.Scheduling.Domain;

namespace Medvya.Scheduling.Application;

/// <summary>
/// Add a patient to the waitlist (MDV-P1-027). <see cref="Days"/> are the days that suit (empty = any), <see cref="From"/> and
/// <see cref="Until"/> a window for the start of the visit in the facility's time (null = no limit); CalendarId null = any doctor.
/// </summary>
public sealed record AddToWaitlist
{
    public Guid FacilityId { get; init; }
    public Guid PatientId { get; init; }
    public Guid? CalendarId { get; init; }
    public VisitType VisitType { get; init; } = VisitType.FollowUp;
    public ReasonCategory ReasonCategory { get; init; }
    public IReadOnlyList<DayOfWeek> Days { get; init; } = [];
    public TimeOnly? From { get; init; }
    public TimeOnly? Until { get; init; }
}

/// <summary>Hold a free slot for a waitlisted patient and tell them. The slot is the one the desk saw as <see cref="WaitlistView.Match"/>.</summary>
public sealed record OfferSlot
{
    public Guid CalendarId { get; init; }
    public DateTimeOffset Start { get; init; }
}

/// <summary>A free slot that suits a waitlisted patient.</summary>
public sealed record SlotMatch(Guid CalendarId, string CalendarName, DateTimeOffset Start);

/// <summary>
/// A waitlist entry as reception sees it: name and MRN, preferences, state. <see cref="Offered"/> is the slot held while the
/// offer is open; <see cref="Match"/> is the earliest free slot that suits a waiting patient, null when none (the "Offer" button).
/// </summary>
public sealed record WaitlistView(
    Guid Id, Guid PatientId, string PatientName, string Mrn, Guid? CalendarId, string? CalendarName, VisitType VisitType, ReasonCategory ReasonCategory,
    IReadOnlyList<DayOfWeek> Days, TimeOnly? From, TimeOnly? Until, WaitlistState State, DateTimeOffset RequestedAt,
    SlotMatch? Offered, DateTimeOffset? OfferExpiresAt, SlotMatch? Match, string? FollowUp = null, DateOnly? DueOn = null);

public sealed record WaitlistList(Guid FacilityId, string TimeZone, DateTimeOffset Now, IReadOnlyList<WaitlistView> Entries);

/// <summary>No waitlist entry with this id in the tenant.</summary>
public sealed class WaitlistNotFoundException() : Exception("Waitlist entry not found.");

/// <summary>
/// The waitlist (MDV-P1-027). Everything needs full "Register / book patients" access at the entry's facility. An offer holds one
/// slot on the booking path (doctor-day lock, template and overlap re-check) for <see cref="SchedulingOptions.OfferHold"/>; accepting
/// books it on the same path; an offer that runs out frees the slot and the entry can be offered the next one.
/// </summary>
public interface IWaitlistService
{
    /// <summary>Waiting and offered entries of the facility, oldest request first; each waiting one with the earliest free slot that suits it.</summary>
    Task<WaitlistList> ListAsync(Guid facilityId, CancellationToken cancellationToken = default);

    /// <exception cref="SchedulingValidationException">Unknown patient, doctor or visit type, or a window that ends before it starts.</exception>
    Task<WaitlistView> AddAsync(AddToWaitlist command, CancellationToken cancellationToken = default);

    /// <exception cref="WaitlistNotFoundException"/>
    /// <exception cref="VisitTransitionException">Already booked or removed ("invalid_transition"), or changed meanwhile ("changed").</exception>
    /// <exception cref="SlotUnavailableException">The slot is taken, held, in the past or does not suit the entry ("not_a_match").</exception>
    Task<WaitlistView> OfferAsync(Guid id, OfferSlot command, CancellationToken cancellationToken = default);

    /// <summary>The patient said no (or the desk gave up): the slot is released and the patient waits for the next one.</summary>
    Task<WaitlistView> DeclineAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The patient said yes: books the held slot on the booking path and removes the patient from the waitlist.</summary>
    /// <exception cref="SlotUnavailableException">No open offer ("no_offer"), the offer ran out ("offer_expired"), or the slot is gone.</exception>
    Task<AppointmentView> AcceptAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Takes the patient off the list, releasing any held slot.</summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>A consultation's follow-up as the waitlist holds it: the period chosen, the day from which a slot suits, and where the desk is with it.</summary>
public sealed record FollowUpBooking(string Period, DateOnly DueOn, WaitlistState State);

/// <summary>
/// The doctor's follow-up choice (MDV-P2-043) as a booking for the front desk to make: one waitlist entry per consultation, for the same doctor and
/// reason, matched only to slots from its due day. The Encounters module never reads scheduling tables; it asks here. No access check: the
/// consultation use case has checked clinical access and audits; writes run on its connection and transaction, so both commit together.
/// </summary>
public interface IFollowUpBookings
{
    Task<FollowUpBooking?> FindAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>Sets the consultation's follow-up (re-opening one the desk removed), or with a null period takes it off the list.</summary>
    /// <exception cref="VisitTransitionException">"follow_up_booked" (the desk already booked it) or "already_waiting" (the patient is on the waitlist for another reason).</exception>
    Task<FollowUpBooking?> SetAsync(Guid appointmentId, Guid encounterId, string? period, DateOnly? dueOn, DbTransaction transaction, CancellationToken cancellationToken = default);
}

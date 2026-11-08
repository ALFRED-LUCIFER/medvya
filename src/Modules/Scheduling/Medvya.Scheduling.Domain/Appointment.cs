using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Scheduling.Domain;

/// <summary>Appointment / visit states (docs/03-architecture/state-machines.md). Reception sees them as nonclinical statuses.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppointmentState>))]
public enum AppointmentState { Booked, CheckedIn, WithNurse, ReadyForDoctor, InConsult, Billing, Done, Cancelled, NoShow, NeedsRebooking }

/// <summary>How the booking reached the desk.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BookingChannel>))]
public enum BookingChannel { InPerson, Phone, WhatsApp }

/// <summary>
/// Nonclinical reason buckets shown on reception screens instead of the free-text reason.
/// shortcut: a fixed list; make it clinic-configurable with practice settings (MDV-P1-005).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReasonCategory>))]
public enum ReasonCategory { General, ChronicReview, Results, PostDischarge, Screening, Vaccination, Paperwork, Other }

/// <summary>
/// Why a visit was cancelled (MDV-P1-024): a short list, with a free-text note that Other requires.
/// shortcut: a fixed list; make it clinic-configurable with practice settings (MDV-P1-005).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CancelReason>))]
public enum CancelReason { PatientRequest, PatientUnwell, DoctorUnavailable, BookedInError, ClinicClosed, Other }

/// <summary>
/// Why a waiting patient moved to another doctor's queue (MDV-P1-030). A fixed list only, no free text, so no clinical detail
/// can be typed into it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReassignReason>))]
public enum ReassignReason { DoctorRunningLate, DoctorUnavailable, PatientRequest, BalanceWorkload }

/// <summary>A booked visit in one calendar (doctor or resource) at one location. Start and End are UTC instants.</summary>
public sealed class Appointment
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    /// <summary>Changes only by reschedule (MDV-P1-024), to a calendar of the same kind.</summary>
    [Classification(DataClass.Internal)] public Guid CalendarId { get; set; }
    [Classification(DataClass.Internal)] public CalendarKind CalendarKind { get; init; }
    /// <summary>Opaque id of the patient in the Patients module.</summary>
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    /// <summary>Set while a patient merge (MDV-P1-017) holds this visit on the surviving record: the record it came from, so an undo moves it back.</summary>
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public VisitType VisitType { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset Start { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset End { get; set; }
    [Classification(DataClass.Internal)] public AppointmentState State { get; set; }
    /// <summary>The room the patient was called in to (call-in, MDV-P1-029); shown on the board and the queue TV.</summary>
    [Classification(DataClass.Internal)] public string? Room { get; set; }
    [Classification(DataClass.Internal)] public ReasonCategory ReasonCategory { get; set; }
    /// <summary>The patient's own words. Clinicians only, never on reception screens, never in logs or events.</summary>
    [Classification(DataClass.SensitiveHealth)] public string? ReasonText { get; set; }
    [Classification(DataClass.Internal)] public BookingChannel Channel { get; init; }
    /// <summary>The patient arrived without an appointment (MDV-P1-025): the board tags the card "Walk-in".</summary>
    [Classification(DataClass.Internal)] public bool WalkIn { get; init; }
    /// <summary>
    /// A walk-in queued with no free slot (MDV-P1-025): it occupies no slot (no SCHEDULE_SLOT rows, zero length at arrival), so
    /// it never blocks or double-books the calendar, and it is not drawn on the schedule; it waits on the board.
    /// </summary>
    [Classification(DataClass.Internal)] public bool Unscheduled { get; set; }
    /// <summary>The recurring series this visit belongs to (MDV-P1-026); each occurrence is still an independent appointment.</summary>
    [Classification(DataClass.Internal)] public Guid? SeriesId { get; init; }
    [Classification(DataClass.Internal)] public Guid? BookedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset BookedAt { get; init; }
    /// <summary>SHA-256 of the booking's Idempotency-Key (MDV-P1-047), unique per tenant and booker; cleared after 24 hours. Never the key itself.</summary>
    [Classification(DataClass.Internal)] public string? IdempotencyKeyHash { get; set; }
    /// <summary>Salted hash of the booking request, to refuse the same key with a different request. Cleared with the key.</summary>
    [Classification(DataClass.Internal)] public string? IdempotencyRequestHash { get; set; }
    [Classification(DataClass.Internal)] public Guid? ChangedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }

    /// <summary>States that occupy their slots; a cancelled, no-show or needs-rebooking visit frees them.</summary>
    public static bool Occupies(AppointmentState state) => state is not (AppointmentState.Cancelled or AppointmentState.NoShow or AppointmentState.NeedsRebooking);
}

/// <summary>
/// The visit state machine (docs/03-architecture/state-machines.md): one step forward at a time along the reception board,
/// so check-in is only allowed from Booked. Cancel, no-show and reschedule are separate actions (MDV-P1-024).
/// </summary>
public static class VisitFlow
{
    static readonly AppointmentState[] Order =
    [
        AppointmentState.Booked, AppointmentState.CheckedIn, AppointmentState.WithNurse, AppointmentState.ReadyForDoctor,
        AppointmentState.InConsult, AppointmentState.Billing, AppointmentState.Done,
    ];

    /// <summary>The board's next column, or null at the end of the visit and for cancelled or no-show visits.</summary>
    public static AppointmentState? Next(AppointmentState state)
    {
        var i = Array.IndexOf(Order, state);
        return i >= 0 && i < Order.Length - 1 ? Order[i + 1] : null;
    }

    public static bool CanMove(AppointmentState from, AppointmentState to) => Next(from) == to;

    /// <summary>The board's columns in order.</summary>
    public static IReadOnlyList<AppointmentState> BoardStates => Order;

    /// <summary>Only a visit that has not started moves to another slot (Booked or Needs rebooking → Booked, history kept).</summary>
    public static bool CanReschedule(AppointmentState state) => state is AppointmentState.Booked or AppointmentState.NeedsRebooking;

    /// <summary>
    /// Cancel before the clinical part of the visit: Booked, or Checked in when the patient leaves before being seen. From With
    /// nurse on the visit is finished on the board, and a completed, cancelled or no-show visit cannot be cancelled.
    /// </summary>
    public static bool CanCancel(AppointmentState state) => state is AppointmentState.Booked or AppointmentState.CheckedIn or AppointmentState.NeedsRebooking;

    /// <summary>Only a booked visit that never checked in is a no-show (and only once it has started: see the use case).</summary>
    public static bool CanMarkNoShow(AppointmentState state) => state == AppointmentState.Booked;

    /// <summary>The doctor may call the patient in from any waiting state, or reopen one already in consult (MDV-P2-001).</summary>
    public static bool CanStartConsult(AppointmentState state) => CanReassign(state) || state == AppointmentState.InConsult;

    /// <summary>Waiting for a doctor (checked in, with the nurse or ready): may move to another doctor's queue (MDV-P1-030).</summary>
    public static bool CanReassign(AppointmentState state) => state is AppointmentState.CheckedIn or AppointmentState.WithNurse or AppointmentState.ReadyForDoctor;
}

/// <summary>
/// A short reservation of a slot while one receptionist completes the booking form (MDV-P1-021). Others see the slot as
/// "being booked" and cannot hold or book it until it is confirmed, released or expires. One active hold per user.
/// </summary>
public sealed class SlotHold
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid CalendarId { get; init; }
    [Classification(DataClass.Internal)] public VisitType VisitType { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset Start { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset End { get; init; }
    [Classification(DataClass.Internal)] public Guid HeldBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// SCHEDULE_SLOT: one 5-minute unit of time an appointment occupies in one calendar at one location (MDV-P1-022). The unique
/// index on (calendar, location, slot start) lets the database refuse any second appointment that shares even five minutes,
/// whatever slot grid either was booked on. Rows exist only while the appointment occupies its time.
/// </summary>
public sealed class ScheduleSlot
{
    public const int UnitMinutes = 5;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid CalendarId { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset SlotStart { get; init; }
    [Classification(DataClass.Internal)] public Guid AppointmentId { get; init; }

    /// <summary>The 5-minute units of [start, end); start and end lie on 5-minute boundaries (template rule).</summary>
    public static IEnumerable<ScheduleSlot> For(Appointment a)
    {
        for (var t = a.Start; t < a.End; t = t.AddMinutes(UnitMinutes))
            yield return new ScheduleSlot { Id = Guid.CreateVersion7(), TenantId = a.TenantId, FacilityId = a.FacilityId, CalendarId = a.CalendarId, SlotStart = t, AppointmentId = a.Id };
    }
}

/// <summary>One state change of an appointment, append-only: who, when and to what (the visit's history).</summary>
public sealed class VisitStatusEvent
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid AppointmentId { get; init; }
    [Classification(DataClass.Internal)] public AppointmentState State { get; init; }
    [Classification(DataClass.Internal)] public Guid? ActorUserId { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset OccurredAt { get; init; }
    /// <summary>Why, for a cancellation (MDV-P1-024).</summary>
    [Classification(DataClass.Internal)] public CancelReason? CancelReason { get; init; }
    /// <summary>The desk's note on a cancellation; it may mention health, so it is never on reception screens, in logs or in events.</summary>
    [Classification(DataClass.SensitiveHealth)] public string? Note { get; init; }
    /// <summary>
    /// Check-in only: milliseconds from the desk tapping "Check in" as the patient arrives to the visit being checked in, timed by
    /// the desk (pilot metric, exit gate 3). A number, never patient data; null when not timed.
    /// </summary>
    [Classification(DataClass.Internal)] public int? CheckInMs { get; init; }
}

/// <summary>A reschedule (MDV-P1-024), append-only: the old slot, the new slot, who moved it and when.</summary>
public sealed class AppointmentReschedule
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid AppointmentId { get; init; }
    [Classification(DataClass.Internal)] public Guid FromCalendarId { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset FromStart { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset FromEnd { get; init; }
    [Classification(DataClass.Internal)] public Guid ToCalendarId { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ToStart { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ToEnd { get; init; }
    [Classification(DataClass.Internal)] public Guid? ActorUserId { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset OccurredAt { get; init; }
    /// <summary>Set when a waiting patient moved to another doctor the same day (MDV-P1-030); null for a reschedule.</summary>
    [Classification(DataClass.Internal)] public ReassignReason? ReassignReason { get; init; }
}

/// <summary>
/// A domain event written in the same transaction as the change it describes (transactional outbox). Payloads carry opaque
/// ids and times only, never PHI.
/// A Hangfire job publishes them to the module subscribers (OutboxDispatcher, MDV-P1-032); no Service Bus in P1.
/// </summary>
public sealed class OutboxEvent
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    /// <summary>Versioned event name, e.g. "AppointmentBooked.v1".</summary>
    [Classification(DataClass.Internal)] public required string Type { get; init; }
    [Classification(DataClass.Internal)] public Guid AggregateId { get; init; }
    /// <summary>JSON of opaque ids and instants.</summary>
    [Classification(DataClass.Internal)] public required string Payload { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset OccurredAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>
/// A tenant that has written an outbox event, kept by a trigger on OutboxEvents. Not row-level secured on purpose: it holds
/// tenant ids only, so a background job can find whose scope to enter without reading any tenant's rows.
/// </summary>
public sealed class OutboxTenant
{
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
}

/// <summary>
/// The database refused a double booking (the SCHEDULE_SLOT unique index fired after the doctor-day lock and checks): the pilot's
/// "double-booking constraint violations" metric (MDV-P1-041). Opaque ids and a time only.
/// </summary>
public sealed class BookingConflict
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid CalendarId { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset OccurredAt { get; init; }
}

/// <summary>The pilot metrics a clinic's pre-Medvya baseline is recorded for (MDV-P1-041, PRD v2 §06).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PilotMetric>))]
public enum PilotMetric { CheckInSeconds, NoShowPercent, DoubleBookings, ReminderDeliveryPercent }

/// <summary>
/// A clinic's pre-Medvya value of one pilot metric, as a person measured it (e.g. from the paper register over the first two
/// weeks), with how and over which dates. Append-only: the newest per metric is the baseline. Numbers and method only.
/// Value is a whole number (seconds, percent or count): a hand-measured baseline is no more precise than that.
/// </summary>
public sealed class MetricBaseline
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public PilotMetric Metric { get; init; }
    [Classification(DataClass.Internal)] public int Value { get; init; }
    /// <summary>How it was measured ("Paper register, every visit"). Staff free text; the form says never to enter patient details.</summary>
    [Classification(DataClass.Internal)] public required string Method { get; init; }
    [Classification(DataClass.Internal)] public DateOnly FromDate { get; init; }
    [Classification(DataClass.Internal)] public DateOnly ToDate { get; init; }
    [Classification(DataClass.Internal)] public Guid? RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RecordedAt { get; init; }
}

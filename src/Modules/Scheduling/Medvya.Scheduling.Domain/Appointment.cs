using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Scheduling.Domain;

/// <summary>Appointment / visit states (docs/03-architecture/state-machines.md). Reception sees them as nonclinical statuses.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppointmentState>))]
public enum AppointmentState { Booked, CheckedIn, WithNurse, ReadyForDoctor, InConsult, Billing, Done, Cancelled, NoShow }

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
    [Classification(DataClass.Internal)] public VisitType VisitType { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset Start { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset End { get; set; }
    [Classification(DataClass.Internal)] public AppointmentState State { get; set; }
    [Classification(DataClass.Internal)] public ReasonCategory ReasonCategory { get; set; }
    /// <summary>The patient's own words. Clinicians only, never on reception screens, never in logs or events.</summary>
    [Classification(DataClass.SensitiveHealth)] public string? ReasonText { get; set; }
    [Classification(DataClass.Internal)] public BookingChannel Channel { get; init; }
    [Classification(DataClass.Internal)] public Guid? BookedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset BookedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid? ChangedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }

    /// <summary>States that occupy their slots; a cancelled or no-show visit frees them.</summary>
    public static bool Occupies(AppointmentState state) => state is not (AppointmentState.Cancelled or AppointmentState.NoShow);
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

    /// <summary>Only a visit that has not started moves to another slot (Booked → Booked, history kept).</summary>
    public static bool CanReschedule(AppointmentState state) => state == AppointmentState.Booked;

    /// <summary>
    /// Cancel before the clinical part of the visit: Booked, or Checked in when the patient leaves before being seen. From With
    /// nurse on the visit is finished on the board, and a completed, cancelled or no-show visit cannot be cancelled.
    /// </summary>
    public static bool CanCancel(AppointmentState state) => state is AppointmentState.Booked or AppointmentState.CheckedIn;

    /// <summary>Only a booked visit that never checked in is a no-show (and only once it has started: see the use case).</summary>
    public static bool CanMarkNoShow(AppointmentState state) => state == AppointmentState.Booked;
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
}

/// <summary>
/// A domain event written in the same transaction as the change it describes (transactional outbox). Payloads carry opaque
/// ids and times only, never PHI.
/// shortcut: nothing dispatches these yet; the Service Bus / Hangfire dispatcher lands with notifications (MDV-P1-032).
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

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

/// <summary>A booked visit in one calendar (doctor or resource) at one location. Start and End are UTC instants.</summary>
public sealed class Appointment
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid CalendarId { get; init; }
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

/// <summary>One state change of an appointment, append-only: who, when and to what (the visit's history).</summary>
public sealed class VisitStatusEvent
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid AppointmentId { get; init; }
    [Classification(DataClass.Internal)] public AppointmentState State { get; init; }
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

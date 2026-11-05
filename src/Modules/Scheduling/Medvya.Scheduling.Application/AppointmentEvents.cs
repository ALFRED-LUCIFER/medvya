namespace Medvya.Scheduling.Application;

public enum AppointmentEventKind { Booked, Rescheduled, Cancelled, NoShow }

/// <summary>
/// An appointment change as other modules may see it (MDV-P1-032): opaque ids and instants only, never names or notes.
/// <paramref name="Start"/> is the start after the change (for a cancellation: the start it had). <paramref name="Quiet"/>: the change
/// is one of several made by one action (a series cancelled "this and following"), so only one of them tells the patient.
/// </summary>
public sealed record AppointmentEvent(
    Guid EventId, AppointmentEventKind Kind, Guid TenantId, Guid AppointmentId, Guid FacilityId, Guid PatientId, DateTimeOffset Start, bool WalkIn, DateTimeOffset OccurredAt, bool Quiet = false);

/// <summary>
/// Subscribes to appointment events. Delivery is at least once and in order per tenant, so a subscriber must be idempotent on
/// <see cref="AppointmentEvent.EventId"/>. A subscriber that throws holds the event back (it is retried by the next dispatch).
/// </summary>
public interface IAppointmentSubscriber
{
    Task HandleAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken = default);
}

/// <summary>Publishes the outbox (transactional outbox, MDV-P1-024): runs in a tenant scope; the host's job calls it per tenant.</summary>
public interface IOutboxDispatcher
{
    /// <summary>Tenants that have ever written an outbox event: the only cross-tenant fact, so a job knows whose scope to enter.</summary>
    Task<IReadOnlyList<Guid>> TenantsAsync(CancellationToken cancellationToken = default);

    /// <summary>Hands up to <paramref name="batch"/> unpublished events of the current tenant to the subscribers, oldest first; returns how many were published.</summary>
    Task<int> DispatchAsync(int batch = 100, CancellationToken cancellationToken = default);
}

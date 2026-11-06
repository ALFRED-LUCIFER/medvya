using Medvya.Notifications.Domain;

namespace Medvya.Notifications.Application;

/// <summary>An SMS as the operator expects it (India DLT): the registered sender header, the registered template id and the final text.</summary>
public sealed record SmsMessage(string To, string Sender, string? TemplateId, string Text, string Reference);

/// <summary>Hands one SMS to a provider. Throws when it was not accepted. The local sink and, later, the chosen aggregator implement it (MDV-P1-033).</summary>
public interface ISmsGateway
{
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}

/// <summary>One message as reception sees it: no text, no address.</summary>
public sealed record NotificationView(
    Guid Id, Guid AppointmentId, NotificationKind Kind, NotificationChannel Channel, NotificationStatus Status, int Attempts, string? FailureCode, DateTimeOffset SendAt, DateTimeOffset UpdatedAt);

/// <summary>Delivery of reminders and confirmations of one channel in a period (exit gate 4).</summary>
public sealed record ChannelDelivery(NotificationChannel Channel, int Total, int Delivered, int Failed, int Pending);

public sealed record DeliveryReport(IReadOnlyList<ChannelDelivery> Channels, IReadOnlyList<NotificationView> Failures);

/// <summary>What reception may read of the messages sent to patients (MDV-P1-032/033). The caller must hold an active membership with view access at the facility.</summary>
public interface INotificationQueries
{
    /// <summary>Every message of the appointment, oldest first.</summary>
    Task<IReadOnlyList<NotificationView>> ForAppointmentAsync(Guid facilityId, Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>Appointments starting within two days of now that have a failed or bounced message (ids only), for the board.</summary>
    Task<IReadOnlyList<Guid>> FailedVisitsAsync(Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>Per-channel counts since <paramref name="since"/> and every failed or bounced message (newest first, at most <paramref name="maxFailures"/>), with its reason code.</summary>
    Task<DeliveryReport> DeliveryAsync(Guid facilityId, DateTimeOffset since, int maxFailures = 100, CancellationToken cancellationToken = default);
}

/// <summary>A delivery receipt from a provider, after the host verified its signature.</summary>
public sealed record DeliveryReceipt(string Reference, NotificationStatus State, string? ReasonCode);

public interface IReceiptProcessor
{
    /// <summary>Applies the receipt to the notification named by its reference (in the current tenant). Unknown references and receipts that would undo a final status are ignored.</summary>
    Task<bool> ApplyAsync(DeliveryReceipt receipt, CancellationToken cancellationToken = default);
}

/// <summary>The background work of one tenant: send what is due. The host's job calls it inside each tenant's scope after publishing the outbox.</summary>
public interface INotificationSender
{
    /// <summary>Sends up to <paramref name="batch"/> due messages; returns how many it handled.</summary>
    Task<int> SendDueAsync(int batch = 50, CancellationToken cancellationToken = default);
}

public static class NotificationReference
{
    public static string Make(Guid tenantId, Guid notificationId) => $"{tenantId:N}.{notificationId:N}";

    public static bool TryParse(string? reference, out Guid tenantId, out Guid notificationId)
    {
        tenantId = notificationId = default;
        var parts = reference?.Split('.');
        return parts is [var t, var n] && Guid.TryParseExact(t, "N", out tenantId) && Guid.TryParseExact(n, "N", out notificationId);
    }
}

/// <summary>One item of the notification centre (D-03): a kind and a pointer, worded by the screen.</summary>
public sealed record StaffNotificationView(Guid Id, StaffNotificationKind Kind, Guid? RefId, DateTimeOffset CreatedAt, bool Read);

public sealed record StaffNotificationList(int Unread, IReadOnlyList<StaffNotificationView> Items);

/// <summary>The signed-in staff member's notifications at a facility. Needs an active membership there and the capability each kind needs.</summary>
public interface IStaffNotifications
{
    /// <summary>The newest items this user may see, with their own read state.</summary>
    Task<StaffNotificationList> ListAsync(Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>False when the item is not one this user may see.</summary>
    Task<bool> MarkReadAsync(Guid facilityId, Guid id, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(Guid facilityId, CancellationToken cancellationToken = default);
}

/// <summary>A practice's notification settings (MDV-P1-006).</summary>
public sealed record NotificationPreferencesView(
    bool Confirmation, bool Reminder24h, bool Reminder2h, bool Changes, bool Email, bool Sms, TimeOnly QuietStart, TimeOnly QuietEnd)
{
    public bool Allows(NotificationKind kind) => kind switch
    {
        NotificationKind.Confirmation => Confirmation,
        NotificationKind.Reminder24h => Reminder24h,
        NotificationKind.Reminder2h => Reminder2h,
        _ => Changes,
    };

    public bool Allows(NotificationChannel channel) => channel == NotificationChannel.Email ? Email : Sms;
}

/// <summary>A message template shown with placeholders only, never a patient's values.</summary>
public sealed record TemplatePreview(string Group, string Subject, string Text);

/// <summary>
/// Reads and changes the practice's preferences. The host allows the endpoints to practice configuration only; background jobs read
/// the same settings for the tenant they run in.
/// </summary>
public interface INotificationPreferences
{
    /// <summary>The saved preferences, or the defaults when none were saved.</summary>
    Task<NotificationPreferencesView> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves, audits, and cancels what is still queued for anything just turned off.</summary>
    Task<NotificationPreferencesView> SaveAsync(NotificationPreferencesView changes, CancellationToken cancellationToken = default);
}

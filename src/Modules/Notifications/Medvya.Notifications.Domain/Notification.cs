using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Notifications.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<NotificationChannel>))]
public enum NotificationChannel { Email, Sms }

/// <summary>What the message is about. Reminders go out at a fixed time before the visit; the others as soon as the change is published.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationKind>))]
public enum NotificationKind { Confirmation, Rescheduled, Cancelled, Reminder24h, Reminder2h, NeedsRebooking }

/// <summary>
/// Queued: waiting for its time or for a retry. Sent: handed to the provider. Delivered / Bounced: the provider's receipt.
/// Failed: every attempt failed (or it can never be sent). Cancelled: the visit changed first. Suppressed: deliberately not sent
/// (no consent, no address, past the visit); the reason is in FailureCode.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationStatus>))]
public enum NotificationStatus { Queued, Sent, Delivered, Failed, Bounced, Cancelled, Suppressed }

/// <summary>
/// NOTIFICATION (MDV-P1-032): one message to one patient on one channel about one appointment. It holds no message text and no
/// address: both are made at send time from the template and the patient record, so nothing personal is stored here.
/// </summary>
public sealed class Notification
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid AppointmentId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    /// <summary>The outbox event this came from; with Kind and Channel it makes publishing the same event twice harmless.</summary>
    [Classification(DataClass.Internal)] public Guid SourceEventId { get; init; }
    [Classification(DataClass.Internal)] public NotificationChannel Channel { get; init; }
    [Classification(DataClass.Internal)] public NotificationKind Kind { get; init; }
    /// <summary>The visit's start when this was made; the message tells the patient this time.</summary>
    [Classification(DataClass.Internal)] public DateTimeOffset AppointmentStart { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset SendAt { get; init; }
    [Classification(DataClass.Internal)] public NotificationStatus Status { get; set; }
    [Classification(DataClass.Internal)] public int Attempts { get; set; }
    /// <summary>Earliest time of the next try; also the claim that keeps two runs from sending the same message.</summary>
    [Classification(DataClass.Internal)] public DateTimeOffset? NextAttemptAt { get; set; }
    /// <summary>Our reference for the provider and its receipts: "tenant.notification" in hex. Not personal.</summary>
    [Classification(DataClass.Internal)] public string? ProviderRef { get; set; }
    /// <summary>A short code (no message text, no address): why it failed or was suppressed.</summary>
    [Classification(DataClass.Internal)] public string? FailureCode { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>A final status never changes again, except that a delivered or bounced receipt may follow "Sent".</summary>
    public static bool IsFinal(NotificationStatus s) => s is not (NotificationStatus.Queued or NotificationStatus.Sent);
}

/// <summary>NOTIFICATION_ATTEMPT: one try or one provider receipt, append-only.</summary>
public sealed class NotificationAttempt
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid NotificationId { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset At { get; init; }
    /// <summary>The status this try or receipt led to.</summary>
    [Classification(DataClass.Internal)] public NotificationStatus Outcome { get; init; }
    [Classification(DataClass.Internal)] public string? Code { get; init; }
}

/// <summary>What a staff notification is about. The screen words each kind itself, so no text is stored.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StaffNotificationKind>))]
public enum StaffNotificationKind { MessageFailed, LicenceExpiring, AccessReviewDue, AccessRequested, RegistrationExpiring }

/// <summary>
/// STAFF_NOTIFICATION (MDV-P1-034): one in-app item for the staff of a facility (the notification centre, D-03). It holds ids and a
/// kind only, never a patient's name or the message text. Who may see it follows from the kind's capability, checked on every read.
/// </summary>
public sealed class StaffNotification
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public StaffNotificationKind Kind { get; init; }
    /// <summary>The appointment (or other record) it points at.</summary>
    [Classification(DataClass.Internal)] public Guid? RefId { get; init; }
    /// <summary>A number the screen needs: days until a licence expires (0 = on its last valid day, below 0 = already expired). Null for other kinds.</summary>
    [Classification(DataClass.Internal)] public int? Detail { get; init; }
    /// <summary>With the tenant it makes raising the same event twice harmless, e.g. "failed:" and the notification id.</summary>
    [Classification(DataClass.Internal)] public string DedupeKey { get; init; } = "";
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>One staff member has read one staff notification; this is what makes the read state follow them across devices.</summary>
public sealed class StaffNotificationRead
{
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid NotificationId { get; init; }
    [Classification(DataClass.Internal)] public Guid UserId { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ReadAt { get; init; }
}

/// <summary>
/// NOTIFICATION_PREFERENCE (MDV-P1-006): which messages a practice sends, on which channels, and its quiet hours. One row per tenant;
/// no row means everything on with the host's default quiet hours. Holds no patient data.
/// </summary>
public sealed class NotificationPreference
{
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public bool Confirmation { get; set; }
    [Classification(DataClass.Internal)] public bool Reminder24h { get; set; }
    [Classification(DataClass.Internal)] public bool Reminder2h { get; set; }
    /// <summary>Messages about a changed or cancelled visit (rescheduled, cancelled, needs rebooking).</summary>
    [Classification(DataClass.Internal)] public bool Changes { get; set; }
    [Classification(DataClass.Internal)] public bool Email { get; set; }
    [Classification(DataClass.Internal)] public bool Sms { get; set; }
    /// <summary>Facility-local window with no SMS; equal values mean no quiet hours.</summary>
    [Classification(DataClass.Internal)] public TimeOnly QuietStart { get; set; }
    [Classification(DataClass.Internal)] public TimeOnly QuietEnd { get; set; }
    /// <summary>Name at the start of every email subject (and the From name where the provider allows it); null means the location's name (#213).</summary>
    [Classification(DataClass.Public)] public string? SenderName { get; set; }
    [Classification(DataClass.Internal)] public Guid? ChangedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }
}

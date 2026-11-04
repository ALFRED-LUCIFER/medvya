using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Notifications.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<NotificationChannel>))]
public enum NotificationChannel { Email, Sms }

/// <summary>What the message is about. Reminders go out at a fixed time before the visit; the others as soon as the change is published.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationKind>))]
public enum NotificationKind { Confirmation, Rescheduled, Cancelled, Reminder24h, Reminder2h }

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

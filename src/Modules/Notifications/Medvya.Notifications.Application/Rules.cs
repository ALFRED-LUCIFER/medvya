using Medvya.Notifications.Domain;

namespace Medvya.Notifications.Application;

/// <summary>Settings (section Notifications). Secrets (SendGrid key, SMS and receipt secrets) come from Key Vault / user secrets, never from files.</summary>
public sealed class NotificationOptions
{
    /// <summary>Channels messages are made for. A channel left out is never used.</summary>
    public List<NotificationChannel> Channels { get; set; } = [NotificationChannel.Email, NotificationChannel.Sms];

    /// <summary>Tries per message before it is shown as failed.</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>Wait before try 2, 3, ...; the last value repeats.</summary>
    public List<int> BackoffMinutes { get; set; } = [1, 5, 30];

    /// <summary>Reminders are not sent in this facility-local window; one that falls inside moves to its end, if that is before the visit.</summary>
    public TimeOnly QuietStart { get; set; } = new(21, 0);
    public TimeOnly QuietEnd { get; set; } = new(8, 0);

    /// <summary>Shared secret that signs provider receipts (HMAC-SHA256 of the body). Unset: every receipt is refused.</summary>
    public string? ReceiptSecret { get; set; }

    public SmsOptions Sms { get; set; } = new();
}

public sealed class SmsOptions
{
    /// <summary>"Sink" (local, the mail sink) is the only provider until the SMS provider decision (Q-03, MDV-P1-045); anything else fails closed.</summary>
    public string Provider { get; set; } = "";

    /// <summary>shortcut: one sender id for every clinic plus per-tenant overrides in config; the clinic's own registered header moves to practice settings with MDV-P1-045.</summary>
    public string? DefaultSender { get; set; }
    public Dictionary<Guid, string> Senders { get; set; } = [];

    /// <summary>
    /// DLT template ids by "Confirmation.en", "Rescheduled.hi", "Reminder.ar"... Registered per clinic sender with the operator
    /// (MDV-P1-045, a person task); a template without an id is not sent in India.
    /// </summary>
    public Dictionary<string, string> DltTemplateIds { get; set; } = [];
}

/// <summary>
/// The patient's consent to be reminded on a channel (MDV-P1-019). Checked before every send, so withdrawing consent stops queued
/// messages. The default registered by the module allows nothing (fail closed) until the consent module supplies the real one.
/// </summary>
public interface IReminderConsent
{
    Task<bool> AllowsAsync(Guid patientId, NotificationChannel channel, CancellationToken cancellationToken = default);
}

/// <summary>Allows nothing: without a consent record no message is sent.</summary>
public sealed class DenyAllReminderConsent : IReminderConsent
{
    public Task<bool> AllowsAsync(Guid patientId, NotificationChannel channel, CancellationToken cancellationToken = default) => Task.FromResult(false);
}

public static class NotificationRules
{
    /// <summary>The lead time before the visit at which a reminder goes out; null for the other kinds.</summary>
    public static TimeSpan? Lead(NotificationKind kind) => kind switch
    {
        NotificationKind.Reminder24h => TimeSpan.FromHours(24),
        NotificationKind.Reminder2h => TimeSpan.FromHours(2),
        _ => null,
    };

    /// <summary>
    /// When a reminder goes out: <paramref name="lead"/> before the visit, moved to the end of the quiet hours if it falls inside
    /// them. Null when that time has already passed at booking or the visit starts before the quiet hours end (nothing useful to say).
    /// </summary>
    public static DateTimeOffset? ReminderTime(DateTimeOffset start, TimeSpan lead, DateTimeOffset now, TimeZoneInfo zone, TimeOnly quietStart, TimeOnly quietEnd)
    {
        var at = start - lead;
        if (at < now) return null;
        var moved = QuietEnd(at, zone, quietStart, quietEnd);
        return moved is null ? at : moved < start ? moved : null;
    }

    /// <summary>The end of the facility-local quiet window that <paramref name="at"/> falls in, or null when it is outside (or the window is empty).</summary>
    public static DateTimeOffset? QuietEnd(DateTimeOffset at, TimeZoneInfo zone, TimeOnly quietStart, TimeOnly quietEnd)
    {
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var quiet = quietStart <= quietEnd ? time >= quietStart && time < quietEnd : time >= quietStart || time < quietEnd;
        if (!quiet) return null;
        var endDay = quietStart > quietEnd && time >= quietStart ? local.Date.AddDays(1) : local.Date;
        return new DateTimeOffset(endDay + quietEnd.ToTimeSpan(), zone.GetUtcOffset(endDay + quietEnd.ToTimeSpan())).ToUniversalTime();
    }

    /// <summary>When try number <paramref name="attempts"/> + 1 may run after <paramref name="attempts"/> failures.</summary>
    public static DateTimeOffset NextAttempt(DateTimeOffset now, int attempts, IReadOnlyList<int> backoffMinutes) =>
        now.AddMinutes(backoffMinutes.Count == 0 ? 1 : backoffMinutes[Math.Min(attempts, backoffMinutes.Count) - 1]);
}

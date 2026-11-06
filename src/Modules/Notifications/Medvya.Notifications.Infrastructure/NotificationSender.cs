using Medvya.BuildingBlocks.Domain;
using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure.Persistence;
using Medvya.Patients.Application;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Medvya.Notifications.Infrastructure;

/// <summary>
/// Sends what is due (MDV-P1-032/033). Per message, in order: the visit has not started, the patient consents to the channel
/// (checked now, not when it was queued), an address exists, the text is made and, for SMS, matches a registered DLT template.
/// SMS waits out the practice's quiet hours. A failed try is retried with backoff up to MaxAttempts and then shown as Failed; a message that can never be sent is Failed at
/// once. Nothing personal is logged or stored: only ids, status and short codes.
/// </summary>
public sealed partial class NotificationSender(
    NotificationsDbContext db, IReminderConsent consent, IPatientContacts contacts, IFacilityDirectory facilities, INotificationPreferences preferences, IEmailSender email, ISmsGateway sms,
    ITermsAcceptance terms, IOptions<NotificationOptions> options, TimeProvider clock, ILogger<NotificationSender> log) : INotificationSender
{
    /// <summary>How long a claimed message is left alone; a run that died mid-send is retried after this.</summary>
    static readonly TimeSpan ClaimWindow = TimeSpan.FromMinutes(5);

    /// <summary>A code for a message that can never be sent; retrying cannot help.</summary>
    sealed class PermanentFailure(string code) : Exception(code) { public string Code { get; } = code; }

    public async Task<int> SendDueAsync(int batch = 50, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        // Until the owner accepts the terms (MDV-P1-019) only what the patient must hear goes out; the rest waits queued.
        var mustTellOnly = !await terms.IsSatisfiedAsync(cancellationToken);
        var due = await db.Notifications.AsNoTracking()
            .Where(n => n.Status == NotificationStatus.Queued && n.SendAt <= now && (n.NextAttemptAt == null || n.NextAttemptAt <= now))
            .Where(n => !mustTellOnly || n.Kind == NotificationKind.Cancelled || n.Kind == NotificationKind.NeedsRebooking)
            .OrderBy(n => n.SendAt).Select(n => n.Id).Take(batch).ToListAsync(cancellationToken);
        var handled = 0;
        foreach (var id in due)
        {
            // Claim first: a second run (or instance) skips what this one is sending.
            if (await db.Notifications.Where(n => n.Id == id && n.Status == NotificationStatus.Queued && (n.NextAttemptAt == null || n.NextAttemptAt <= now))
                    .ExecuteUpdateAsync(s => s.SetProperty(n => n.NextAttemptAt, now + ClaimWindow), cancellationToken) == 0) continue;
            var n = await db.Notifications.SingleAsync(x => x.Id == id, cancellationToken);
            await ProcessAsync(n, now, cancellationToken);
            handled++;
        }
        return handled;
    }

    async Task ProcessAsync(Notification n, DateTimeOffset now, CancellationToken ct)
    {
        var o = options.Value;
        try
        {
            if (n.Kind is not (NotificationKind.Cancelled or NotificationKind.NeedsRebooking) && now >= n.AppointmentStart) { await EndAsync(n, now, NotificationStatus.Suppressed, "visit_started", ct); return; }
            if (!await consent.AllowsAsync(n.PatientId, n.Channel, ct)) { await EndAsync(n, now, NotificationStatus.Suppressed, "no_consent", ct); return; }
            var contact = await contacts.FindAsync(n.PatientId, ct);
            var address = contact is null ? null : n.Channel == NotificationChannel.Email ? contact.Email : contact.Mobile;
            if (contact is null || string.IsNullOrWhiteSpace(address)) { await EndAsync(n, now, NotificationStatus.Suppressed, "no_address", ct); return; }
            var facility = await facilities.FindAsync(n.FacilityId, ct) ?? throw new PermanentFailure("facility_missing");

            var zone = TimeZoneInfo.FindSystemTimeZoneById(facility.TimeZone);
            if (n.Channel == NotificationChannel.Sms)
            {
                // Quiet hours: no SMS inside the window; wait for its end (the visit-started check above then drops it if the visit came first).
                var prefs = await preferences.GetAsync(ct);
                if (NotificationRules.QuietEnd(now, zone, prefs.QuietStart, prefs.QuietEnd) is { } end)
                {
                    n.NextAttemptAt = end;
                    await db.SaveChangesAsync(ct);
                    return;
                }
            }
            var message = MessageCatalog.Render(n.Kind, contact.PreferredLanguage, facility.Name, TimeZoneInfo.ConvertTime(n.AppointmentStart, zone));
            var reference = NotificationReference.Make(n.TenantId, n.Id);
            if (n.Channel == NotificationChannel.Email)
                await email.SendAsync(new EmailMessage(address, message.Subject, message.Body, new Dictionary<string, string> { ["X-SMTPAPI"] = $$$"""{"unique_args":{"ref":"{{{reference}}}"}}""" }), ct);
            else
                await sms.SendAsync(SmsFor(n, contact.CountryCode, address, message, reference, o.Sms), ct);

            n.ProviderRef = reference;
            await EndAsync(n, now, NotificationStatus.Sent, null, ct);
        }
        catch (PermanentFailure f)
        {
            await EndAsync(n, now, NotificationStatus.Failed, f.Code, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The exception text can hold the address; keep only its type as the code.
            n.Attempts++;
            var code = "provider_" + e.GetType().Name.ToLowerInvariant();
            if (n.Attempts >= o.MaxAttempts) await EndAsync(n, now, NotificationStatus.Failed, code, ct);
            else
            {
                n.NextAttemptAt = NotificationRules.NextAttempt(now, n.Attempts, o.BackoffMinutes);
                await EndAsync(n, now, NotificationStatus.Queued, code, ct);
            }
        }
    }

    /// <summary>The SMS only if it is a registered template with its sender and, in India, its DLT template id; otherwise it is never handed to a provider.</summary>
    static SmsMessage SmsFor(Notification n, string countryCode, string to, RenderedMessage message, string reference, SmsOptions o)
    {
        var sender = o.Senders.GetValueOrDefault(n.TenantId) ?? o.DefaultSender;
        if (string.IsNullOrWhiteSpace(sender)) throw new PermanentFailure("sms_sender_missing");
        if (MessageCatalog.RegisteredTemplate(message.Body) is not { } key) throw new PermanentFailure("sms_template_unregistered");
        var templateId = o.DltTemplateIds.GetValueOrDefault(key);
        if (countryCode == "IN" && string.IsNullOrWhiteSpace(templateId)) throw new PermanentFailure("dlt_template_id_missing");
        return new SmsMessage(to, sender, templateId, message.Body, reference);
    }

    /// <summary>Records the try (status, code) and saves it with the message in one go.</summary>
    async Task EndAsync(Notification n, DateTimeOffset now, NotificationStatus status, string? code, CancellationToken ct)
    {
        n.Status = status;
        n.FailureCode = status == NotificationStatus.Sent ? null : code;
        n.UpdatedAt = now;
        if (status != NotificationStatus.Queued) n.NextAttemptAt = null;
        if (status is NotificationStatus.Suppressed or NotificationStatus.Failed) n.Attempts = Math.Max(n.Attempts, 1);
        else if (status == NotificationStatus.Sent) n.Attempts++;
        db.Attempts.Add(new NotificationAttempt { Id = Guid.CreateVersion7(), TenantId = n.TenantId, NotificationId = n.Id, At = now, Outcome = status, Code = code });
        if (status == NotificationStatus.Failed) StaffNotifications.Raise(db, n, now, $"failed:{n.Id:N}"); // reception sees it in the notification centre
        await db.SaveChangesAsync(ct);
        Ended(log, n.Id, status, code);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification {NotificationId} is now {Status} ({Code}).")]
    static partial void Ended(ILogger logger, Guid notificationId, NotificationStatus status, string? code);
}

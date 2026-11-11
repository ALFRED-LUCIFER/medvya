using Medvya.BuildingBlocks.Domain;
using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Patients.Application;
using Medvya.Tenancy.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Medvya.Notifications.Infrastructure;

/// <summary>
/// Sends secure-link messages (MDV-P2-020) at once, through the same email sender and SMS gateway as appointment messages, with the same SMS rules:
/// the practice's sender header and, in India, a registered DLT template id, or nothing is handed to the provider. Only ids and codes are logged.
/// </summary>
public sealed partial class SecureLinkMessages(
    IPatientContacts contacts, IFacilityDirectory facilities, INotificationPreferences preferences, IEmailSender email, ISmsGateway sms, ICurrentTenant tenant,
    IOptions<NotificationOptions> options, ILogger<SecureLinkMessages> log) : ISecureLinkMessages
{
    public async Task SendLinkToPatientAsync(Guid patientId, Guid facilityId, NotificationChannel channel, string url, DateTimeOffset expiresAt, Guid reference, CancellationToken cancellationToken = default)
    {
        var contact = await contacts.FindAsync(patientId, cancellationToken);
        var address = channel == NotificationChannel.Email ? contact?.Email : contact?.Mobile;
        if (contact is null || string.IsNullOrWhiteSpace(address)) throw new SecureLinkNotSentException("no_address");
        var facility = await FacilityAsync(facilityId, cancellationToken);
        var message = SecureLinkCatalog.Link(contact.PreferredLanguage, facility.Name, url, Local(expiresAt, facility.TimeZone));
        await SendAsync(channel, contact.CountryCode, address, message, facility.Name, reference, cancellationToken);
    }

    public async Task SendLinkToPharmacyAsync(string email, Guid facilityId, string url, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        var facility = await FacilityAsync(facilityId, cancellationToken);
        await SendAsync(NotificationChannel.Email, "", email, SecureLinkCatalog.PharmacyLink(facility.Name, url, Local(expiresAt, facility.TimeZone)), facility.Name, Guid.Empty, cancellationToken);
    }

    public async Task SendCodeAsync(Guid patientId, Guid facilityId, string code, Guid reference, CancellationToken cancellationToken = default)
    {
        var contact = await contacts.FindAsync(patientId, cancellationToken);
        if (contact is null || string.IsNullOrWhiteSpace(contact.Mobile)) throw new SecureLinkNotSentException("no_address");
        var facility = await FacilityAsync(facilityId, cancellationToken);
        await SendAsync(NotificationChannel.Sms, contact.CountryCode, contact.Mobile, SecureLinkCatalog.Code(contact.PreferredLanguage, code, facility.Name), facility.Name, reference, cancellationToken);
    }

    async Task<FacilityInfo> FacilityAsync(Guid facilityId, CancellationToken ct) =>
        await facilities.FindAsync(facilityId, ct) ?? throw new InvalidOperationException("Facility not in this tenant.");

    static DateTimeOffset Local(DateTimeOffset at, string timeZone) => TimeZoneInfo.ConvertTime(at, TimeZoneInfo.FindSystemTimeZoneById(timeZone));

    async Task SendAsync(NotificationChannel channel, string countryCode, string to, RenderedMessage message, string clinic, Guid reference, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        try
        {
            if (channel == NotificationChannel.Email)
            {
                var sender = (await preferences.GetAsync(ct)).SenderFor(clinic);
                await email.SendAsync(new EmailMessage(to, $"{sender}: {message.Subject}", message.Body, null, sender), ct);
            }
            else
            {
                var o = options.Value.Sms;
                var sender = o.Senders.GetValueOrDefault(tenantId) ?? o.DefaultSender;
                var templateId = o.DltTemplateIds.GetValueOrDefault(message.TemplateKey);
                if (string.IsNullOrWhiteSpace(sender) || (countryCode == "IN" && string.IsNullOrWhiteSpace(templateId))) throw new SecureLinkNotSentException("send_failed");
                await sms.SendAsync(new SmsMessage(to, sender, templateId, message.Body, NotificationReference.Make(tenantId, reference)), ct);
            }
        }
        catch (Exception e) when (e is not (OperationCanceledException or SecureLinkNotSentException))
        {
            // The exception text can hold the address; keep only its type.
            NotSent(log, message.TemplateKey, e.GetType().Name);
            throw new SecureLinkNotSentException("send_failed");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Secure link message {TemplateKey} was not sent ({ErrorType}).")]
    static partial void NotSent(ILogger logger, string templateKey, string errorType);
}

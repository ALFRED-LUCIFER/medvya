using System.Globalization;
using Medvya.BuildingBlocks.Domain;
using Medvya.Notifications.Application;

namespace Medvya.Notifications.Infrastructure;

/// <summary>
/// The local SMS sink (MDV-P0-005): the text goes to the mail sink (smtp4dev) as an email to "{digits}@sms.sink.local", with the
/// sender, DLT template id and reference in the subject and headers, so a developer sees exactly what the operator would get.
/// Never reaches a phone.
/// </summary>
public sealed class SinkSmsGateway(IEmailSender email) : ISmsGateway
{
    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var digits = new string(message.To.Where(char.IsAsciiDigit).ToArray());
        return email.SendAsync(new EmailMessage($"{digits}@sms.sink.local", string.Create(CultureInfo.InvariantCulture, $"SMS from {message.Sender} (DLT template {message.TemplateId ?? "none"})"), message.Text,
            new Dictionary<string, string> { ["X-Medvya-Sms-Reference"] = message.Reference }), cancellationToken);
    }
}

/// <summary>
/// No SMS provider is chosen yet (Q-03, MDV-P1-045), so a real send fails closed and shows as failed to reception instead of
/// silently doing nothing.
/// </summary>
public sealed class UnconfiguredSmsGateway : ISmsGateway
{
    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No SMS provider is configured (Notifications:Sms:Provider).");
}

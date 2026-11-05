using Azure;
using Azure.Communication.Email;
using Azure.Core;
using Medvya.BuildingBlocks.Domain;
using Microsoft.Extensions.Options;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>Azure Communication Services Email settings (section Email:Acs). Endpoint is the resource's https address; From a sender of its linked domain.</summary>
public sealed class AcsEmailOptions
{
    public string? Endpoint { get; set; }
    public string? From { get; set; }
}

/// <summary>
/// Sends through Azure Communication Services Email with the app's own identity: no password or key exists to leak.
/// Hands the message to the service (WaitUntil.Started) and throws when it is refused; fails closed when unconfigured.
/// </summary>
public sealed class AcsEmailSender(IOptions<AcsEmailOptions> options, TokenCredential credential) : IEmailSender
{
    public async Task SendAsync(Domain.EmailMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Endpoint) || string.IsNullOrWhiteSpace(o.From))
            throw new InvalidOperationException("Email is not configured (Email:Acs:Endpoint and Email:Acs:From).");
        var mail = new Azure.Communication.Email.EmailMessage(o.From, message.To, new EmailContent(message.Subject) { PlainText = message.Body });
        foreach (var (name, value) in message.Headers ?? new Dictionary<string, string>()) mail.Headers.Add(name, value);
        await new EmailClient(new Uri(o.Endpoint), credential).SendAsync(WaitUntil.Started, mail, cancellationToken);
    }
}

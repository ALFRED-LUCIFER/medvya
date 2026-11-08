namespace Medvya.BuildingBlocks.Domain;

/// <summary>A plain-text email to one recipient. Never put patient data in it: email is not a PHI channel (AGENTS.md 1.2).</summary>
/// <param name="FromName">Display name for the sender where the provider allows one per message (SMTP/SendGrid); Azure Communication Services
/// keeps the name of its sender username, so the clinic is also named in the subject (#213).</param>
public sealed record EmailMessage(string To, string Subject, string Body, IReadOnlyDictionary<string, string>? Headers = null, string? FromName = null);

/// <summary>Sends transactional email. Throws when the message cannot be handed to the mail server.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

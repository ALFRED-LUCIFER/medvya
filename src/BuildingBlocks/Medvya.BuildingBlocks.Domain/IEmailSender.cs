namespace Medvya.BuildingBlocks.Domain;

/// <summary>A plain-text email to one recipient. Never put patient data in it: email is not a PHI channel (AGENTS.md 1.2).</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>Sends transactional email. Throws when the message cannot be handed to the mail server.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

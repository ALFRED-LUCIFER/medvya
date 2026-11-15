namespace Medvya.BuildingBlocks.Domain;

/// <summary>An email to one recipient: the plain-text body, and the branded HTML copy beside it (<see cref="EmailHtml"/>). Never put patient data in it: email is not a PHI channel (AGENTS.md 1.2).</summary>
/// <param name="FromName">Display name for the sender where the provider allows one per message (SMTP/SendGrid); Azure Communication Services
/// keeps the name of its sender username, so the clinic is also named in the subject (#213).</param>
/// <param name="Html">The HTML alternative; null sends plain text only.</param>
/// <param name="Logo">The clinic logo (PNG) the HTML shows inline as <c>cid:logo</c> (<see cref="EmailHtml.LogoId"/>); sent only with <paramref name="Html"/>.</param>
public sealed record EmailMessage(string To, string Subject, string Body, IReadOnlyDictionary<string, string>? Headers = null, string? FromName = null,
    string? Html = null, byte[]? Logo = null);

/// <summary>Sends transactional email. Throws when the message cannot be handed to the mail server.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

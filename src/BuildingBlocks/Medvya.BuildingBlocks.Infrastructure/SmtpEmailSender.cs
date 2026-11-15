using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Medvya.BuildingBlocks.Domain;
using Microsoft.Extensions.Options;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>
/// SMTP settings (section Email:Smtp). One code path everywhere: smtp4dev locally (localhost:2525, no TLS),
/// SendGrid's SMTP relay in the cloud (smtp.sendgrid.net:587, STARTTLS, user "apikey", password = API key from Key Vault).
/// </summary>
public sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
}

/// <summary>
/// Sends through System.Net.Mail: enough for transactional mail over STARTTLS, so no mail library is needed. With an HTML copy the message is
/// multipart/alternative (plain text first) and the logo rides inside the HTML part as an inline image (cid:logo).
/// Fails closed: without a configured host and sender nothing is sent and the caller's request fails.
/// </summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Host) || string.IsNullOrWhiteSpace(o.From))
            throw new InvalidOperationException("Email is not configured (Email:Smtp:Host and Email:Smtp:From).");
        using var smtp = new SmtpClient(o.Host, o.Port) { EnableSsl = o.EnableSsl, DeliveryMethod = SmtpDeliveryMethod.Network };
        if (!string.IsNullOrEmpty(o.UserName)) smtp.Credentials = new NetworkCredential(o.UserName, o.Password);
        using var mail = new MailMessage(new MailAddress(o.From, message.FromName), new MailAddress(message.To)) { Subject = message.Subject, Body = message.Body };
        if (message.Html is { } html)
        {
            var view = AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html);
            if (message.Logo is { } logo) view.LinkedResources.Add(new LinkedResource(new MemoryStream(logo), MediaTypeNames.Image.Png) { ContentId = EmailHtml.LogoId });
            mail.AlternateViews.Add(view); // disposed with the message
        }
        foreach (var (name, value) in message.Headers ?? new Dictionary<string, string>()) mail.Headers.Add(name, value);
        await smtp.SendMailAsync(mail, cancellationToken);
    }
}

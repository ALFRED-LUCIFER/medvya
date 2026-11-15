using System.Net;
using System.Net.Sockets;
using System.Text;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.Extensions.Options;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P1-111: every email is the shared HTML layout beside the unchanged plain text; values are escaped, Arabic reads right to left. Synthetic data only.</summary>
public sealed class EmailHtmlTests
{
    const string Hostile = "Iyer <script>alert(\"x\")</script> & 'Sons' Clinic";

    static EmailView View(string lang = "en", EmailButton? button = null, string sender = "Iyer Family Clinic") =>
        new(lang, sender, "Adyar", "Appointment reminder", ["Reminder: your appointment is on 17 Nov 2026."], [new("Date", "Monday 17 November 2026", "India Standard Time")],
            button, [], ["Sent by Iyer Family Clinic through Medvya. This email has no medical details."]);

    [Fact]
    public void EveryValueIsEscaped()
    {
        var html = EmailHtml.Render(View(sender: Hostile, button: new("Open", "https://clinic.test/x?a=1&b=\"2\"")), logo: true);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, html, StringComparison.Ordinal);
        Assert.Contains("Iyer &lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; &#39;Sons&#39; Clinic", html, StringComparison.Ordinal);
        Assert.Contains("""alt="Iyer &lt;script&gt;""", html, StringComparison.Ordinal); // the logo's alt text too
        Assert.Contains("""href="https://clinic.test/x?a=1&amp;b=&quot;2&quot;" """.TrimEnd(), html, StringComparison.Ordinal);
    }

    [Fact]
    public void ArabicReadsRightToLeftAndEnglishLeftToRight()
    {
        var ar = EmailHtml.Render(View("ar"), logo: false);
        Assert.Contains("""<html lang="ar" dir="rtl">""", ar, StringComparison.Ordinal);
        Assert.Contains("text-align:right", ar, StringComparison.Ordinal);
        Assert.DoesNotContain("text-transform:uppercase", ar, StringComparison.Ordinal); // upper case and letter spacing would break Arabic joining
        var en = EmailHtml.Render(View(), logo: false);
        Assert.Contains("""<html lang="en" dir="ltr">""", en, StringComparison.Ordinal);
        Assert.Contains("text-transform:uppercase", en, StringComparison.Ordinal);
    }

    [Fact]
    public void TheButtonAndItsLinkAppearOnlyWhenThereIsSomethingToDo()
    {
        var none = EmailHtml.Render(View(), logo: false);
        Assert.DoesNotContain("<a ", none, StringComparison.Ordinal);
        Assert.DoesNotContain("If the button does not work", none, StringComparison.Ordinal);
        var one = EmailHtml.Render(View(button: new("Accept the invitation", "https://clinic.test/invite/abc")), logo: false);
        Assert.Equal(2, one.Split("href=\"https://clinic.test/invite/abc\"").Length - 1); // the button and the spelt-out link
        Assert.Contains("If the button does not work, open this link:", one, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLogoIsInlineOnlyWhenTheMessageCarriesIt()
    {
        Assert.Contains("src=\"cid:logo\"", EmailHtml.Render(View(), logo: true), StringComparison.Ordinal);
        Assert.DoesNotContain("<img", EmailHtml.Render(View(), logo: false), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAppointmentEmailKeepsItsTextAndAddsOnlyTheClinicsDetails()
    {
        var start = new DateTimeOffset(2026, 11, 17, 10, 30, 0, TimeSpan.FromHours(4));
        var text = MessageCatalog.Render(NotificationKind.Reminder24h, "ar", "Al Noor Family Clinic", start);
        var view = MessageCatalog.Email(NotificationKind.Reminder24h, "ar", text, "Al Noor Family Clinic", "Al Noor Family Clinic", start, "Asia/Dubai", "Khalidiya Street, Abu Dhabi", "02 665 0000");
        var html = EmailHtml.Render(view, logo: false);
        Assert.Equal([text.Body], view.Lead); // the opening is the registered text, word for word
        Assert.Equal(text.Subject, view.Title);
        Assert.Contains("بتوقيت الإمارات", html, StringComparison.Ordinal); // the zone named
        Assert.Contains("Khalidiya Street, Abu Dhabi", html, StringComparison.Ordinal);
        Assert.Contains("""<span dir="ltr">02 665 0000</span>""", html, StringComparison.Ordinal); // a phone number keeps its digit order in Arabic
        Assert.Contains("لا يحتوي هذا البريد على أي تفاصيل طبية", html, StringComparison.Ordinal);
        Assert.Null(view.Button);

        var bare = MessageCatalog.Email(NotificationKind.Cancelled, "en", MessageCatalog.Render(NotificationKind.Cancelled, "en", "Iyer", start), "Iyer", "Iyer", start, "Asia/Kolkata", null, null);
        Assert.Equal(["Date", "Time", "Clinic"], bare.Facts.Select(f => f.Label)); // no phone: no row for it
        Assert.Equal("India Standard Time", bare.Facts[1].Detail);
    }

    [Fact]
    public void AnOrganisationDecisionKeepsItsTextAndIsMedvyas()
    {
        var mail = OrganisationStates.Message("owner@example.test", Hostile, OrganisationState.NeedsChanges, "Upload the <registration> certificate");
        Assert.Equal($"Medvya reviewed the request for {Hostile} and needs changes.\n\nReason: Upload the <registration> certificate\n\nSign in to Medvya, update the request and send it again.", mail.Body);
        Assert.Contains("Upload the &lt;registration&gt; certificate", mail.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", mail.Html, StringComparison.Ordinal);
        Assert.Null(mail.Logo);
        var approved = OrganisationStates.Message("owner@example.test", "Iyer", OrganisationState.Approved, "ignored");
        Assert.Equal("Iyer is approved. Sign in to Medvya to register patients and book visits.", approved.Body);
    }

    [Fact]
    public void AStaffInvitationKeepsItsTextAndOffersOneButton()
    {
        var facility = new Facility { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Name = "Adyar", TimeZone = "Asia/Kolkata" };
        var mail = Invitations.Email("joseph@example.test", "Iyer Family Clinic", Role.Receptionist, facility, "https://clinic.test/invite/abc",
            new DateTimeOffset(2026, 11, 18, 5, 12, 0, TimeSpan.Zero), [1, 2, 3]);
        Assert.Equal("""
            You have been invited to join Iyer Family Clinic on Medvya as Receptionist at Adyar.

            Accept the invitation: https://clinic.test/invite/abc

            The link works once and expires on 18 Nov 2026, 10:42 (Asia/Kolkata). If you were not expecting this invitation, ignore this email.
            """, mail.Body);
        Assert.Contains(">Accept the invitation</a>", mail.Html, StringComparison.Ordinal);
        Assert.Contains("18 Nov 2026, 10:42", mail.Html, StringComparison.Ordinal);
        Assert.Contains("src=\"cid:logo\"", mail.Html, StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], mail.Logo);
    }

    static readonly EmailMessage Branded = new("meera.iyer@example.test", "Iyer Family Clinic: Appointment reminder", "Reminder: your appointment is on 17 Nov 2026.",
        null, "Iyer Family Clinic", "<p>Reminder</p>", [0x89, 0x50, 0x4E, 0x47]);

    [Fact]
    public async Task SmtpSendsTheTextAndTheHtmlAsAlternativesWithTheLogoInline()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var received = ReceiveOneAsync(listener);
            var sender = new SmtpEmailSender(Options.Create(new SmtpOptions
            {
                Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port, EnableSsl = false, From = "reminders@medvya.test",
            }));
            await sender.SendAsync(Branded);
            var raw = await received.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("multipart/alternative", raw, StringComparison.Ordinal);
            var plain = raw.IndexOf("text/plain", StringComparison.Ordinal);
            var html = raw.IndexOf("text/html", StringComparison.Ordinal);
            Assert.True(plain > 0 && html > plain, "plain text first, then the HTML");
            Assert.Contains("multipart/related", raw, StringComparison.Ordinal); // the logo travels with the HTML part
            Assert.Contains("Content-ID: <logo>", raw, StringComparison.Ordinal);
            Assert.Contains("image/png", raw, StringComparison.Ordinal);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void AcsSendsTheTextAndTheHtmlWithTheLogoAsAnInlineAttachment()
    {
        var mail = AcsEmailSender.Compose("reminders@medvya.test", Branded);
        Assert.Equal(Branded.Body, mail.Content.PlainText);
        Assert.Equal(Branded.Html, mail.Content.Html);
        var logo = Assert.Single(mail.Attachments);
        Assert.Equal(("logo", "image/png"), (logo.ContentId, logo.ContentType));
        Assert.Empty(AcsEmailSender.Compose("reminders@medvya.test", Branded with { Html = null }).Attachments); // no HTML, no inline image
    }

    /// <summary>A one-message SMTP server: answers the dialogue and returns what came after DATA.</summary>
    static async Task<string> ReceiveOneAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };
        await writer.WriteLineAsync("220 test");
        var data = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line == "DATA")
            {
                await writer.WriteLineAsync("354 go on");
                while (await reader.ReadLineAsync() is { } d && d != ".") data.AppendLine(d);
                await writer.WriteLineAsync("250 ok");
            }
            else if (line == "QUIT") { await writer.WriteLineAsync("221 bye"); break; }
            else await writer.WriteLineAsync("250 ok");
        }
        return data.ToString();
    }
}

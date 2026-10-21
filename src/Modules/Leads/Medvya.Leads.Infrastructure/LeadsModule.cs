using System.Net;
using System.Net.Mail;
using Medvya.Leads.Application;
using Medvya.Leads.Domain;
using Medvya.Leads.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Medvya.Leads.Infrastructure;

/// <summary>Where the founder's notification goes. Locally: the SMTP sink from docker-compose (localhost:2525).</summary>
public sealed class LeadsEmailOptions
{
    public string? To { get; set; }
    public string From { get; set; } = "no-reply@medvya.example";
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    /// <summary>Writes .eml files here instead of sending (tests).</summary>
    public string? PickupDirectory { get; set; }
}

public static class LeadsModule
{
    public static IServiceCollection AddLeadsModule(this IServiceCollection services, string connectionString, LeadsEmailOptions email) =>
        services
            .AddDbContext<LeadsDbContext>(o => o.UseSqlServer(connectionString))
            .AddSingleton(email)
            .AddScoped<IDemoRequests, DemoRequests>()
            .AddScoped<IWaitlist, Waitlist>();
}

sealed class Waitlist(LeadsDbContext db) : IWaitlist
{
    public async Task JoinAsync(WaitlistEntry entry, CancellationToken cancellationToken = default)
    {
        db.Waitlist.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }
}

sealed partial class DemoRequests(LeadsDbContext db, LeadsEmailOptions email, ILogger<DemoRequests> log) : IDemoRequests
{
    public async Task SubmitAsync(DemoRequest request, CancellationToken cancellationToken = default)
    {
        db.DemoRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);

        if (email.To is null || (email.SmtpHost is null && email.PickupDirectory is null))
        {
            EmailNotConfigured(log, request.Id);
            return;
        }
        try
        {
            // shortcut: System.Net.Mail over SMTP; move to the transactional email provider when it is contracted (data map: SendGrid, MDV-P1-044).
            using var smtp = email.PickupDirectory is { } dir
                ? new SmtpClient { DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory, PickupDirectoryLocation = dir }
                : new SmtpClient(email.SmtpHost, email.SmtpPort) { EnableSsl = email.EnableSsl };
            using var message = new MailMessage(email.From, email.To, $"Demo request: {request.Organisation}", Body(request));
            await smtp.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException or WebException)
        {
            EmailFailed(log, request.Id); // the request is stored; the founder can still find it
        }
    }

    // The free-text message stays in the database: a visitor could have typed patient details into it despite the warning.
    static string Body(DemoRequest r) => $"""
        New demo request {r.Id} received {r.ReceivedAt:u}.

        Name: {r.Name}
        Organisation: {r.Organisation}
        Role: {r.Role}
        Country: {r.Country}
        Email: {r.Email ?? "-"}
        Phone: {r.Phone ?? "-"}
        Message: {(string.IsNullOrEmpty(r.Message) ? "none" : "stored with the request, not emailed")}
        """;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo request {RequestId} stored; founder email is not configured")]
    static partial void EmailNotConfigured(ILogger logger, Guid requestId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Demo request {RequestId} stored; sending the founder email failed")]
    static partial void EmailFailed(ILogger logger, Guid requestId);
}

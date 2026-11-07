using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Medvya.Notifications.Infrastructure;

/// <summary>
/// Compliance reminders for the practice's owners (MDV-P1-010), run by the host's job inside each tenant's scope:
/// a doctor's licence or the practice's registration 60 / 30 / 7 days before it expires, on its last valid day (0) and, if the job missed that, once after; and "quarterly access
/// review due" at the start of each calendar quarter until the review is complete. Each reminder is an in-app notification (and, for a
/// licence, an email to the practice contact address) written once: the dedupe key makes a second run, or a second instance, add nothing.
/// Every reminder is audited (system actor) in the same transaction. Messages name no doctor and no patient.
/// shortcut: the email goes to the practice contact address only (no per-owner address list), and nothing is sent when it is unset; the in-app item always is.
/// </summary>
public sealed partial class ComplianceReminders(
    NotificationsDbContext db, IComplianceFacts facts, ICurrentTenant tenant, IEmailSender email, IAuditLog audit, TimeProvider clock, ILogger<ComplianceReminders> log) : IComplianceReminders
{
    /// <summary>Days before expiry at which a reminder is due, smallest first.</summary>
    static readonly int[] Steps = [0, 7, 30, 60];

    /// <summary>The reminder step a licence is at: the smallest step that is at least its days left (negative days are step 0), or null when more than 60 days remain.</summary>
    public static int? StepFor(int daysLeft) => Steps.Cast<int?>().FirstOrDefault(s => daysLeft <= s);

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var read = await facts.ReadAsync(today, cancellationToken);
        var raised = 0;
        var failed = 0;
        foreach (var l in read.Licences)
        {
            if (StepFor(l.ExpiresOn.DayNumber - today.DayNumber) is not { } step) continue;
            try { raised += await RaiseAsync(tenantId, l.FacilityId, StaffNotificationKind.LicenceExpiring, l.MembershipId, step, $"licence:{l.MembershipId:N}:{l.ExpiresOn:yyyyMMdd}:{step}", "compliance.licence_expiry.notify", now,
                    read.ContactEmail is { } to ? ct => email.SendAsync(LicenceMail(to, read.ClinicName, step), ct) : null, cancellationToken); }
            catch (Exception e) when (e is not OperationCanceledException) { failed++; Failed(log, tenantId, e.GetType().Name); } // the type only: messages can carry addresses
        }
        // The practice's own registration: one item per location (each location's admins see it), the email once.
        if (read.RegistrationExpiresOn is { } expires && StepFor(expires.DayNumber - today.DayNumber) is { } regStep)
            foreach (var (facility, i) in read.FacilityIds.Select((f, i) => (f, i)))
            {
                try { raised += await RaiseAsync(tenantId, facility, StaffNotificationKind.RegistrationExpiring, null, regStep, $"registration:{facility:N}:{expires:yyyyMMdd}:{regStep}", "compliance.registration_expiry.notify", now,
                        i == 0 && read.ContactEmail is { } to ? ct => email.SendAsync(RegistrationMail(to, read.ClinicName, regStep), ct) : null, cancellationToken); }
                catch (Exception e) when (e is not OperationCanceledException) { failed++; Failed(log, tenantId, e.GetType().Name); }
            }
        if (read.ReviewDueQuarter is { } quarter)
            foreach (var facility in read.FacilityIds)
                raised += await RaiseAsync(tenantId, facility, StaffNotificationKind.AccessReviewDue, null, null, $"review:{quarter}:{facility:N}", "compliance.access_review.due", now, null, cancellationToken);
        if (failed > 0) throw new InvalidOperationException($"{failed} compliance reminder(s) failed.");
        return raised;
    }

    /// <summary>Saves the item and its audit event, and sends the email, in one transaction: a failed email undoes the item, so the next run tries again.</summary>
    async Task<int> RaiseAsync(Guid tenantId, Guid facilityId, StaffNotificationKind kind, Guid? refId, int? detail, string key, string action, DateTimeOffset now,
        Func<CancellationToken, Task>? send, CancellationToken ct)
    {
        if (await db.StaffNotifications.AnyAsync(n => n.DedupeKey == key, ct)) return 0;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        StaffNotifications.Raise(db, tenantId, facilityId, kind, refId, detail, now, key);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, ActorUserId = null, Action = action, ResourceType = "compliance", ResourceId = refId ?? facilityId,
            Purpose = "operations", Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, tx.GetDbTransaction(), ct);
        if (send is not null) await send(ct);
        await tx.CommitAsync(ct);
        return 1;
    }

    static EmailMessage LicenceMail(string to, string clinic, int step) => new(to,
        step <= 0 ? $"A doctor's licence at {clinic} expires today or has expired" : $"A doctor's licence at {clinic} expires in {step} days or less",
        $"""
        A doctor's registration (licence) at {clinic} {(step <= 0 ? "expires today or has already expired" : $"expires within {step} days")}.

        After it expires the doctor can no longer record or sign in Medvya until the renewed licence is added and verified.
        Open Settings, Doctors in Medvya, choose the doctor and add the renewed licence.
        """);

    static EmailMessage RegistrationMail(string to, string clinic, int step) => new(to,
        step <= 0 ? $"The registration of {clinic} expires today or has expired" : $"The registration of {clinic} expires in {step} days or less",
        $"""
        The clinic registration of {clinic} {(step <= 0 ? "expires today or has already expired" : $"expires within {step} days")}.

        Renew it with the issuing authority, then enter the new expiry date in Medvya under Settings, Profile.
        """);

    [LoggerMessage(Level = LogLevel.Error, Message = "Compliance reminder failed for tenant {TenantId}: {ErrorType}.")]
    static partial void Failed(ILogger logger, Guid tenantId, string errorType);
}

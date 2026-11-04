using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure.Persistence;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Medvya.Notifications.Infrastructure;

/// <summary>
/// Turns appointment events into queued messages (MDV-P1-032). A booking queues a confirmation and the 24 h and 2 h reminders; a
/// reschedule or cancellation first cancels everything still queued for the visit, then queues its own message (and, after a
/// reschedule, reminders for the new time). Walk-ins get nothing: the patient is at the desk. Idempotent on the event id.
/// </summary>
public sealed class AppointmentNotifier(NotificationsDbContext db, IFacilityDirectory facilities, IOptions<NotificationOptions> options, TimeProvider clock) : IAppointmentSubscriber
{
    public async Task HandleAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appointmentEvent);
        var e = appointmentEvent;
        if (await db.Notifications.AnyAsync(n => n.SourceEventId == e.EventId, cancellationToken)) return; // published twice

        var now = clock.GetUtcNow();
        var o = options.Value;
        var facility = await facilities.FindAsync(e.FacilityId, cancellationToken);
        var zone = facility is null ? null : TimeZoneInfo.FindSystemTimeZoneById(facility.TimeZone);
        var rows = new List<Notification>();
        void Add(NotificationKind kind, DateTimeOffset sendAt)
        {
            foreach (var channel in o.Channels.Distinct())
                rows.Add(new Notification
                {
                    Id = Guid.CreateVersion7(), TenantId = e.TenantId, FacilityId = e.FacilityId, AppointmentId = e.AppointmentId, PatientId = e.PatientId, SourceEventId = e.EventId,
                    Channel = channel, Kind = kind, AppointmentStart = e.Start, SendAt = sendAt, Status = NotificationStatus.Queued, CreatedAt = now, UpdatedAt = now,
                });
        }
        void AddReminders()
        {
            if (zone is null) return;
            foreach (var kind in new[] { NotificationKind.Reminder24h, NotificationKind.Reminder2h })
                if (NotificationRules.ReminderTime(e.Start, NotificationRules.Lead(kind)!.Value, now, zone, o.QuietStart, o.QuietEnd) is { } at) Add(kind, at);
        }

        switch (e.Kind)
        {
            case AppointmentEventKind.Booked when !e.WalkIn:
                Add(NotificationKind.Confirmation, e.OccurredAt);
                AddReminders();
                break;
            case AppointmentEventKind.Rescheduled:
                Add(NotificationKind.Rescheduled, e.OccurredAt);
                AddReminders();
                break;
            case AppointmentEventKind.Cancelled:
                Add(NotificationKind.Cancelled, e.OccurredAt);
                break;
        }

        // The visit changed: whatever older message is still waiting is wrong now. Messages already sent stay as they are.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (e.Kind != AppointmentEventKind.Booked)
            await db.Notifications.Where(n => n.AppointmentId == e.AppointmentId && n.Status == NotificationStatus.Queued && n.SourceEventId != e.EventId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, NotificationStatus.Cancelled).SetProperty(n => n.FailureCode, "visit_changed").SetProperty(n => n.UpdatedAt, now), cancellationToken);
        db.Notifications.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}

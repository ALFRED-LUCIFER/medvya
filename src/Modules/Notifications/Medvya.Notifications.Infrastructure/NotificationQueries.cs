using Medvya.BuildingBlocks.Domain;
using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Notifications.Infrastructure;

/// <summary>Raised when the caller has no view access at the facility (the host answers 403).</summary>
public sealed class NotificationAccessDeniedException : Exception
{
    public NotificationAccessDeniedException() : base("Not allowed at this facility.") { }
}

/// <summary>
/// Reception's view of messages (MDV-P1-032/033). Access is the membership at the facility, as for the calendar: read or full access
/// to "Register / book patients". Returns statuses and codes only, never text or addresses.
/// </summary>
public sealed class NotificationQueries(NotificationsDbContext db, IStaffAccess access) : INotificationQueries
{
    public async Task<IReadOnlyList<NotificationView>> ForAppointmentAsync(Guid facilityId, Guid appointmentId, CancellationToken cancellationToken = default)
    {
        await RequireAsync(facilityId, cancellationToken);
        return await db.Notifications.AsNoTracking().Where(n => n.FacilityId == facilityId && n.AppointmentId == appointmentId)
            .OrderBy(n => n.CreatedAt).ThenBy(n => n.SendAt).Select(n => View(n)).ToListAsync(cancellationToken);
    }

    public async Task<DeliveryReport> DeliveryAsync(Guid facilityId, DateTimeOffset since, int maxFailures = 100, CancellationToken cancellationToken = default)
    {
        await RequireAsync(facilityId, cancellationToken);
        var rows = db.Notifications.AsNoTracking().Where(n => n.FacilityId == facilityId && n.CreatedAt >= since && n.Status != NotificationStatus.Cancelled && n.Status != NotificationStatus.Suppressed);
        var grouped = await rows.GroupBy(n => new { n.Channel, n.Status }).Select(g => new { g.Key.Channel, g.Key.Status, Count = g.Count() }).ToListAsync(cancellationToken);
        var channels = grouped.GroupBy(g => g.Channel).Select(c => new ChannelDelivery(c.Key, c.Sum(x => x.Count),
            c.Where(x => x.Status == NotificationStatus.Delivered).Sum(x => x.Count),
            c.Where(x => x.Status is NotificationStatus.Failed or NotificationStatus.Bounced).Sum(x => x.Count),
            c.Where(x => x.Status is NotificationStatus.Queued or NotificationStatus.Sent).Sum(x => x.Count))).ToList();
        var failures = await rows.Where(n => n.Status == NotificationStatus.Failed || n.Status == NotificationStatus.Bounced)
            .OrderByDescending(n => n.UpdatedAt).Take(Math.Clamp(maxFailures, 1, 500)).Select(n => View(n)).ToListAsync(cancellationToken);
        return new DeliveryReport(channels, failures);
    }

    static NotificationView View(Notification n) => new(n.Id, n.AppointmentId, n.Kind, n.Channel, n.Status, n.Attempts, n.FailureCode, n.SendAt, n.UpdatedAt);

    async Task RequireAsync(Guid facilityId, CancellationToken ct)
    {
        if (facilityId == Guid.Empty || await access.AccessAsync(Capability.RegisterBookPatients, facilityId, ct) is not (Access.Read or Access.Full)) throw new NotificationAccessDeniedException();
    }
}

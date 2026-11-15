using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Application;
using Medvya.Notifications.Application;
using Medvya.Notifications.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Medvya.Notifications.Infrastructure;

/// <summary>
/// Private push for the notification centre (MDV-P5-038, ADR-011 §8), run by the host's job inside each tenant's scope. Each new item is
/// claimed once (PushedAt) and sent to the live native sessions of the people who may see it in the list: an opaque id and the fixed text,
/// never its kind, a name or a result. A device with no token (the app turned pushes off, or the session was revoked or ended) gets nothing;
/// the in-app item is the record either way. Nothing personal is logged: ids and error types only, never a token.
/// shortcut: one try per device, no retry and no quiet hours; add both when the staff app registers real device tokens (MDV-P5-038 follow-up).
/// </summary>
public sealed partial class StaffPush(
    NotificationsDbContext db, IStaffAccess access, IStaffSessions sessions, ICurrentTenant tenant, IPushSender push, TimeProvider clock, ILogger<StaffPush> log) : IStaffPush
{
    public async Task<int> SendDueAsync(int batch = 100, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        var due = await db.StaffNotifications.AsNoTracking().Where(n => n.PushedAt == null).OrderBy(n => n.CreatedAt).Take(batch)
            .Select(n => new { n.Id, n.FacilityId, n.Kind }).ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var n in due)
        {
            // Claim first: a second run or instance skips it. At most once, because a push is only a nudge and the list is the record.
            if (await db.StaffNotifications.Where(x => x.Id == n.Id && x.PushedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.PushedAt, clock.GetUtcNow()), cancellationToken) == 0) continue;
            var members = (await access.FacilityAccessAsync(StaffNotifications.NeededFor(n.Kind), n.FacilityId, cancellationToken))
                .Where(m => StaffNotifications.Sees(m.Value)).Select(m => m.Key).ToList();
            if (members.Count == 0) continue;
            foreach (var token in await sessions.PushTokensAsync(tenantId, members, cancellationToken))
            {
                try
                {
                    await push.SendAsync(new PushMessage(token, n.Id), cancellationToken);
                    sent++;
                }
                catch (Exception e) when (e is not OperationCanceledException) { Failed(log, n.Id, e.GetType().Name); }
            }
        }
        return sent;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Push for staff notification {NotificationId} failed: {ErrorType}.")]
    static partial void Failed(ILogger logger, Guid notificationId, string errorType);
}

/// <summary>Development only: logs that a push would go out, by item id. Never the token, and never reaches a phone.</summary>
public sealed partial class LogPushSender(ILogger<LogPushSender> log) : IPushSender
{
    public Task SendAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        Pushed(log, message.Id);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Push (development log) for staff notification {NotificationId}.")]
    static partial void Pushed(ILogger logger, Guid notificationId);
}

/// <summary>
/// No push service is set up yet (Azure Notification Hubs in the tenant's country needs the APNs key and an FCM project, MDV-P5-038), so a
/// push fails closed: logged by type, and the item stays in the in-app list.
/// shortcut: add the Notification Hubs sender when the staff app registers native device tokens with their platform (MDV-P5-038 follow-up).
/// </summary>
public sealed class UnconfiguredPushSender : IPushSender
{
    public Task SendAsync(PushMessage message, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No push service is configured.");
}

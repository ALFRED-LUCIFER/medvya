using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Medvya.Notifications.Infrastructure;

/// <summary>
/// A practice's notification settings (MDV-P1-006). Saving writes the row and its audit event in one transaction, and cancels what
/// is still queued for every message kind or channel that was just turned off, so a turned-off reminder never goes out.
/// Turning it back on does not bring the cancelled ones back; it applies to visits booked from then on.
/// shortcut: the audit event names the change but not the before/after values (no settings-change table in this module); add it if the owner needs to see old values.
/// </summary>
public sealed class NotificationPreferences(
    NotificationsDbContext db, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, IOptions<NotificationOptions> options, TimeProvider clock) : INotificationPreferences
{
    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    public async Task<NotificationPreferencesView> GetAsync(CancellationToken cancellationToken = default) =>
        await db.Preferences.AsNoTracking().Select(p => ViewOf(p)).SingleOrDefaultAsync(cancellationToken) ?? Defaults(options.Value);

    static NotificationPreferencesView Defaults(NotificationOptions o) =>
        new(true, true, true, true, o.Channels.Contains(NotificationChannel.Email), o.Channels.Contains(NotificationChannel.Sms), o.QuietStart, o.QuietEnd);

    static NotificationPreferencesView ViewOf(NotificationPreference p) => new(p.Confirmation, p.Reminder24h, p.Reminder2h, p.Changes, p.Email, p.Sms, p.QuietStart, p.QuietEnd);

    public async Task<NotificationPreferencesView> SaveAsync(NotificationPreferencesView changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var before = await GetAsync(cancellationToken);
        var row = await db.Preferences.SingleOrDefaultAsync(cancellationToken);
        if (row is null) db.Preferences.Add(row = new NotificationPreference { TenantId = TenantId });
        (row.Confirmation, row.Reminder24h, row.Reminder2h, row.Changes, row.Email, row.Sms, row.QuietStart, row.QuietEnd) =
            (changes.Confirmation, changes.Reminder24h, changes.Reminder2h, changes.Changes, changes.Email, changes.Sms, changes.QuietStart, changes.QuietEnd);
        var now = clock.GetUtcNow();
        (row.ChangedBy, row.ChangedAt) = (user.UserId, now);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var kind in Enum.GetValues<NotificationKind>().Where(k => before.Allows(k) && !changes.Allows(k)))
            await CancelQueuedAsync(n => n.Kind == kind, now, cancellationToken);
        foreach (var channel in Enum.GetValues<NotificationChannel>().Where(c => before.Allows(c) && !changes.Allows(c)))
            await CancelQueuedAsync(n => n.Channel == channel, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = "settings.notifications.update", ResourceType = "settings", ResourceId = TenantId,
            Purpose = "operations", Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return changes;
    }

    Task<int> CancelQueuedAsync(System.Linq.Expressions.Expression<Func<Notification, bool>> which, DateTimeOffset now, CancellationToken ct) =>
        db.Notifications.Where(n => n.Status == NotificationStatus.Queued).Where(which)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, NotificationStatus.Cancelled).SetProperty(n => n.FailureCode, "turned_off").SetProperty(n => n.UpdatedAt, now), ct);
}

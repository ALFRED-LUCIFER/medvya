using Medvya.BuildingBlocks.Domain;
using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Notifications.Infrastructure;

/// <summary>
/// The notification centre (MDV-P1-034). A user sees the items of the facility they work at whose kind their role may see (the kind's
/// capability, read from the database on each call); read state is kept per user so it follows them across devices.
/// </summary>
public sealed class StaffNotifications(NotificationsDbContext db, IStaffAccess access, ICurrentUser user, TimeProvider clock) : IStaffNotifications
{
    /// <summary>What a kind needs: message failures are for the desk (view access to registering and booking).</summary>
    static Capability NeededFor(StaffNotificationKind kind) => kind switch
    {
        StaffNotificationKind.MessageFailed => Capability.RegisterBookPatients,
        // Practice administration (owner doctor, practice admin, hospital IT): the same people who manage staff and doctors.
        StaffNotificationKind.LicenceExpiring or StaffNotificationKind.AccessReviewDue => Capability.StaffRolesIntegrations,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Queues the item for the same save as the change that caused it. Raising the same event again adds nothing.</summary>
    public static void Raise(NotificationsDbContext db, Notification n, DateTimeOffset now, string dedupeKey) =>
        Raise(db, n.TenantId, n.FacilityId, StaffNotificationKind.MessageFailed, n.AppointmentId, null, now, dedupeKey);

    public static void Raise(NotificationsDbContext db, Guid tenantId, Guid facilityId, StaffNotificationKind kind, Guid? refId, int? detail, DateTimeOffset now, string dedupeKey)
    {
        if (db.StaffNotifications.Local.Any(x => x.TenantId == tenantId && x.DedupeKey == dedupeKey)) return;
        db.StaffNotifications.Add(new StaffNotification
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, FacilityId = facilityId, Kind = kind, RefId = refId, Detail = detail, DedupeKey = dedupeKey, CreatedAt = now,
        });
    }

    async Task<(Guid User, IReadOnlyList<StaffNotificationKind> Kinds)> WhoAsync(Guid facilityId, CancellationToken ct)
    {
        var me = user.UserId ?? throw new NotificationAccessDeniedException();
        if (facilityId == Guid.Empty) throw new NotificationAccessDeniedException();
        var kinds = new List<StaffNotificationKind>();
        foreach (var k in Enum.GetValues<StaffNotificationKind>())
            if (await access.AccessAsync(NeededFor(k), facilityId, ct) is Access.Read or Access.Full or Access.Config) kinds.Add(k);
        if (kinds.Count == 0) throw new NotificationAccessDeniedException();
        return (me, kinds);
    }

    IQueryable<StaffNotification> Visible(Guid facilityId, IReadOnlyList<StaffNotificationKind> kinds) =>
        db.StaffNotifications.Where(n => n.FacilityId == facilityId && kinds.Contains(n.Kind));

    public async Task<StaffNotificationList> ListAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        var (me, kinds) = await WhoAsync(facilityId, cancellationToken);
        var items = await Visible(facilityId, kinds).AsNoTracking().OrderByDescending(n => n.CreatedAt).Take(50)
            .Select(n => new StaffNotificationView(n.Id, n.Kind, n.RefId, n.Detail, n.CreatedAt, db.StaffNotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == me))).ToListAsync(cancellationToken);
        var unread = await Visible(facilityId, kinds).CountAsync(n => !db.StaffNotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == me), cancellationToken);
        return new StaffNotificationList(unread, items);
    }

    public async Task<bool> MarkReadAsync(Guid facilityId, Guid id, CancellationToken cancellationToken = default)
    {
        var (me, kinds) = await WhoAsync(facilityId, cancellationToken);
        if (!await Visible(facilityId, kinds).AnyAsync(n => n.Id == id, cancellationToken)) return false;
        await AddReadsAsync(me, [id], cancellationToken);
        return true;
    }

    public async Task MarkAllReadAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        var (me, kinds) = await WhoAsync(facilityId, cancellationToken);
        var unread = await Visible(facilityId, kinds).Where(n => !db.StaffNotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == me)).Select(n => n.Id).ToListAsync(cancellationToken);
        await AddReadsAsync(me, unread, cancellationToken);
    }

    async Task AddReadsAsync(Guid me, List<Guid> ids, CancellationToken ct)
    {
        var tenant = await db.StaffNotifications.Where(n => ids.Contains(n.Id)).Select(n => n.TenantId).FirstOrDefaultAsync(ct);
        var already = await db.StaffNotificationReads.Where(r => r.UserId == me && ids.Contains(r.NotificationId)).Select(r => r.NotificationId).ToListAsync(ct);
        foreach (var id in ids.Except(already)) db.StaffNotificationReads.Add(new StaffNotificationRead { TenantId = tenant, NotificationId = id, UserId = me, ReadAt = clock.GetUtcNow() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* the same user marked it on another device at the same moment: it is read either way */ }
    }
}

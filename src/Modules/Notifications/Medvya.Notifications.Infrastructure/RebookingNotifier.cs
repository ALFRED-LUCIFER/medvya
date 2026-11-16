using System.Data.Common;
using Medvya.BuildingBlocks.Domain;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure.Persistence;
using Medvya.Scheduling.Application;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Notifications.Infrastructure;

/// <summary>
/// Leave or an offboarding moved booked visits to "needs rebooking": one notification-centre item for the desk (MDV-P1-034) with the
/// count, the doctor's calendar and the first day, written on the action's own transaction like <see cref="AccessRequestNotifier"/>. No patient.
/// </summary>
public sealed class RebookingNotifier(ICurrentTenant tenant, TimeProvider clock) : IRebookingParticipant
{
    public async Task NeedsRebookingAsync(Guid facilityId, Guid calendarId, DateOnly firstDay, int count, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        var now = clock.GetUtcNow();
        await using var db = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(transaction.Connection!).Options, tenant);
        await db.Database.UseTransactionAsync(transaction, cancellationToken);
        StaffNotifications.Raise(db, tenantId, facilityId, StaffNotificationKind.VisitsNeedRebooking, calendarId, count, now, $"rebook:{calendarId:N}:{now.UtcTicks}", firstDay);
        await db.SaveChangesAsync(cancellationToken);
    }
}

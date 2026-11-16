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
        await using var db = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(transaction.Connection!).Options, tenant);
        await db.Database.UseTransactionAsync(transaction, cancellationToken);
        // Called once per action and facility, and a retried action rolled back its first item: a fresh key never collides (one doctor offboarded
        // from two locations in the same instant would, with a doctor-and-time key, and roll the whole offboarding back).
        StaffNotifications.Raise(db, tenantId, facilityId, StaffNotificationKind.VisitsNeedRebooking, calendarId, count, clock.GetUtcNow(), $"rebook:{Guid.CreateVersion7():N}", firstDay);
        await db.SaveChangesAsync(cancellationToken);
    }
}

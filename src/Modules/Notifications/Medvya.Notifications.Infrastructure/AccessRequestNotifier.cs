using System.Data.Common;
using Medvya.BuildingBlocks.Domain;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Notifications.Infrastructure;

/// <summary>
/// "Request access" tells the facility's owners (MDV-P2-024): one notification-centre item pointing at the request, written on the
/// request's own connection and transaction, so the request and its notification commit or roll back together. Ids only.
/// </summary>
public sealed class AccessRequestNotifier(ICurrentTenant tenant, TimeProvider clock) : IAccessRequestParticipant
{
    public async Task RequestedAsync(Guid requestId, Guid facilityId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        await using var db = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(transaction.Connection!).Options, tenant);
        await db.Database.UseTransactionAsync(transaction, cancellationToken);
        StaffNotifications.Raise(db, tenantId, facilityId, StaffNotificationKind.AccessRequested, requestId, null, clock.GetUtcNow(), $"access-request:{requestId:N}");
        await db.SaveChangesAsync(cancellationToken);
    }
}

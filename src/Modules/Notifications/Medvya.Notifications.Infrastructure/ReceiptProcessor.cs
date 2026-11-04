using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Notifications.Infrastructure;

/// <summary>
/// Applies provider delivery receipts to NOTIFICATION / NOTIFICATION_ATTEMPT (MDV-P1-033). A receipt only moves a message that was
/// handed over (Sent) to Delivered, Failed or Bounced, and a late "delivered" after a bounce is ignored, so duplicates and
/// out-of-order receipts are harmless. The host has verified the signature and entered the tenant named by the reference.
/// </summary>
public sealed class ReceiptProcessor(NotificationsDbContext db, TimeProvider clock) : IReceiptProcessor
{
    public async Task<bool> ApplyAsync(DeliveryReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.State is not (NotificationStatus.Delivered or NotificationStatus.Failed or NotificationStatus.Bounced)) return false;
        if (!NotificationReference.TryParse(receipt.Reference, out _, out var id)) return false;
        var now = clock.GetUtcNow();
        var code = receipt.ReasonCode is { Length: > 0 } c ? new string(c.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_').Take(60).ToArray()) : null; // a short code, never free text
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await db.Notifications.Where(n => n.Id == id && n.Status == NotificationStatus.Sent)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, receipt.State).SetProperty(n => n.FailureCode, code).SetProperty(n => n.UpdatedAt, now), cancellationToken);
        if (changed == 0) return false;
        var tenant = await db.Notifications.Where(n => n.Id == id).Select(n => n.TenantId).SingleAsync(cancellationToken);
        db.Attempts.Add(new NotificationAttempt { Id = Guid.CreateVersion7(), TenantId = tenant, NotificationId = id, At = now, Outcome = receipt.State, Code = code });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }
}

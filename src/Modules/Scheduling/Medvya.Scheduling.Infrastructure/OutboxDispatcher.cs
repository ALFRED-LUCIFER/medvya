using System.Text.Json;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// Publishes the transactional outbox to <see cref="IAppointmentSubscriber"/>s, oldest first, marking each event published only
/// after every subscriber took it (at least once). A subscriber that fails stops the batch, so order is kept and the event is
/// retried by the next run.
/// shortcut: one failing subscriber call holds back that tenant's later events; add a dead-letter state when more than one
/// module subscribes.
/// </summary>
public sealed partial class OutboxDispatcher(SchedulingDbContext db, IEnumerable<IAppointmentSubscriber> subscribers, TimeProvider clock, ILogger<OutboxDispatcher> log) : IOutboxDispatcher
{
    public async Task<IReadOnlyList<Guid>> TenantsAsync(CancellationToken cancellationToken = default) =>
        await db.OutboxTenants.AsNoTracking().Select(t => t.TenantId).ToListAsync(cancellationToken);

    public async Task<int> DispatchAsync(int batch = 100, CancellationToken cancellationToken = default)
    {
        var pending = await db.Outbox.Where(e => e.PublishedAt == null).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).Take(batch).ToListAsync(cancellationToken);
        foreach (var row in pending)
        {
            if (Map(row) is { } mapped)
                foreach (var subscriber in subscribers) await subscriber.HandleAsync(mapped, cancellationToken);
            row.PublishedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }
        return pending.Count;
    }

    /// <summary>Null for events nobody outside Scheduling acts on (reassignments keep the time) and for payloads that cannot be read (logged by id).</summary>
    AppointmentEvent? Map(OutboxEvent row)
    {
        var kind = row.Type switch
        {
            "AppointmentBooked.v1" => AppointmentEventKind.Booked,
            "AppointmentRescheduled.v1" => AppointmentEventKind.Rescheduled,
            "AppointmentCancelled.v1" => AppointmentEventKind.Cancelled,
            "AppointmentNoShow.v1" => AppointmentEventKind.NoShow,
            "AppointmentNeedsRebooking.v1" => AppointmentEventKind.NeedsRebooking,
            _ => (AppointmentEventKind?)null,
        };
        if (kind is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(row.Payload);
            var p = doc.RootElement;
            return new AppointmentEvent(row.Id, kind.Value, row.TenantId, row.AggregateId, p.GetProperty("facilityId").GetGuid(), p.GetProperty("patientId").GetGuid(),
                p.GetProperty("start").GetDateTimeOffset(), p.TryGetProperty("walkIn", out var w) && w.GetBoolean(), row.OccurredAt);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            Unreadable(log, row.Id);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox event {EventId} has an unreadable payload and is skipped.")]
    static partial void Unreadable(ILogger logger, Guid eventId);
}

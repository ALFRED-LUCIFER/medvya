using System.Globalization;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Audit.Infrastructure;

/// <summary>Keyset paging on (OccurredAt, Id) descending: stable while new events arrive, and it uses the (TenantId, OccurredAt) index.</summary>
public sealed class AuditTrail(AuditDbContext db) : IAuditTrail
{
    public async Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var inRange = db.Events.AsNoTracking().Where(e => query.FacilityId == null || e.FacilityId == query.FacilityId)
            .Where(e => (query.From == null || e.OccurredAt >= query.From) && (query.To == null || e.OccurredAt < query.To));
        var events = inRange
            .Where(e => (query.ActorUserId == null || e.ActorUserId == query.ActorUserId)
                && (query.PatientId == null || e.PatientId == query.PatientId)
                && (query.Action == null || e.Action == query.Action));
        if (query.After is not null)
        {
            var (at, id) = ParseCursor(query.After);
            events = events.Where(e => e.OccurredAt < at || (e.OccurredAt == at && e.Id.CompareTo(id) < 0));
        }
        var page = await events.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Take(query.Limit + 1).ToListAsync(cancellationToken);
        var more = page.Count > query.Limit;
        if (more) page.RemoveAt(query.Limit);
        // shortcut: distinct actions are read per request over the date range; cache or keep a list when a tenant's range gets large.
        var actions = await inRange.Select(e => e.Action).Distinct().OrderBy(a => a).Take(100).ToListAsync(cancellationToken);
        return new AuditPage(page, more ? Cursor(page[^1]) : null, actions);
    }

    static string Cursor(AuditEvent e) => $"{e.OccurredAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}_{e.Id}";

    static (DateTimeOffset At, Guid Id) ParseCursor(string cursor)
    {
        var parts = cursor.Split('_');
        if (parts.Length == 2 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && ticks > 0 && ticks <= DateTimeOffset.MaxValue.Ticks
            && Guid.TryParse(parts[1], out var id))
            return (new DateTimeOffset(ticks, TimeSpan.Zero), id);
        throw new ArgumentException("Not a page cursor.", nameof(cursor));
    }
}

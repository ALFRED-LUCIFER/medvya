using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// The ordered-queue query (MDV-P1-103): the place of each waiting visit under the clinic's rule (<see cref="QueueRules"/>), from its token
/// and any open nurse escalation. The board, the clinic overview and the queue display all read their order from here.
/// </summary>
public sealed class QueueOrdering(SchedulingDbContext db, IEnumerable<IEscalatedVisits> escalations)
{
    /// <summary>1, 2, … for the waiting visits (checked in, with the nurse, ready) among those given; the others have no place.</summary>
    public async Task<Dictionary<Guid, int>> PlacesAsync(IEnumerable<Appointment> visits, QueueRule rule, CancellationToken cancellationToken)
    {
        var waiting = visits.Where(v => VisitFlow.CanReassign(v.State)).ToList();
        if (waiting.Count == 0) return [];
        var ids = waiting.ConvertAll(v => v.Id);
        var tokens = await db.QueueTokens.AsNoTracking().Where(t => ids.Contains(t.AppointmentId))
            .ToDictionaryAsync(t => t.AppointmentId, cancellationToken);
        var escalated = new HashSet<Guid>();
        foreach (var source in escalations) escalated.UnionWith(await source.EscalatedAsync(ids, cancellationToken));
        var order = QueueRules.Order(waiting.Select(v =>
        {
            var t = tokens.GetValueOrDefault(v.Id);
            return new QueueRules.Waiting(v.Id, v.Start, v.WalkIn, t?.IssuedAt, t?.Prefix, t?.Number ?? 0, escalated.Contains(v.Id));
        }), rule.Order, rule.BookedFirst);
        return order.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1);
    }
}

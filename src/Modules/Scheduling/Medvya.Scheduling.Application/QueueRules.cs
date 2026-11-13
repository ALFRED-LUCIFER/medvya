using Medvya.Tenancy.Domain;

namespace Medvya.Scheduling.Application;

/// <summary>
/// The one order of a facility's waiting patients (MDV-P1-103). The doctor's day, the board's columns, the clinic overview and the queue
/// display all take it from here, so they never disagree. A nurse escalation comes first; then booked before walk-ins when the clinic says
/// so; then the clinic's rule. A walk-in counts from its arrival, never from the far slot it was given.
/// </summary>
public static class QueueRules
{
    /// <summary>A waiting visit as the rule sees it. Arrived is the check-in (token issue) time; Prefix and Number make its token.</summary>
    public sealed record Waiting(Guid Id, DateTimeOffset Start, bool WalkIn, DateTimeOffset? Arrived, string? Prefix, int Number, bool Escalated);

    /// <summary>The ids in queue order.</summary>
    public static IReadOnlyList<Guid> Order(IEnumerable<Waiting> waiting, QueueOrder order, bool bookedFirst)
    {
        var sorted = waiting.OrderByDescending(w => w.Escalated).ThenBy(w => bookedFirst && w.WalkIn);
        sorted = order switch
        {
            QueueOrder.Token => sorted.ThenBy(w => w.Prefix is null).ThenBy(w => w.Prefix, StringComparer.Ordinal).ThenBy(w => w.Number),
            QueueOrder.Appointment => sorted.ThenBy(w => w.WalkIn ? Arrival(w) : w.Start),
            _ => sorted.ThenBy(Arrival),
        };
        return [.. sorted.ThenBy(Arrival).ThenBy(w => w.Number).ThenBy(w => w.Id).Select(w => w.Id)]; // tokens issued in one instant keep their numbers
    }

    // A visit with no token yet (an older row) counts from its slot.
    static DateTimeOffset Arrival(Waiting w) => w.Arrived ?? w.Start;
}

using Medvya.Scheduling.Application;
using Medvya.Tenancy.Domain;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P1-103: the one queue rule, without a database (the ordered-queue query and the three screens are QueueOrderTests).</summary>
public sealed class QueueRulesTests
{
    static DateTimeOffset At(int hour, int minute) => new(2026, 10, 19, hour, minute, 0, TimeSpan.FromHours(5.5));

    // The audit's morning: Early booked 10:20 and came at 09:50; the walk-in came at 09:58 and was given a 19:00 slot; OnTime booked 10:00, came at 10:05.
    static readonly Guid Early = Guid.NewGuid(), WalkIn = Guid.NewGuid(), OnTime = Guid.NewGuid();
    static QueueRules.Waiting[] Morning(Guid? escalated = null) =>
    [
        new(OnTime, At(10, 0), false, At(10, 5), "A", 3, OnTime == escalated),
        new(WalkIn, At(19, 0), true, At(9, 58), "A", 2, WalkIn == escalated),
        new(Early, At(10, 20), false, At(9, 50), "A", 1, Early == escalated),
    ];

    public static TheoryData<QueueOrder, bool, Guid[]> Rules => new()
    {
        { QueueOrder.Arrival, false, [Early, WalkIn, OnTime] },
        { QueueOrder.Token, false, [Early, WalkIn, OnTime] },
        // The walk-in counts from 09:58, not from her 19:00 slot.
        { QueueOrder.Appointment, false, [WalkIn, OnTime, Early] },
        { QueueOrder.Arrival, true, [Early, OnTime, WalkIn] },
        { QueueOrder.Appointment, true, [OnTime, Early, WalkIn] },
    };

    [Theory, MemberData(nameof(Rules))]
    public void EachRuleOrdersTheMorning(QueueOrder order, bool bookedFirst, Guid[] expected) =>
        Assert.Equal(expected, QueueRules.Order(Morning(), order, bookedFirst));

    [Theory, MemberData(nameof(Rules))]
    public void ANurseEscalationGoesFirstUnderEveryRule(QueueOrder order, bool bookedFirst, Guid[] _) =>
        Assert.Equal(WalkIn, QueueRules.Order(Morning(escalated: WalkIn), order, bookedFirst)[0]);

    [Fact]
    public void TokensIssuedInOneInstantKeepTheirNumbersAndAVisitWithoutATokenCountsFromItsSlot()
    {
        var (a, b, none) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        QueueRules.Waiting[] waiting = [new(b, At(9, 0), false, At(9, 0), "A", 2, false), new(none, At(9, 30), false, null, null, 0, false), new(a, At(9, 40), false, At(9, 0), "A", 1, false)];
        Assert.Equal([a, b, none], QueueRules.Order(waiting, QueueOrder.Arrival, false));
        Assert.Equal([a, b, none], QueueRules.Order(waiting, QueueOrder.Token, false)); // no token: after every token
    }
}

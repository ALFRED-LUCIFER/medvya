using Medvya.Scheduling.Application;
using Medvya.Tenancy.Domain;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P1-103: the one queue rule, without a database (the ordered-queue query and the three screens are QueueOrderTests).</summary>
public sealed class QueueRulesTests
{
    static DateTimeOffset At(int hour, int minute) => new(2026, 10, 19, hour, minute, 0, TimeSpan.FromHours(5.5));

    // The audit's morning: Early booked 10:20 and came at 09:50; the walk-in came at 09:58 and was given a 19:00 slot; OnTime booked 10:00, came at 10:05.
    static readonly Guid Early = Guid.NewGuid(), WalkIn = Guid.NewGuid(), OnTime = Guid.NewGuid();
    static readonly Guid Rao = Guid.NewGuid(), Menon = Guid.NewGuid(); // two doctors' calendars
    static QueueRules.Waiting[] Morning(Guid? escalated = null) =>
    [
        new(OnTime, Rao, At(10, 0), false, At(10, 5), "A", 3, OnTime == escalated),
        new(WalkIn, Rao, At(19, 0), true, At(9, 58), "A", 2, WalkIn == escalated),
        new(Early, Rao, At(10, 20), false, At(9, 50), "A", 1, Early == escalated),
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
        QueueRules.Waiting[] waiting = [new(b, Rao, At(9, 0), false, At(9, 0), "A", 2, false), new(none, Rao, At(9, 30), false, null, null, 0, false), new(a, Rao, At(9, 40), false, At(9, 0), "A", 1, false)];
        Assert.Equal([a, b, none], QueueRules.Order(waiting, QueueOrder.Arrival, false));
        Assert.Equal([a, b, none], QueueRules.Order(waiting, QueueOrder.Token, false)); // no token: after every token
    }

    // Decision 10 of the validation plan: each doctor has their own sequence. Two doctors' patients arrive interleaved:
    // Rao 09:50, Menon 09:55, Rao 09:58 (walk-in), Menon 10:02 (walk-in), Rao 10:05.
    static readonly Guid MenonFirst = Guid.NewGuid(), MenonWalkIn = Guid.NewGuid();
    static QueueRules.Waiting[] TwoDoctors(Guid? escalated = null) =>
    [
        .. Morning(escalated),
        new(MenonWalkIn, Menon, At(18, 0), true, At(10, 2), "B", 2, MenonWalkIn == escalated),
        new(MenonFirst, Menon, At(10, 30), false, At(9, 55), "B", 1, MenonFirst == escalated),
    ];

    [Fact]
    public void EachDoctorCountsTheirOwnQueue()
    {
        var places = QueueRules.Places(TwoDoctors(), QueueOrder.Arrival, false);
        Assert.Equal((1, 2, 3), (places[Early], places[WalkIn], places[OnTime]));
        Assert.Equal((1, 2), (places[MenonFirst], places[MenonWalkIn])); // not 2 and 4: the other doctor's patients are never ahead
    }

    [Fact]
    public void BookedFirstAndAnEscalationApplyWithinTheDoctorsQueue()
    {
        var booked = QueueRules.Places(TwoDoctors(), QueueOrder.Arrival, true);
        Assert.Equal((1, 3, 2), (booked[Early], booked[WalkIn], booked[OnTime]));
        Assert.Equal((1, 2), (booked[MenonFirst], booked[MenonWalkIn]));

        var escalated = QueueRules.Places(TwoDoctors(escalated: MenonWalkIn), QueueOrder.Arrival, false);
        Assert.Equal((1, 2), (escalated[MenonWalkIn], escalated[MenonFirst])); // first in Dr. Menon's queue
        Assert.Equal((1, 2, 3), (escalated[Early], escalated[WalkIn], escalated[OnTime])); // and not in Dr. Rao's
    }

    [Fact]
    public void AReassignedVisitTakesItsPlaceInTheNewDoctorsQueueByItsArrival()
    {
        // Dr. Rao's 09:58 walk-in goes to Dr. Menon: there she arrived after 09:55 and before 10:02.
        var moved = TwoDoctors().Select(w => w.Id == WalkIn ? w with { Queue = Menon } : w);
        var places = QueueRules.Places(moved, QueueOrder.Arrival, false);
        Assert.Equal((1, 2), (places[Early], places[OnTime]));
        Assert.Equal((1, 2, 3), (places[MenonFirst], places[WalkIn], places[MenonWalkIn]));
    }
}

using System.Diagnostics.Metrics;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-028 on real SQL Server: today's board in facility time, one-tap check-in that a double tap or a second desk cannot
/// repeat, moves only along the visit state machine with a status event each, and the role check at the facility.
/// </summary>
public sealed class FrontDeskServiceTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    async Task<List<AppointmentState>> Events(Guid appointmentId)
    {
        await using var db = sql.Scheduling(h.Tenant);
        // Tests move the clock between steps, so the time orders the history.
        return await db.StatusEvents.Where(e => e.AppointmentId == appointmentId).OrderBy(e => e.OccurredAt).Select(e => e.State).ToListAsync();
    }

    static async Task<string> Code(Task task) => (await Assert.ThrowsAsync<VisitTransitionException>(() => task)).Code;

    static readonly string[] Outcomes = ["WithNurse", "CheckedIn", "invalid_transition"];

    [Fact]
    public async Task TheBoardShowsTodaysVisitsInTimeOrderWithIdentityToConfirmAndNoReasonText()
    {
        var later = await h.BookAsync(10, 0, h.SecondPatient);
        var first = await h.BookAsync(9);
        await h.BookAsync(0, start: At(9) + TimeSpan.FromDays(1)); // Tuesday: not on today's board

        var board = await h.FrontDesk(h.Reception).GetBoardAsync(h.Facility);
        Assert.Equal(("Asia/Kolkata", Monday, MondayMorning), (board.TimeZone, board.Date, board.Now));
        Assert.Equal([first.Id, later.Id], board.Cards.Select(c => c.Id));
        var card = board.Cards[0];
        Assert.Equal(("Meera Iyer (synthetic)", new DateOnly(1972, 3, 12), true, "Dr. Arjun Rao (synthetic)", ReasonCategory.ChronicReview, AppointmentState.Booked),
            (card.PatientName, card.DateOfBirth, card.RegistrationIncomplete, card.CalendarName, card.ReasonCategory, card.State));
        Assert.Equal(first.Reference, card.Reference);
    }

    [Fact]
    public async Task CheckInIsOneStepAndATapRepeatedChangesNothing()
    {
        var visit = await h.BookAsync(9);
        h.Clock.Advance(TimeSpan.FromMinutes(50));

        var checkedIn = await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn { ElapsedMs = 12_000 });
        Assert.Equal((AppointmentState.CheckedIn, MondayMorning + TimeSpan.FromMinutes(50)), (checkedIn.State, checkedIn.Since));
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        var again = await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        Assert.Equal((AppointmentState.CheckedIn, checkedIn.Since), (again.State, again.Since)); // the wait timer does not restart

        Assert.Equal([AppointmentState.Booked, AppointmentState.CheckedIn], await Events(visit.Id));
        await using var db = sql.Scheduling(h.Tenant);
        var row = await db.Appointments.SingleAsync(a => a.Id == visit.Id);
        Assert.Equal((AppointmentState.CheckedIn, h.Reception), (row.State, row.ChangedBy!.Value));
    }

    [Fact]
    public async Task ConcurrentCheckInsFromSeveralDesksCheckInOnce()
    {
        var visit = await h.BookAsync(9);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        var desks = Enumerable.Range(0, 8).Select(i => h.FrontDesk(i % 2 == 0 ? h.Reception : h.Nurse)).ToList();

        var results = await Task.WhenAll(desks.Select(d => Task.Run(() => d.CheckInAsync(visit.Id, new CheckIn()))));

        Assert.All(results, r => Assert.Equal(AppointmentState.CheckedIn, r.State));
        Assert.Equal([AppointmentState.Booked, AppointmentState.CheckedIn], await Events(visit.Id));
    }

    [Fact]
    public async Task ConcurrentDifferentMovesLeaveExactlyOneWinner()
    {
        var visit = await h.BookAsync(9);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        // One desk sends the patient to the nurse while another taps the stale "Check in" again: the repeat is harmless.
        var moves = Enumerable.Range(0, 6).Select(i => Task.Run(() => i % 2 == 0
            ? h.FrontDesk(h.Nurse).MoveAsync(visit.Id, new MoveVisit { State = AppointmentState.WithNurse })
            : h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn())));
        var outcomes = await Task.WhenAll(moves.Select(async m => { try { return (await m).State.ToString(); } catch (VisitTransitionException e) { return e.Code; } }));

        Assert.Contains("WithNurse", outcomes);
        Assert.All(outcomes, o => Assert.Contains(o, Outcomes));
        Assert.Equal([AppointmentState.Booked, AppointmentState.CheckedIn, AppointmentState.WithNurse], await Events(visit.Id));
    }

    [Fact]
    public async Task MovesFollowTheStateMachineToDoneAndAnythingElseIsRefused()
    {
        var visit = await h.BookAsync(9);
        var desk = h.FrontDesk(h.Reception);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("invalid_transition", await Code(desk.MoveAsync(visit.Id, new MoveVisit { State = AppointmentState.InConsult })));
        Assert.Equal("invalid_transition", await Code(desk.MoveAsync(visit.Id, new MoveVisit { State = AppointmentState.Cancelled })));

        await desk.CheckInAsync(visit.Id, new CheckIn());
        Assert.Equal("invalid_transition", await Code(desk.MoveAsync(visit.Id, new MoveVisit { State = AppointmentState.Booked }))); // never backwards
        foreach (var state in new[] { AppointmentState.WithNurse, AppointmentState.ReadyForDoctor, AppointmentState.InConsult, AppointmentState.Billing, AppointmentState.Done })
        {
            h.Clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(state, (await desk.MoveAsync(visit.Id, new MoveVisit { State = state })).State);
        }
        Assert.Equal("invalid_transition", await Code(desk.CheckInAsync(visit.Id, new CheckIn()))); // check-in only from Booked

        Assert.Equal(
            [AppointmentState.Booked, AppointmentState.CheckedIn, AppointmentState.WithNurse, AppointmentState.ReadyForDoctor, AppointmentState.InConsult, AppointmentState.Billing, AppointmentState.Done],
            await Events(visit.Id));
        await Assert.ThrowsAsync<SchedulingValidationException>(() => desk.MoveAsync(visit.Id, new MoveVisit()));
    }

    [Fact]
    public async Task AVisitOnAnotherDayCannotBeCheckedInToday()
    {
        var tuesday = await h.BookAsync(0, start: At(9) + TimeSpan.FromDays(1));
        Assert.Equal("not_today", await Code(h.FrontDesk(h.Reception).CheckInAsync(tuesday.Id, new CheckIn())));
        Assert.Equal([AppointmentState.Booked], await Events(tuesday.Id));
    }

    [Fact]
    public async Task OnlyRolesThatRunTheQueueAtThisFacilityMoveVisits()
    {
        var visit = await h.BookAsync(9);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        // A hospital doctor reads the queue; the portal user and an admin of another facility see nothing.
        Assert.Single((await h.FrontDesk(h.SecondDoctor).GetBoardAsync(h.Facility)).Cards);
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.FrontDesk(h.SecondDoctor).CheckInAsync(visit.Id, new CheckIn()));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.FrontDesk(h.PortalUser).GetBoardAsync(h.Facility));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.FrontDesk(h.Elsewhere).GetBoardAsync(h.Facility));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.FrontDesk(h.Elsewhere).CheckInAsync(visit.Id, new CheckIn()));

        Assert.Equal(AppointmentState.CheckedIn, (await h.FrontDesk(h.Nurse).CheckInAsync(visit.Id, new CheckIn())).State);
        Assert.Equal([AppointmentState.Booked, AppointmentState.CheckedIn], await Events(visit.Id));
    }

    [Fact]
    public async Task AnotherTenantCannotSeeOrMoveTheVisit()
    {
        var visit = await h.BookAsync(9);
        var otherTenant = Guid.NewGuid();
        await Assert.ThrowsAsync<AppointmentNotFoundException>(() => h.FrontDesk(h.Reception, otherTenant).CheckInAsync(visit.Id, new CheckIn()));
        await Assert.ThrowsAsync<AppointmentNotFoundException>(() => h.FrontDesk(h.Reception, otherTenant).MoveAsync(visit.Id, new MoveVisit { State = AppointmentState.CheckedIn }));
        Assert.Equal([AppointmentState.Booked], await Events(visit.Id));
    }

    [Fact]
    public async Task CheckInTimeIsMeasuredOnceForTheExitGate()
    {
        var visit = await h.BookAsync(9);
        var seconds = new List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (i, l) => { if (i.Meter.Name == FrontDeskService.MeterName) l.EnableMeasurementEvents(i); };
        listener.SetMeasurementEventCallback<double>((i, v, tags, _) =>
        {
            if (tags.ToArray().Any(t => t.Key == "tenant_id" && t.Value is Guid g && g == h.Tenant)) seconds.Add(v);
        });
        listener.Start();

        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn { ElapsedMs = 41_500 });
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn { ElapsedMs = 2_000 }); // a repeat is not a second check-in

        Assert.Equal([41.5], seconds);
    }
}

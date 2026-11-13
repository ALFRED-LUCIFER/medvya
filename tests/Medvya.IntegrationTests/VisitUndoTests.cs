using Medvya.Encounters.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-100 on real PostgreSQL: the desk undoes its own last move within 30 seconds and never later or after another move; a patient
/// who leaves ends as Left without being seen (off the board, never Done, slot freed); the desk cannot close or undo a visit while the
/// doctor's consultation is open; each is audited with the state it left, and none crosses a tenant or a role.
/// </summary>
public sealed class VisitUndoTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    static readonly CancelAppointment Unwell = new() { Reason = CancelReason.PatientUnwell };

    static async Task<string> Code(Task task) => (await Assert.ThrowsAsync<VisitTransitionException>(() => task)).Code;

    /// <summary>A visit checked in and waiting for the doctor (the nurse step is off, so check-in lands in Ready for doctor).</summary>
    async Task<BoardCard> ReadyAsync(int hour = 9, Guid? patient = null)
    {
        var visit = await h.BookAsync(hour, 0, patient);
        var card = await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        Assert.Equal(AppointmentState.ReadyForDoctor, card.State);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        return card;
    }

    Task<BoardCard> Move(Guid visit, AppointmentState to, Guid? by = null) => h.FrontDesk(by ?? h.Reception).MoveAsync(visit, new MoveVisit { State = to });
    Task<BoardCard> Undo(Guid visit, AppointmentState seen, Guid? by = null) => h.FrontDesk(by ?? h.Reception).UndoAsync(visit, new UndoMove { State = seen });

    async Task<List<(AppointmentState? From, AppointmentState To)>> Events(Guid visit)
    {
        await using var db = sql.Scheduling(h.Tenant);
        return [.. (await db.StatusEvents.Where(e => e.AppointmentId == visit).OrderBy(e => e.OccurredAt).ToListAsync()).Select(e => (e.FromState, e.State))];
    }

    async Task<(Guid? Actor, DateTimeOffset At, string? Detail)> Audited(string action, Guid visit)
    {
        await using var db = sql.Audit(h.Tenant);
        var row = await db.Events.SingleAsync(e => e.Action == action && e.ResourceId == visit);
        return (row.ActorUserId, row.OccurredAt, row.Detail);
    }

    [Fact]
    public async Task UndoWithinTheWindowPutsACalledInVisitBackInReadyForDoctorWithItsToken()
    {
        var ready = await ReadyAsync();
        var called = await Move(ready.Id, AppointmentState.InConsult);
        Assert.Equal((AppointmentState.InConsult, "Room 3"), (called.State, called.Room));
        h.Clock.Advance(VisitFlow.UndoWindow); // the last moment of the window

        var back = await Undo(ready.Id, AppointmentState.InConsult);
        Assert.Equal((AppointmentState.ReadyForDoctor, ready.Token, null, h.Clock.GetUtcNow()), (back.State, back.Token, back.Room, back.Since));
        Assert.Equal((AppointmentState.InConsult, AppointmentState.ReadyForDoctor), (await Events(ready.Id))[^1]); // the history keeps both moves
        // The audit row: who, when and the state it left.
        Assert.Equal((h.Reception, h.Clock.GetUtcNow(), "from=InConsult"), await Audited(SchedulingAudit.Undo, ready.Id));
        Assert.Equal(AppointmentState.InConsult, (await Move(ready.Id, AppointmentState.InConsult)).State); // and the desk carries on from there
    }

    // SAFETY (AC2, AC7), mutation check: removing the UndoWindow line in FrontDeskService.UndoAsync fails the first assertion; removing the
    // "changed" line (state seen and own move) fails the two after it.
    [Fact]
    public async Task UndoIsRefusedAfterTheWindowAfterAnotherMoveAndForSomeoneElsesMove()
    {
        var late = await ReadyAsync();
        await Move(late.Id, AppointmentState.InConsult);
        h.Clock.Advance(VisitFlow.UndoWindow + TimeSpan.FromSeconds(1));
        Assert.Equal("undo_expired", await Code(Undo(late.Id, AppointmentState.InConsult)));

        // Followed by another move: the undo names the move it meant (In consult), and the visit is past it.
        var moved = await ReadyAsync(10, h.SecondPatient);
        await Move(moved.Id, AppointmentState.InConsult);
        await Move(moved.Id, AppointmentState.Done);
        Assert.Equal("changed", await Code(Undo(moved.Id, AppointmentState.InConsult)));
        // Nobody undoes a colleague's move.
        Assert.Equal("changed", await Code(Undo(moved.Id, AppointmentState.Done, h.Nurse)));

        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal([AppointmentState.InConsult, AppointmentState.Done], await db.Appointments.OrderBy(a => a.Start).Select(a => a.State).ToListAsync());
    }

    // Mutation check: allowing Booked in VisitFlow.CanUndo fails the first assertion (the check-in would be undone, token issued).
    [Fact]
    public async Task CheckInAndAnUndoAreNotUndone()
    {
        await h.StepsAsync(nurse: true, billing: false); // check-in stops at Checked in, so the move just made is the check-in itself
        var booked = await h.BookAsync(11, 0, h.SecondPatient);
        Assert.Equal(AppointmentState.CheckedIn, (await h.FrontDesk(h.Reception).CheckInAsync(booked.Id, new CheckIn())).State);
        Assert.Equal("invalid_transition", await Code(Undo(booked.Id, AppointmentState.CheckedIn)));
        await h.StepsAsync(nurse: false, billing: false);

        var ready = await ReadyAsync();

        await Move(ready.Id, AppointmentState.InConsult);
        await Undo(ready.Id, AppointmentState.InConsult);
        Assert.Equal("invalid_transition", await Code(Undo(ready.Id, AppointmentState.ReadyForDoctor))); // an undo is not undone back into the room
        await Assert.ThrowsAsync<SchedulingValidationException>(() => h.FrontDesk(h.Reception).UndoAsync(ready.Id, new UndoMove()));
    }

    [Fact]
    public async Task APatientWhoLeavesIsOffTheBoardNeverDoneAndTheirSlotIsFree()
    {
        var gone = await ReadyAsync();
        var stays = await ReadyAsync(10, h.SecondPatient);

        var chip = await h.Booking(h.Reception).MarkLeftAsync(gone.Id, Unwell);
        Assert.Equal(AppointmentState.LeftWithoutBeingSeen, chip.State);
        var board = await h.FrontDesk(h.Reception).GetBoardAsync(h.Facility);
        Assert.Equal([stays.Id], board.Cards.Select(c => c.Id));
        Assert.Equal(1, board.LeftWithoutBeingSeen);
        Assert.DoesNotContain(board.Cards, c => c.State == AppointmentState.Done);
        var overview = await h.FrontDesk(h.Reception).GetOverviewAsync(h.Facility);
        Assert.Equal([stays.Id], overview.Waiting.Select(w => w.Id));
        Assert.Equal(0, overview.Doctors.Sum(d => d.Counts[AppointmentState.Done]));

        Assert.Equal((AppointmentState.ReadyForDoctor, AppointmentState.LeftWithoutBeingSeen), (await Events(gone.Id))[^1]);
        Assert.Equal((h.Reception, h.Clock.GetUtcNow(), "from=ReadyForDoctor;reason=PatientUnwell"), await Audited(SchedulingAudit.Left, gone.Id));
        await using (var db = sql.Scheduling(h.Tenant))
        {
            Assert.Equal(CancelReason.PatientUnwell, (await db.StatusEvents.SingleAsync(e => e.State == AppointmentState.LeftWithoutBeingSeen)).CancelReason);
            Assert.False(await db.ScheduleSlots.AnyAsync(s => s.AppointmentId == gone.Id));
            Assert.True(await db.QueueTokens.AnyAsync(t => t.AppointmentId == gone.Id)); // the token is history, never reissued
        }
        Assert.NotEqual(gone.Id, (await h.BookAsync(9, 0, h.SecondPatient)).Id); // 09:00 can be booked again

        // An end state: it moves nowhere, and is neither cancelled nor undone.
        Assert.Equal("invalid_transition", await Code(h.Booking(h.Reception).MarkLeftAsync(gone.Id, Unwell)));
        Assert.Equal("invalid_transition", await Code(h.Booking(h.Reception).CancelAsync(gone.Id, Unwell)));
        Assert.Equal("invalid_transition", await Code(Move(gone.Id, AppointmentState.InConsult)));
        Assert.Equal("invalid_transition", await Code(Undo(gone.Id, AppointmentState.LeftWithoutBeingSeen)));
    }

    [Fact]
    public async Task LeftWithoutBeingSeenNeedsAReasonAndAWaitingVisit()
    {
        var booked = await h.BookAsync(9);
        Assert.Equal("invalid_transition", await Code(h.Booking(h.Reception).MarkLeftAsync(booked.Id, Unwell))); // never arrived: that is a no-show or a cancellation
        var seen = await ReadyAsync(10, h.SecondPatient);
        await Assert.ThrowsAsync<SchedulingValidationException>(() => h.Booking(h.Reception).MarkLeftAsync(seen.Id, new CancelAppointment()));
        await Assert.ThrowsAsync<SchedulingValidationException>(() => h.Booking(h.Reception).MarkLeftAsync(seen.Id, new CancelAppointment { Reason = CancelReason.Other }));
        await Move(seen.Id, AppointmentState.InConsult);
        Assert.Equal("invalid_transition", await Code(h.Booking(h.Reception).MarkLeftAsync(seen.Id, Unwell))); // in the room: the patient is being seen
    }

    // SAFETY (AC4, AC7), mutation check: removing either RefuseWithDoctorAsync call in FrontDeskService fails this (the move, then the undo).
    [Fact]
    public async Task TheDeskCannotCloseOrUndoAVisitWhileTheDoctorsConsultationIsOpen()
    {
        var ready = await ReadyAsync();
        await Move(ready.Id, AppointmentState.InConsult);
        Assert.False(Assert.Single((await h.FrontDesk(h.Reception).GetBoardAsync(h.Facility)).Cards).WithDoctor); // called in, no consultation yet
        var encounter = await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(ready.Id));

        Assert.True(Assert.Single((await h.FrontDesk(h.Reception).GetBoardAsync(h.Facility)).Cards).WithDoctor);
        Assert.Equal("with_doctor", await Code(Move(ready.Id, AppointmentState.Done)));
        Assert.Equal("with_doctor", await Code(Undo(ready.Id, AppointmentState.InConsult))); // still inside the window, and the desk's own call-in
        await using (var db = sql.Scheduling(h.Tenant))
            Assert.Equal(AppointmentState.InConsult, (await db.Appointments.SingleAsync(a => a.Id == ready.Id)).State);

        // The doctor's End closes it; the card is then nobody's.
        await h.Get<IEncounters>(h.Doctor).EndAsync(encounter!.Id);
        var card = Assert.Single((await h.FrontDesk(h.Reception).GetBoardAsync(h.Facility)).Cards);
        Assert.Equal((AppointmentState.Done, false), (card.State, card.WithDoctor));
    }

    [Fact]
    public async Task OnlyTheQueueRolesOfThisFacilityAndTenantUndoOrMarkLeft()
    {
        var ready = await ReadyAsync();
        await Move(ready.Id, AppointmentState.InConsult);
        // A hospital doctor reads the queue only; an admin of another facility has nothing here; another tenant does not find the visit.
        foreach (var user in new[] { h.SecondDoctor, h.Elsewhere })
        {
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => Undo(ready.Id, AppointmentState.InConsult, user));
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Booking(user).MarkLeftAsync(ready.Id, Unwell));
        }
        var otherTenant = Guid.NewGuid();
        await Assert.ThrowsAsync<AppointmentNotFoundException>(() => h.FrontDesk(h.Reception, otherTenant).UndoAsync(ready.Id, new UndoMove { State = AppointmentState.InConsult }));
        await Assert.ThrowsAsync<AppointmentNotFoundException>(() => h.Booking(h.Reception, otherTenant).MarkLeftAsync(ready.Id, Unwell));
        await using (var db = sql.Scheduling(h.Tenant))
            Assert.Equal(AppointmentState.InConsult, (await db.Appointments.SingleAsync(a => a.Id == ready.Id)).State);

        // The nurse runs the queue too: she marks a waiting patient as left.
        var waiting = await ReadyAsync(10, h.SecondPatient);
        Assert.Equal(AppointmentState.LeftWithoutBeingSeen, (await h.Booking(h.Nurse).MarkLeftAsync(waiting.Id, Unwell)).State);
    }
}

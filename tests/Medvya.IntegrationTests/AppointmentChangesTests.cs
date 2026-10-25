using System.Text.Json;
using Microsoft.Data.SqlClient;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-024 on real SQL Server: reschedule, cancel with a reason and no-show follow the state machine, keep their history,
/// free the old slot in the same transaction (the MDV-P1-022 follow-up) and never double-book, also under concurrency.
/// </summary>
public sealed class AppointmentChangesTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    const int Desks = 100;
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    IBookingService Desk => h.Booking(h.Reception);

    static RescheduleAppointment To(Guid calendar, DateTimeOffset start) => new() { CalendarId = calendar, Start = start };

    static CancelAppointment Because(CancelReason reason, string? note = null) => new() { Reason = reason, Note = note };

    async Task<int> SlotRowsAsync(Guid appointmentId)
    {
        await using var db = sql.Scheduling(h.Tenant);
        return await db.ScheduleSlots.CountAsync(s => s.AppointmentId == appointmentId);
    }

    async Task<OutboxEvent> LastOutboxAsync(Guid appointmentId)
    {
        await using var db = sql.Scheduling(h.Tenant);
        return (await db.Outbox.Where(e => e.AggregateId == appointmentId).ToListAsync()).OrderBy(e => e.Id).Last(); // version 7 ids sort by time
    }

    static async Task<string> TransitionRefused(Func<Task> change) => (await Assert.ThrowsAsync<VisitTransitionException>(change)).Code;

    [Fact]
    public async Task CancellingFreesTheSlotForTheNextBookingAndKeepsTheReason()
    {
        var visit = await h.BookAsync(10);
        var cancelled = await Desk.CancelAsync(visit.Id, Because(CancelReason.Other, "Synthetic: travelling that week"));

        Assert.Equal(AppointmentState.Cancelled, cancelled.State);
        Assert.Equal(0, await SlotRowsAsync(visit.Id));
        Assert.Equal(At(10), (await h.BookAsync(10, patient: h.SecondPatient)).Start); // the same slot books again

        await using (var db = sql.Scheduling(h.Tenant))
        {
            var evt = await db.StatusEvents.SingleAsync(e => e.AppointmentId == visit.Id && e.State == AppointmentState.Cancelled);
            Assert.Equal((CancelReason.Other, "Synthetic: travelling that week", h.Reception), (evt.CancelReason!.Value, evt.Note, evt.ActorUserId!.Value));
        }
        var outbox = await LastOutboxAsync(visit.Id);
        Assert.Equal("AppointmentCancelled.v1", outbox.Type);
        Assert.DoesNotContain("travelling", outbox.Payload, StringComparison.Ordinal); // the note never leaves the database
        Assert.DoesNotContain("Meera", outbox.Payload, StringComparison.Ordinal);

        var column = (await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).Columns.Single(c => c.CalendarId == h.Doctor);
        Assert.Contains(column.Appointments, a => a.Id == visit.Id && a.State == AppointmentState.Cancelled); // still shown, as cancelled
    }

    [Fact]
    public async Task CancelNeedsAReasonAndOnlyHappensBeforeTheClinicalPartOfTheVisit()
    {
        var visit = await h.BookAsync(10);
        var seen = await h.BookAsync(11);
        async Task<IReadOnlyList<ValidationError>> Invalid(CancelAppointment c) => (await Assert.ThrowsAsync<SchedulingValidationException>(() => Desk.CancelAsync(visit.Id, c))).Errors;
        Assert.Equal([new ValidationError("reason", "required")], await Invalid(new CancelAppointment()));
        Assert.Equal([new ValidationError("note", "required")], await Invalid(Because(CancelReason.Other, "  ")));
        Assert.Equal([new ValidationError("note", "too_long")], await Invalid(Because(CancelReason.PatientRequest, new string('x', 501))));
        Assert.Equal(4, await SlotRowsAsync(visit.Id)); // nothing changed

        // Checked in, the patient may still leave before being seen; once with the nurse the visit is finished on the board.
        h.Clock.Advance(At(9, 50) - MondayMorning);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        Assert.Equal(AppointmentState.Cancelled, (await Desk.CancelAsync(visit.Id, Because(CancelReason.PatientUnwell))).State);
        Assert.Equal("invalid_transition", await TransitionRefused(() => Desk.CancelAsync(visit.Id, Because(CancelReason.PatientRequest))));

        await h.FrontDesk(h.Reception).CheckInAsync(seen.Id, new CheckIn());
        await h.FrontDesk(h.Reception).MoveAsync(seen.Id, new MoveVisit { State = AppointmentState.WithNurse });
        Assert.Equal("invalid_transition", await TransitionRefused(() => Desk.CancelAsync(seen.Id, Because(CancelReason.PatientRequest))));
        Assert.Equal((4, 0), (await SlotRowsAsync(seen.Id), await SlotRowsAsync(visit.Id)));
    }

    [Fact]
    public async Task ANoShowIsOnlyABookedVisitThatHasStartedAndItFreesTheSlot()
    {
        var visit = await h.BookAsync(10);
        var arrived = await h.BookAsync(9, 40);
        Assert.Equal("not_started", await TransitionRefused(() => Desk.MarkNoShowAsync(visit.Id)));

        h.Clock.Advance(At(10, 5) - MondayMorning);
        await h.FrontDesk(h.Reception).CheckInAsync(arrived.Id, new CheckIn());
        Assert.Equal("invalid_transition", await TransitionRefused(() => Desk.MarkNoShowAsync(arrived.Id))); // checked in: not a no-show

        Assert.Equal(AppointmentState.NoShow, (await Desk.MarkNoShowAsync(visit.Id)).State);
        Assert.Equal(0, await SlotRowsAsync(visit.Id));
        Assert.Equal("AppointmentNoShow.v1", (await LastOutboxAsync(visit.Id)).Type); // with the status event, what the no-show metric (MDV-P1-041) counts
        await using (var db = sql.Scheduling(h.Tenant))
            Assert.Equal(h.Reception, (await db.StatusEvents.SingleAsync(e => e.AppointmentId == visit.Id && e.State == AppointmentState.NoShow)).ActorUserId);
        Assert.Equal("invalid_transition", await TransitionRefused(() => Desk.MarkNoShowAsync(visit.Id)));
        Assert.Equal("invalid_transition", await TransitionRefused(() => Desk.CancelAsync(visit.Id, Because(CancelReason.PatientRequest))));
        Assert.Equal("invalid_transition", await TransitionRefused(() => Desk.RescheduleAsync(visit.Id, To(h.Doctor, At(17)))));
    }

    [Fact]
    public async Task ReschedulingMovesTheVisitFreesTheOldSlotAndKeepsHistory()
    {
        var visit = await h.BookAsync(10);
        var moved = await Desk.RescheduleAsync(visit.Id, To(h.Doctor, At(11, 20)));

        Assert.Equal((At(11, 20), At(11, 40), AppointmentState.Booked, visit.Reference), (moved.Start, moved.End, moved.State, moved.Reference));
        await using (var db = sql.Scheduling(h.Tenant))
        {
            var slots = await db.ScheduleSlots.Where(s => s.AppointmentId == visit.Id).Select(s => s.SlotStart).OrderBy(s => s).ToListAsync();
            Assert.Equal([At(11, 20), At(11, 25), At(11, 30), At(11, 35)], slots);
            var history = await db.Reschedules.SingleAsync(r => r.AppointmentId == visit.Id);
            Assert.Equal((h.Doctor, At(10), At(10, 20), h.Doctor, At(11, 20), At(11, 40), h.Reception, MondayMorning),
                (history.FromCalendarId, history.FromStart, history.FromEnd, history.ToCalendarId, history.ToStart, history.ToEnd, history.ActorUserId!.Value, history.OccurredAt));
        }
        var outbox = await LastOutboxAsync(visit.Id);
        Assert.Equal("AppointmentRescheduled.v1", outbox.Type);
        Assert.Equal(At(10), JsonDocument.Parse(outbox.Payload).RootElement.GetProperty("fromStart").GetDateTimeOffset());

        Assert.Equal(At(10), (await h.BookAsync(10, patient: h.SecondPatient)).Start); // the old slot is free again
        Assert.Equal("slot_unavailable", (await Assert.ThrowsAsync<SlotUnavailableException>(() => h.BookAsync(11, 20, patient: h.SecondPatient))).Code); // the new one is taken
    }

    [Fact]
    public async Task ALongVisitCanMoveOntoPartOfItsOwnTimeAndToAnotherDoctor()
    {
        var hold = await Desk.HoldAsync(new HoldSlot { FacilityId = h.Facility, CalendarId = h.Doctor, Start = At(10), VisitType = VisitType.NewConsultation });
        var visit = await Desk.BookAsync(new BookAppointment { HoldId = hold.Id, PatientId = h.Patient });
        var later = await Desk.RescheduleAsync(visit.Id, To(h.Doctor, At(10, 20))); // 10:00–10:40 → 10:20–11:00, overlapping itself
        Assert.Equal((At(10, 20), At(11)), (later.Start, later.End));

        await h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate() with { CalendarId = h.SecondDoctor });
        var other = await Desk.RescheduleAsync(visit.Id, To(h.SecondDoctor, At(17)));
        Assert.Equal((At(17), At(17, 40)), (other.Start, other.End));
        var day = await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday);
        Assert.Contains(day.Columns.Single(c => c.CalendarId == h.SecondDoctor).Appointments, a => a.Id == visit.Id);
        Assert.DoesNotContain(day.Columns.Single(c => c.CalendarId == h.Doctor).Appointments, a => a.Id == visit.Id);
        Assert.Equal(8, await SlotRowsAsync(visit.Id));
    }

    [Fact]
    public async Task OnlyABookedVisitMovesAndOnlyToAFreeBookableFutureSlot()
    {
        var visit = await h.BookAsync(10);
        var taken = await h.BookAsync(11, patient: h.SecondPatient);
        async Task<string> Refused(RescheduleAppointment to) => (await Assert.ThrowsAsync<SlotUnavailableException>(() => Desk.RescheduleAsync(visit.Id, to))).Code;

        Assert.Equal("slot_unavailable", await Refused(To(h.Doctor, At(11))));
        Assert.Equal("not_bookable", await Refused(To(h.Doctor, At(12, 20)))); // lunch
        Assert.Equal("not_bookable", await Refused(To(h.Doctor, At(10, 10)))); // not a slot start
        Assert.Equal("not_bookable", await Refused(To(h.SecondDoctor, At(10)))); // no template: not working
        var room = await h.Schedule(h.Admin).AddResourceAsync(new AddResource { FacilityId = h.Facility, Name = "Procedure room (synthetic)" });
        await h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate() with { CalendarId = room.Id, CalendarKind = CalendarKind.Resource });
        Assert.Equal("not_bookable", await Refused(To(room.Id, At(10)))); // a doctor's visit does not move into a room's calendar
        Assert.Equal("in_past", await Refused(To(h.Doctor, MondayMorning.AddMinutes(-20))));
        var otherDesk = (await h.AddReceptionistsAsync(1))[0];
        await h.Booking(otherDesk).HoldAsync(new HoldSlot { FacilityId = h.Facility, CalendarId = h.Doctor, Start = At(11, 40), VisitType = VisitType.FollowUp });
        Assert.Equal("being_booked", await Refused(To(h.Doctor, At(11, 40))));
        Assert.Equal([new ValidationError("start", "unchanged")],
            (await Assert.ThrowsAsync<SchedulingValidationException>(() => Desk.RescheduleAsync(visit.Id, To(h.Doctor, At(10))))).Errors);
        Assert.Equal((4, At(10)), (await SlotRowsAsync(visit.Id),
            (await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).Columns.Single(c => c.CalendarId == h.Doctor).Appointments.Single(a => a.Id == visit.Id).Start));

        h.Clock.Advance(At(10, 50) - MondayMorning);
        await h.FrontDesk(h.Reception).CheckInAsync(taken.Id, new CheckIn());
        Assert.Equal("invalid_transition", await TransitionRefused(() => Desk.RescheduleAsync(taken.Id, To(h.Doctor, At(17)))));
    }

    [Fact]
    public async Task OnlyStaffWhoMayBookAtTheFacilityChangeABooking()
    {
        var visit = await h.BookAsync(10);
        foreach (var user in new[] { h.Nurse, h.PortalUser, h.Elsewhere })
        {
            var booking = h.Booking(user);
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => booking.RescheduleAsync(visit.Id, To(h.Doctor, At(11))));
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => booking.CancelAsync(visit.Id, Because(CancelReason.PatientRequest)));
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => booking.MarkNoShowAsync(visit.Id));
        }
        await Assert.ThrowsAsync<AppointmentNotFoundException>(() => h.Booking(h.Reception, Guid.NewGuid()).CancelAsync(visit.Id, Because(CancelReason.PatientRequest)));
        Assert.Equal(4, await SlotRowsAsync(visit.Id));
    }

    /// <summary>Appointments written straight to the table at distinct times from Tuesday on, each with its slot rows, as if booked earlier.</summary>
    async Task<List<Guid>> SeedVisitsAsync(int count)
    {
        await using var db = sql.Scheduling(h.Tenant);
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var slot = i % 20; // twenty a day: 09:00–12:40 and 17:00–19:20
            var start = At(9).AddDays(1 + i / 20).AddMinutes(20 * (slot < 12 ? slot : slot + 12));
            var visit = new Appointment
            {
                Id = Guid.CreateVersion7(), TenantId = h.Tenant, FacilityId = h.Facility, CalendarId = h.Doctor, CalendarKind = CalendarKind.Doctor, PatientId = h.Patient,
                VisitType = VisitType.FollowUp, Start = start, End = start.AddMinutes(20), State = AppointmentState.Booked, BookedAt = MondayMorning, ChangedAt = MondayMorning,
            };
            db.Appointments.Add(visit);
            db.ScheduleSlots.AddRange(ScheduleSlot.For(visit));
            ids.Add(visit.Id);
        }
        await db.SaveChangesAsync();
        return ids;
    }

    /// <summary>Runs every call at the same moment; returns the successes and the 409 codes of the rest.</summary>
    static async Task<(int Ok, List<string> Codes)> Race(IEnumerable<Func<Task>> calls)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = calls.Select(call => Task.Run(async () => { await start.Task; await call(); })).ToList();
        start.SetResult();
        var codes = new List<string>();
        foreach (var t in tasks)
        {
            try { await t; }
            catch (SlotUnavailableException e) { codes.Add(e.Code); }
        }
        return (tasks.Count - codes.Count, codes);
    }

    [Fact]
    public async Task AHundredReschedulesIntoOneSlotAtOnceMoveExactlyOneVisit()
    {
        var visits = await SeedVisitsAsync(Desks);
        var desks = await h.AddReceptionistsAsync(Desks);

        var (ok, codes) = await Race(visits.Zip(desks, (visit, desk) => (Func<Task>)(() => h.Booking(desk).RescheduleAsync(visit, To(h.Doctor, At(10))))));

        Assert.Equal(1, ok);
        Assert.All(codes, c => Assert.Equal("slot_unavailable", c)); // a clear "slot no longer available", never a crash or "busy"
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(1, await db.Appointments.CountAsync(a => a.Start == At(10)));
        Assert.Equal(4, await db.ScheduleSlots.CountAsync(s => s.SlotStart >= At(10) && s.SlotStart < At(10, 20)));
        Assert.Equal(Desks * 4, await db.ScheduleSlots.CountAsync()); // every visit holds exactly its own four units: the winner's old slot is free
        Assert.Equal(1, await db.Reschedules.CountAsync());
    }

    /// <summary>Takes a doctor-day lock on another connection, as a desk in mid-booking of that day would.</summary>
    async Task<(SqlConnection, SqlTransaction)> HoldDoctorDayLockAsync(DateOnly day)
    {
        var conn = new SqlConnection(sql.ConnectionString);
        await conn.OpenAsync();
        var tx = conn.BeginTransaction();
        await using var cmd = new SqlCommand("EXEC sys.sp_getapplock @Resource = @r, @LockMode = 'Exclusive', @LockOwner = 'Transaction';", conn, tx);
        cmd.Parameters.AddWithValue("@r", $"doctor:{h.Doctor}:{day:yyyy-MM-dd}");
        await cmd.ExecuteNonQueryAsync();
        return (conn, tx);
    }

    /// <summary>Runs the change while another desk holds the doctor-day lock of <paramref name="day"/>, and returns its 409 code.</summary>
    async Task<string> WhileLockedAsync(DateOnly day, Func<Task> change)
    {
        h.Options.LockTimeout = TimeSpan.FromMilliseconds(200);
        var (conn, tx) = await HoldDoctorDayLockAsync(day);
        try
        {
            return (await Assert.ThrowsAsync<SlotUnavailableException>(change)).Code;
        }
        finally
        {
            await tx.RollbackAsync();
            await conn.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(0)] // Monday, the visit's own day: its slot is freed
    [InlineData(1)] // Tuesday, the new slot's day: it is taken
    public async Task ARescheduleWaitsForTheDoctorDayLocksOfBothSlots(int lockedDay)
    {
        var visit = await h.BookAsync(10);
        Assert.Equal("busy", await WhileLockedAsync(Monday.AddDays(lockedDay), () => Desk.RescheduleAsync(visit.Id, To(h.Doctor, At(11).AddDays(1)))));
        Assert.Equal(4, await SlotRowsAsync(visit.Id)); // nothing was written
        Assert.Equal(At(10), (await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).Columns.Single(c => c.CalendarId == h.Doctor).Appointments.Single().Start);
    }

    [Fact]
    public async Task CancelAndNoShowWaitForTheVisitsDoctorDayLock()
    {
        var visit = await h.BookAsync(10);
        Assert.Equal("busy", await WhileLockedAsync(Monday, () => Desk.CancelAsync(visit.Id, Because(CancelReason.PatientRequest))));
        h.Clock.Advance(At(10, 5) - MondayMorning);
        Assert.Equal("busy", await WhileLockedAsync(Monday, () => Desk.MarkNoShowAsync(visit.Id)));
        Assert.Equal(4, await SlotRowsAsync(visit.Id)); // still booked, still occupying its slot
    }

    [Fact]
    public async Task ReschedulesAndHoldsRacingForOneSlotLetExactlyOneWin()
    {
        var visits = await SeedVisitsAsync(Desks / 2);
        var desks = await h.AddReceptionistsAsync(Desks);
        var slot = new HoldSlot { FacilityId = h.Facility, CalendarId = h.Doctor, Start = At(10), VisitType = VisitType.FollowUp };

        var calls = visits.Zip(desks, (visit, desk) => (Func<Task>)(() => h.Booking(desk).RescheduleAsync(visit, To(h.Doctor, At(10)))))
            .Concat(desks.Skip(Desks / 2).Select(desk => (Func<Task>)(() => h.Booking(desk).HoldAsync(slot))));
        var (ok, codes) = await Race(calls);

        Assert.Equal(1, ok); // a hold first: every reschedule meets it as "being booked"; a reschedule first: every hold finds the slot taken
        Assert.All(codes, c => Assert.True(c is "slot_unavailable" or "being_booked", c));
        await using var db = sql.Scheduling(h.Tenant);
        var moved = await db.Appointments.CountAsync(a => a.Start == At(10));
        var held = await db.SlotHolds.CountAsync();
        Assert.Equal(1, moved + held);
        Assert.Equal(moved * 4, await db.ScheduleSlots.CountAsync(s => s.SlotStart >= At(10) && s.SlotStart < At(10, 20)));
    }
}

using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-025 on real SQL Server: a walk-in takes the next free slot today through the booking path (doctor-day lock, re-check,
/// SCHEDULE_SLOT rows), or is queued unscheduled without taking a slot; it is checked in with the board's check-in and shows on
/// the board as a walk-in. Dr. Rao works Mon 09:00–12:00 and 17:00–20:00 IST in 20-minute slots. Synthetic data only.
/// </summary>
public sealed class WalkInTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    WalkIn For(Guid? doctor, Guid? patient = null) =>
        new() { FacilityId = h.Facility, CalendarId = doctor, PatientId = patient ?? h.Patient, VisitType = VisitType.FollowUp, ReasonCategory = ReasonCategory.General };

    void At(int hour, int minute) => h.Clock.Advance(SchedulingHarness.At(hour, minute) - h.Clock.GetUtcNow());

    async Task<List<string>> Audit(Guid appointmentId)
    {
        await using var db = sql.Audit(h.Tenant);
        return await db.Events.Where(e => e.ResourceId == appointmentId).OrderBy(e => e.OccurredAt).Select(e => e.Action + ":" + e.Outcome).ToListAsync();
    }

    [Fact]
    public async Task AWalkInTakesTheNextFreeSlotAndIsCheckedInAsAWalkIn()
    {
        At(10, 5);
        await h.BookAsync(10, 20, h.SecondPatient); // the next slot is taken: the walk-in gets 10:40
        var walkIn = await h.Booking(h.Reception).WalkInAsync(For(doctor: null));
        Assert.Equal((SchedulingHarness.At(10, 40), SchedulingHarness.At(11, 0), h.Doctor, true, false, AppointmentState.Booked),
            (walkIn.Start, walkIn.End, walkIn.CalendarId, walkIn.WalkIn, walkIn.Unscheduled, walkIn.State));

        await using (var db = sql.Scheduling(h.Tenant))
            Assert.Equal(4, await db.ScheduleSlots.CountAsync(s => s.AppointmentId == walkIn.Id)); // the slot is taken like any booking

        // Checked in with the board's own check-in (the same state machine), then on the board's Checked in column, tagged.
        var card = await h.FrontDesk(h.Reception).CheckInAsync(walkIn.Id, new CheckIn());
        Assert.Equal((AppointmentState.CheckedIn, true), (card.State, card.WalkIn));
        var board = await h.FrontDesk(h.Reception).GetBoardAsync(h.Facility);
        Assert.True(board.Cards.Single(c => c.Id == walkIn.Id).WalkIn);

        // Each write audited once, in its own transaction (same instant on the test clock, so compared as a set).
        Assert.Equal(["appointment.check_in:Allowed", "appointment.walk_in:Allowed"], (await Audit(walkIn.Id)).Order());
    }

    /// <summary>The visit type decides the length, re-checked against the template under the lock: a new consultation takes two slots.</summary>
    [Fact]
    public async Task ALongerVisitTypeTakesTheNextRunOfFreeSlots()
    {
        At(10, 5);
        await h.BookAsync(11, 0, h.SecondPatient); // 10:20 and 10:40 are free back to back; 11:00 is taken
        var walkIn = await h.Booking(h.Reception).WalkInAsync(For(h.Doctor) with { VisitType = VisitType.NewConsultation });
        Assert.Equal((SchedulingHarness.At(10, 20), SchedulingHarness.At(11, 0)), (walkIn.Start, walkIn.End));
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(8, await db.ScheduleSlots.CountAsync(s => s.AppointmentId == walkIn.Id));
    }

    [Fact]
    public async Task WithNoFreeSlotTheWalkInIsQueuedWithoutTakingASlot()
    {
        At(19, 45); // the last slot (19:40) has started: nothing left today
        var queued = await h.Booking(h.Reception).WalkInAsync(For(h.Doctor));
        Assert.Equal((true, true, h.Clock.GetUtcNow(), h.Clock.GetUtcNow()), (queued.WalkIn, queued.Unscheduled, queued.Start, queued.End));
        await using (var db = sql.Scheduling(h.Tenant))
            Assert.False(await db.ScheduleSlots.AnyAsync(s => s.AppointmentId == queued.Id));

        // Not drawn on the schedule (it holds no time), but waiting on the board once checked in.
        var column = (await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).Columns.Single(c => c.CalendarId == h.Doctor);
        Assert.DoesNotContain(column.Appointments, a => a.Id == queued.Id);
        var card = await h.FrontDesk(h.Reception).CheckInAsync(queued.Id, new CheckIn());
        Assert.Equal((AppointmentState.CheckedIn, true, true), (card.State, card.WalkIn, card.Unscheduled));
    }

    /// <summary>
    /// #912: a just-registered patient's first walk-in, "Next available doctor" (the screen's default) after the last slot of the
    /// day, was refused 409 no_free_slot on Azure dev. It is queued like a chosen doctor's walk-in, under the only doctor working today.
    /// </summary>
    [Fact]
    public async Task NextAvailableWithNoFreeSlotIsQueuedUnderTheDoctorWorkingToday()
    {
        At(19, 45);
        var queued = await h.Booking(h.Reception).WalkInAsync(For(doctor: null) with { VisitType = VisitType.NewConsultation });
        Assert.Equal((h.Doctor, true, true), (queued.CalendarId, queued.WalkIn, queued.Unscheduled));
        Assert.Equal(AppointmentState.CheckedIn, (await h.FrontDesk(h.Reception).CheckInAsync(queued.Id, new CheckIn())).State);
    }

    /// <summary>With several doctors working today and none free, the one with the fewest open visits today takes the walk-in.</summary>
    [Fact]
    public async Task NextAvailableWithNoFreeSlotGoesToTheLeastBusyDoctor()
    {
        await h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate() with { CalendarId = h.SecondDoctor });
        At(19, 5);
        await h.BookAsync(19, 20, h.SecondPatient); // Dr. Rao: two open visits today; Dr. Menon: one
        await h.BookAsync(19, 40, h.SecondPatient);
        await h.Booking(h.Reception).WalkInAsync(For(h.SecondDoctor));
        At(19, 45);
        var queued = await h.Booking(h.Reception).WalkInAsync(For(doctor: null) with { AllowRepeat = true });
        Assert.Equal((h.SecondDoctor, true), (queued.CalendarId, queued.Unscheduled));
    }

    /// <summary>Nobody works today (Sunday): there is no doctor to queue with, so the desk is still asked to choose one.</summary>
    [Fact]
    public async Task NextAvailableOnADayNobodyWorksStillAsksForADoctor()
    {
        h.Clock.Advance(SchedulingHarness.At(10, 0).AddDays(6) - h.Clock.GetUtcNow());
        Assert.Equal("no_free_slot", (await Assert.ThrowsAsync<SlotUnavailableException>(() => h.Booking(h.Reception).WalkInAsync(For(doctor: null)))).Code);
    }

    /// <summary>No double booking (MDV-P1-022) holds for walk-ins: six desks at once for two free slots get two distinct slots and four queued.</summary>
    [Fact]
    public async Task ParallelWalkInsNeverShareASlot()
    {
        At(19, 5); // free today: 19:20 and 19:40
        var desks = await h.AddReceptionistsAsync(6);
        // One synthetic patient for all six: the desks confirmed the repeat, so only the slot rule is under test here.
        var results = await Task.WhenAll(desks.Select(d => h.Booking(d).WalkInAsync(For(h.Doctor) with { AllowRepeat = true })));

        var slotted = results.Where(r => !r.Unscheduled).ToList();
        Assert.Equal([SchedulingHarness.At(19, 20), SchedulingHarness.At(19, 40)], slotted.Select(r => r.Start).Order());
        Assert.Equal(4, results.Count(r => r.Unscheduled));
        await using var db = sql.Scheduling(h.Tenant);
        var units = await db.ScheduleSlots.Where(s => s.CalendarId == h.Doctor && s.SlotStart >= SchedulingHarness.At(19, 20)).Select(s => s.SlotStart).ToListAsync();
        Assert.Equal(8, units.Count);
        Assert.Equal(units.Count, units.Distinct().Count());
    }

    /// <summary>MDV-P1-052: a patient with an open visit today is not checked in again by accident; the desk must confirm.</summary>
    [Fact]
    public async Task ASecondWalkInTheSameDayIsRefusedUntilTheDeskConfirmsIt()
    {
        At(10, 5);
        var first = await h.Booking(h.Reception).WalkInAsync(For(h.Doctor));
        await h.FrontDesk(h.Reception).CheckInAsync(first.Id, new CheckIn());
        Assert.Equal("already_today", (await Assert.ThrowsAsync<SlotUnavailableException>(() => h.Booking(h.Reception).WalkInAsync(For(h.Doctor)))).Code);
        await using (var db = sql.Scheduling(h.Tenant))
            Assert.Equal(1, await db.Appointments.CountAsync(a => a.PatientId == h.Patient)); // nothing made by the refused attempt

        // A booked visit later today counts too: the desk should check that one in instead.
        await h.BookAsync(17, 0, h.SecondPatient);
        Assert.Equal("already_today", (await Assert.ThrowsAsync<SlotUnavailableException>(() => h.Booking(h.Reception).WalkInAsync(For(null, h.SecondPatient)))).Code);

        var second = await h.Booking(h.Reception).WalkInAsync(For(h.Doctor) with { AllowRepeat = true });
        Assert.NotEqual(first.Id, second.Id);

        // A finished visit does not count: the patient may come back the same day.
        foreach (var state in new[] { AppointmentState.WithNurse, AppointmentState.ReadyForDoctor, AppointmentState.InConsult, AppointmentState.Billing, AppointmentState.Done })
            await h.FrontDesk(h.Reception).MoveAsync(first.Id, new MoveVisit { State = state });
        await h.Booking(h.Reception).CancelAsync(second.Id, new CancelAppointment { Reason = CancelReason.BookedInError });
        Assert.True((await h.Booking(h.Reception).WalkInAsync(For(h.Doctor))).WalkIn);
    }

    /// <summary>MDV-P1-052: a walk-in queued "now" with no slot waits behind patients who checked in earlier for later slots.</summary>
    [Fact]
    public async Task NextPatientsFollowTheTokenOrderNotTheSlotTime()
    {
        At(19, 25); // one slot left today: 19:40
        var early = await h.Booking(h.Reception).WalkInAsync(For(h.Doctor));
        await h.FrontDesk(h.Reception).CheckInAsync(early.Id, new CheckIn());
        At(19, 30);
        var queued = await h.Booking(h.Reception).WalkInAsync(For(h.Doctor, h.SecondPatient));
        Assert.True(queued.Unscheduled && queued.Start < early.Start); // stamped 19:30, before the 19:40 slot
        await h.FrontDesk(h.Reception).CheckInAsync(queued.Id, new CheckIn());

        var doctor = (await h.FrontDesk(h.Reception).GetOverviewAsync(h.Facility)).Doctors.Single(d => d.CalendarId == h.Doctor);
        Assert.Equal([early.Id, queued.Id], doctor.Next.Select(n => n.Id));
        Assert.Equal(["A-01", "A-02"], doctor.Next.Select(n => n.Token));
    }

    [Fact]
    public async Task OnlyStaffWhoMayBookAndOnlyThisFacilitysPatientsAndDoctors()
    {
        At(10, 5);
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Booking(h.Nurse).WalkInAsync(For(null)));
        Assert.Equal("unknown", Assert.Single((await Assert.ThrowsAsync<SchedulingValidationException>(() =>
            h.Booking(h.Reception).WalkInAsync(For(null, h.OtherFacilityPatient)))).Errors).Code);
        Assert.Equal("calendarId", Assert.Single((await Assert.ThrowsAsync<SchedulingValidationException>(() =>
            h.Booking(h.Reception).WalkInAsync(For(Guid.NewGuid())))).Errors).Field);
    }
}

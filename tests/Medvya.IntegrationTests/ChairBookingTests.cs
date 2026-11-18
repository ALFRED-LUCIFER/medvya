using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P8-009 on real PostgreSQL: a dental chair is a resource with its own hours; a booking holds a dentist and a chair, a procedure sets its length
/// (rounded up to the dentist's slots), and neither the dentist nor the chair can be double-booked: by the service, by the chair's day lock under
/// concurrency, and by the SCHEDULE_SLOT unique index on its own.
/// </summary>
public sealed class ChairBookingTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    Guid chair;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate() with { CalendarId = h.SecondDoctor });
        chair = (await h.Schedule(h.Admin).AddResourceAsync(new AddResource { FacilityId = h.Facility, Name = "Chair 1 (synthetic)" })).Id;
        // The chair is open Monday to Friday 09:00 to 12:00 only.
        await h.Schedule(h.Admin).SaveTemplateAsync(new SaveTemplate
        {
            FacilityId = h.Facility, CalendarId = chair, CalendarKind = CalendarKind.Resource, SlotMinutes = 15,
            Hours = [.. Enum.GetValues<DayOfWeek>().Where(d => d is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Select(d => new WorkingHours { Weekday = d, Start = new(9, 0), End = new(12, 0) })],
        });
    }

    public Task DisposeAsync() => h.CloseAsync();

    HoldSlot Slot(Guid doctor, int hour, int minute = 0, int? minutes = 40, Guid? resource = null) =>
        new() { FacilityId = h.Facility, CalendarId = doctor, Start = At(hour, minute), VisitType = VisitType.FollowUp, ResourceId = resource ?? chair, Minutes = minutes };

    async Task<AppointmentView> BookAsync(HoldSlot slot, Guid? desk = null)
    {
        var booking = h.Booking(desk ?? h.Reception);
        var hold = await booking.HoldAsync(slot);
        return await booking.BookAsync(new BookAppointment { HoldId = hold.Id, PatientId = h.Patient, ReasonCategory = ReasonCategory.Other, Channel = BookingChannel.Phone });
    }

    static async Task<string> Unavailable(Task task) => (await Assert.ThrowsAsync<SlotUnavailableException>(() => task)).Code;

    [Fact]
    public async Task ADentistAndAChairAreBookedTogetherForTheProceduresLength()
    {
        var hold = await h.Booking(h.Reception).HoldAsync(Slot(h.Doctor, 10, minutes: 45));
        Assert.Equal((At(11), (Guid?)chair), (hold.End, hold.ResourceId)); // 45 minutes on 20-minute slots: three slots, an hour
        await h.Booking(h.Reception).ReleaseHoldAsync(hold.Id);

        var visit = await BookAsync(Slot(h.Doctor, 10, minutes: 45));
        await using (var db = sql.Scheduling(h.Tenant))
        {
            Assert.Equal(chair, (await db.Appointments.SingleAsync(a => a.Id == visit.Id)).ResourceId);
            Assert.Equal(12, await db.ScheduleSlots.CountAsync(s => s.AppointmentId == visit.Id && s.CalendarId == h.Doctor));
            Assert.Equal(12, await db.ScheduleSlots.CountAsync(s => s.AppointmentId == visit.Id && s.CalendarId == chair));
        }

        // The day can be read by dentist or by chair: the visit is in both columns.
        var day = await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday);
        Assert.Contains(day.Columns.Single(c => c.CalendarId == chair).Appointments, a => a.Id == visit.Id && a.CalendarId == h.Doctor && a.ResourceId == chair);
        Assert.Contains(day.Columns.Single(c => c.CalendarId == h.Doctor).Appointments, a => a.Id == visit.Id);
        Assert.DoesNotContain(day.Columns.Single(c => c.CalendarId == chair).Slots, s => s.Start < At(11) && At(10) < s.End); // no free chair slot then
    }

    /// <summary>SAFETY (mutation check: drop the chair from ScheduleSlot.For, or the chair check from HoldAsync, and this fails).</summary>
    [Fact]
    public async Task TheChairCannotBeDoubleBookedEvenWithAnotherDentistOrOutsideItsHours()
    {
        await BookAsync(Slot(h.Doctor, 10)); // 10:00 to 10:40
        Assert.Equal("chair_unavailable", await Unavailable(h.Booking(h.Reception).HoldAsync(Slot(h.SecondDoctor, 10, 20))));
        await BookAsync(Slot(h.SecondDoctor, 10, 40)); // the chair is free again at 10:40
        Assert.Equal("chair_unavailable", await Unavailable(h.Booking(h.Reception).HoldAsync(Slot(h.SecondDoctor, 17)))); // the dentist works, the chair is closed
        Assert.Equal("chair_unavailable", await Unavailable(h.Booking(h.Reception).HoldAsync(Slot(h.SecondDoctor, 9, 0, resource: h.Doctor)))); // a doctor is not a chair
        await Assert.ThrowsAsync<SchedulingValidationException>(() => h.Booking(h.Reception).HoldAsync(Slot(h.Doctor, 9, minutes: 3)));
        await Assert.ThrowsAsync<SchedulingValidationException>(() => h.Booking(h.Reception).HoldAsync(Slot(h.Doctor, 9, resource: h.Doctor)));
    }

    /// <summary>SAFETY (mutation check: write the chair's slot rows without the unique index, and this fails).</summary>
    [Fact]
    public async Task TheDatabaseRefusesASecondVisitInTheChairEvenWithoutTheService()
    {
        var first = await BookAsync(Slot(h.Doctor, 10));
        await using var db = sql.Scheduling(h.Tenant);
        db.ScheduleSlots.Add(new ScheduleSlot { Id = Guid.NewGuid(), TenantId = h.Tenant, FacilityId = h.Facility, CalendarId = chair, SlotStart = At(10, 15), AppointmentId = Guid.NewGuid() });
        var e = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, (e.InnerException as PostgresException)?.SqlState);
        Assert.NotEqual(Guid.Empty, first.Id);
    }

    /// <summary>
    /// Two dentists' desks race for the same chair at the same time: exactly one visit, the rest a clear "chair unavailable" (the chair's day lock and the
    /// index), never "busy". SAFETY (mutation check: leave the chair out of the transaction's locks and the index still refuses; drop both and this fails).
    /// </summary>
    [Fact]
    public async Task DesksOfTwoDentistsRacingForOneChairGiveExactlyOneVisit()
    {
        var desks = await h.AddReceptionistsAsync(40);
        await using (var db = sql.Scheduling(h.Tenant))
        {
            db.SlotHolds.AddRange(desks.Select((d, i) => new SlotHold
            {
                Id = Guid.NewGuid(), TenantId = h.Tenant, FacilityId = h.Facility, CalendarId = i % 2 == 0 ? h.Doctor : h.SecondDoctor, VisitType = VisitType.FollowUp,
                Start = At(10), End = At(10, 40), HeldBy = d, ExpiresAt = MondayMorning.AddMinutes(10), ResourceId = chair,
            }));
            await db.SaveChangesAsync();
        }
        List<SlotHold> holds;
        await using (var db = sql.Scheduling(h.Tenant)) holds = await db.SlotHolds.AsNoTracking().ToListAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = holds.Select(x => Task.Run(async () =>
        {
            await start.Task;
            await h.Booking(x.HeldBy).BookAsync(new BookAppointment { HoldId = x.Id, PatientId = h.Patient });
        })).ToList();
        start.SetResult();
        var codes = new List<string>();
        foreach (var t in tasks)
        {
            try { await t; }
            catch (SlotUnavailableException e) { codes.Add(e.Code); }
        }
        Assert.Equal(desks.Count - 1, codes.Count);
        Assert.All(codes, c => Assert.True(c is "chair_unavailable" or "slot_unavailable", c));
        await using var check = sql.Scheduling(h.Tenant);
        Assert.Equal(1, await check.Appointments.CountAsync(a => a.ResourceId == chair));
    }

    [Fact]
    public async Task AVisitMovedToAnotherTimeTakesItsChairAndLengthWithIt()
    {
        var visit = await BookAsync(Slot(h.Doctor, 9, minutes: 60));
        await BookAsync(Slot(h.SecondDoctor, 11));
        Assert.Equal("chair_unavailable", await Unavailable(h.Booking(h.Reception).RescheduleAsync(visit.Id, new RescheduleAppointment { CalendarId = h.Doctor, Start = At(10, 40) })));
        var moved = await h.Booking(h.Reception).RescheduleAsync(visit.Id, new RescheduleAppointment { CalendarId = h.Doctor, Start = At(10) });
        Assert.Equal((At(10), At(11)), (moved.Start, moved.End));
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(At(10), await db.ScheduleSlots.Where(s => s.AppointmentId == visit.Id && s.CalendarId == chair).MinAsync(s => s.SlotStart));
    }

    [Fact]
    public async Task TheBookingFormListsProceduresWithTheirLengthAndNoPrice()
    {
        var catalogue = h.Get<ICatalogue>(h.Admin);
        await catalogue.CreateAsync(new SaveService { Name = "Root canal, molar (synthetic)", Code = "DN-T1", Category = ServiceCategory.Procedure, Sac = "999312", DurationMinutes = 60 });
        await catalogue.CreateAsync(new SaveService { Name = "Certificate (synthetic)", Category = ServiceCategory.Other });
        var procedures = await h.Booking(h.Reception).ProceduresAsync(h.Facility);
        Assert.Equal([("Root canal, molar (synthetic)", 60)], procedures.Select(p => (p.Name, p.DurationMinutes)));
    }
}

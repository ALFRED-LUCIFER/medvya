using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-023 on real SQL Server: the week view of one calendar shows exactly what the day views show (appointments, free and
/// held slots, capacity), days are the facility's local days, and only a calendar of the facility has a week.
/// </summary>
public sealed class ScheduleViewsTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        // Dr. Rao's usual hours plus a night session from 00:00 to 01:00 IST on Tuesday, which is still Monday in UTC.
        var template = h.DoctorTemplate();
        await h.Schedule(h.Admin).SaveTemplateAsync(template with
        {
            Hours = [.. template.Hours, new WorkingHours { Weekday = DayOfWeek.Tuesday, Start = new(0, 0), End = new(1, 0) }],
        });
    }

    public Task DisposeAsync() => h.CloseAsync();

    async Task BookAsync(DateTimeOffset start, VisitType type = VisitType.FollowUp)
    {
        var hold = await h.Booking(h.Reception).HoldAsync(new HoldSlot { FacilityId = h.Facility, CalendarId = h.Doctor, Start = start, VisitType = type });
        await h.Booking(h.Reception).BookAsync(new BookAppointment { HoldId = hold.Id, PatientId = h.Patient });
    }

    [Fact]
    public async Task TheWeekShowsTheSameAppointmentsSlotsAndCapacityAsEachDay()
    {
        await BookAsync(At(9));
        await BookAsync(At(17), VisitType.NewConsultation); // two slots
        await BookAsync(At(10).AddDays(1));
        var otherDesk = (await h.AddReceptionistsAsync(1))[0];
        await h.Booking(otherDesk).HoldAsync(new HoldSlot { FacilityId = h.Facility, CalendarId = h.Doctor, Start = At(11).AddDays(2), VisitType = VisitType.FollowUp });

        var week = await h.Schedule(h.Reception).GetWeekAsync(h.Facility, h.Doctor, Monday.AddDays(2)); // asked on Wednesday
        Assert.Equal([.. Enumerable.Range(0, 7).Select(Monday.AddDays)], week.Days.Select(d => d.Date)); // Monday to Sunday
        Assert.Equal(3, week.Days.Sum(d => Assert.Single(d.Columns).Appointments.Count));
        Assert.Single(week.Days[2].Columns[0].Slots, s => s.Held); // being booked at the other desk

        foreach (var day in week.Days)
        {
            var single = (await h.Schedule(h.Reception).GetDayAsync(h.Facility, day.Date)).Columns.Single(c => c.CalendarId == h.Doctor);
            var inWeek = Assert.Single(day.Columns);
            Assert.Equal(single.Appointments, inWeek.Appointments);
            Assert.Equal(single.Slots, inWeek.Slots);
            Assert.Equal(single.Capacity, inWeek.Capacity);
        }
        // Capacity counts template slots, booked or free: Monday has lunch (18), Tuesday the night session too (21 + 3), Saturday is closed.
        Assert.Equal((18, 24, 21, 0), (week.Days[0].Columns[0].Capacity, week.Days[1].Columns[0].Capacity, week.Days[2].Columns[0].Capacity, week.Days[5].Columns[0].Capacity));
        Assert.Equal(18 - 3, week.Days[0].Columns[0].Slots.Count); // the three booked slots are not free
    }

    [Fact]
    public async Task AVisitJustAfterLocalMidnightBelongsToTheFacilitysDayNotUtcs()
    {
        var night = new DateTimeOffset(2026, 10, 20, 0, 20, 0, TimeSpan.FromHours(5.5)); // Tuesday 00:20 IST = Monday 18:50 UTC
        await BookAsync(night);

        var week = await h.Schedule(h.Reception).GetWeekAsync(h.Facility, h.Doctor, Monday);
        Assert.Empty(week.Days[0].Columns[0].Appointments);
        Assert.Equal(night, Assert.Single(week.Days[1].Columns[0].Appointments).Start);
        Assert.Empty((await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).Columns.Single(c => c.CalendarId == h.Doctor).Appointments);
        Assert.Single((await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday.AddDays(1))).Columns.Single(c => c.CalendarId == h.Doctor).Appointments);
    }

    [Fact]
    public async Task OnlyACalendarOfTheFacilityHasAWeek()
    {
        var notACalendar = await Assert.ThrowsAsync<SchedulingValidationException>(() => h.Schedule(h.Reception).GetWeekAsync(h.Facility, h.Reception, Monday));
        Assert.Equal([new ValidationError("calendarId", "unknown")], notACalendar.Errors);
        // Dr. Rao works at Adyar: Velachery's admin asking for his week at Velachery finds no such calendar, and is refused at Adyar.
        var elsewhere = await Assert.ThrowsAsync<SchedulingValidationException>(() => h.Schedule(h.Elsewhere).GetWeekAsync(h.OtherFacility, h.Doctor, Monday));
        Assert.Equal([new ValidationError("calendarId", "unknown")], elsewhere.Errors);
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Schedule(h.Elsewhere).GetWeekAsync(h.Facility, h.Doctor, Monday));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Schedule(h.PortalUser).GetWeekAsync(h.Facility, h.Doctor, Monday));
    }
}

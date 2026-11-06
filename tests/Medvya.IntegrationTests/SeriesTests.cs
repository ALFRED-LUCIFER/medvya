using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-026 on real SQL Server: a series previews its conflicts, books each occurrence on the booking path as an independent
/// appointment, and "this and following" cancels the rest with one reason and one notification.
/// </summary>
public sealed class SeriesTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    IBookingService Desk => h.Booking(h.Reception);

    SeriesPattern Weekly(int hour, int count = 4) =>
        new() { FacilityId = h.Facility, CalendarId = h.Doctor, FirstStart = At(hour), Count = count };

    BookSeries Confirm(IEnumerable<DateTimeOffset> starts) => new()
    {
        FacilityId = h.Facility, CalendarId = h.Doctor, PatientId = h.Patient, ReasonCategory = ReasonCategory.ChronicReview, Channel = BookingChannel.Phone, Starts = [.. starts],
    };

    async Task<SeriesResult> BookWeeklyAsync(int count) =>
        await Desk.BookSeriesAsync(Confirm((await Desk.PreviewSeriesAsync(Weekly(10, count))).Select(o => o.Start)));

    [Fact]
    public async Task PreviewShowsWhichOccurrencesConflict()
    {
        await h.BookAsync(10, start: At(10).AddDays(14), patient: h.SecondPatient); // 2 Nov 10:00 is taken
        var preview = await Desk.PreviewSeriesAsync(Weekly(10));

        Assert.Equal([null, null, "slot_unavailable", null], preview.Select(o => o.Conflict));
        Assert.Equal([At(10), At(10).AddDays(7), At(10).AddDays(14), At(10).AddDays(21)], preview.Select(o => o.Start));
    }

    [Fact]
    public async Task AMonthlyPreviewKeepsTheDateAndTimeAndIgnoresTheWeekInterval()
    {
        var preview = await Desk.PreviewSeriesAsync(Weekly(10, 3) with { Monthly = true, IntervalWeeks = 99 }); // 99 weeks would be refused if it counted
        Assert.Equal([At(10), At(10).AddMonths(1), At(10).AddMonths(2)], preview.Select(o => o.Start));
        Assert.Null(preview[0].Conflict);
        Assert.Equal([new ValidationError("intervalWeeks", "out_of_range")], (await Assert.ThrowsAsync<SchedulingValidationException>(() => Desk.PreviewSeriesAsync(Weekly(10, 3) with { IntervalWeeks = 99 }))).Errors);
    }

    [Fact]
    public async Task PreviewReportsBreaksAndThePast()
    {
        // Monday lunch 12:00-13:00 is a break; the clock is Monday 08:00 IST so 07:00 is in the past.
        Assert.All(await Desk.PreviewSeriesAsync(Weekly(12, 2)), o => Assert.Equal("not_bookable", o.Conflict));
        Assert.Equal("in_past", (await Desk.PreviewSeriesAsync(Weekly(7, 2)))[0].Conflict);
    }

    [Fact]
    public async Task ConfirmingBooksEachOccurrenceAsItsOwnAppointmentWithOneSeriesId()
    {
        var result = await BookWeeklyAsync(4);

        Assert.All(result.Occurrences, o => Assert.Null(o.Conflict));
        await using var db = sql.Scheduling(h.Tenant);
        var rows = await db.Appointments.Where(a => a.SeriesId == result.SeriesId).ToListAsync();
        var ids = rows.Select(a => a.Id).ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal(4 * 4, await db.ScheduleSlots.CountAsync(s => ids.Contains(s.AppointmentId))); // 20 minutes = four 5-minute units each
        Assert.Equal(4, await db.Outbox.CountAsync(e => e.Type == "AppointmentBooked.v1" && ids.Contains(e.AggregateId)));
    }

    [Fact]
    public async Task SkippingAndMovingAConflictBooksTheRest()
    {
        await h.BookAsync(10, start: At(10).AddDays(7), patient: h.SecondPatient);
        var starts = new[] { At(10), At(10, 20).AddDays(7), At(10).AddDays(14) }; // second occurrence moved 20 minutes later
        var result = await Desk.BookSeriesAsync(Confirm(starts));

        Assert.All(result.Occurrences, o => Assert.Null(o.Conflict));
        Assert.Equal(3, result.Occurrences.Count);
    }

    [Fact]
    public async Task AnOccurrenceTakenAfterThePreviewIsReportedAndTheOthersStayBooked()
    {
        var preview = await Desk.PreviewSeriesAsync(Weekly(10, 3));
        await h.BookAsync(10, start: At(10).AddDays(7), patient: h.SecondPatient); // another desk wins that slot meanwhile
        var result = await Desk.BookSeriesAsync(Confirm(preview.Select(o => o.Start)));

        Assert.Equal([null, "slot_unavailable", null], result.Occurrences.Select(o => o.Conflict));
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(2, await db.Appointments.CountAsync(a => a.SeriesId == result.SeriesId));
    }

    [Fact]
    public async Task ASeriesIsValidatedAndPatientsOfOtherFacilitiesAreRefused()
    {
        async Task<IReadOnlyList<ValidationError>> Invalid(Func<Task> call) => (await Assert.ThrowsAsync<SchedulingValidationException>(call)).Errors;
        Assert.Equal([new ValidationError("count", "out_of_range")], await Invalid(() => Desk.PreviewSeriesAsync(Weekly(10, 1))));
        Assert.Equal([new ValidationError("count", "out_of_range")], await Invalid(() => Desk.PreviewSeriesAsync(Weekly(10, SeriesPattern.MaxCount + 1))));
        Assert.Equal([new ValidationError("starts", "out_of_range")], await Invalid(() => Desk.BookSeriesAsync(Confirm([]))));
        Assert.Equal([new ValidationError("starts", "out_of_range")], await Invalid(() => Desk.BookSeriesAsync(Confirm([At(10), At(10)]))));
        Assert.Equal([new ValidationError("patientId", "unknown")], await Invalid(() => Desk.BookSeriesAsync(Confirm([At(10)]) with { PatientId = h.OtherFacilityPatient })));
    }

    [Fact]
    public async Task OnlyStaffWhoMayBookCanPreviewOrBookASeries()
    {
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Booking(Guid.NewGuid()).PreviewSeriesAsync(Weekly(10)));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Booking(Guid.NewGuid()).BookSeriesAsync(Confirm([At(10)])));
    }

    [Fact]
    public async Task CancellingThisAndFollowingCancelsTheRestWithOneReasonAndOneNotification()
    {
        var series = await BookWeeklyAsync(5);
        var ids = series.Occurrences.Select(o => o.Appointment!.Id).ToList();

        var cancelled = await Desk.CancelFollowingAsync(ids[2], new CancelAppointment { Reason = CancelReason.PatientUnwell });

        Assert.Equal(ids.Skip(2), cancelled.Select(c => c.Id));
        Assert.All(cancelled, c => Assert.Equal(AppointmentState.Cancelled, c.State));
        await using var db = sql.Scheduling(h.Tenant);
        var rows = await db.Appointments.Where(a => a.SeriesId == series.SeriesId).OrderBy(a => a.Start).ToListAsync();
        Assert.Equal([AppointmentState.Booked, AppointmentState.Booked, AppointmentState.Cancelled, AppointmentState.Cancelled, AppointmentState.Cancelled], rows.Select(a => a.State));
        Assert.Equal(2 * 4, await db.ScheduleSlots.CountAsync(s => ids.Contains(s.AppointmentId))); // the cancelled occurrences freed their slots
        Assert.Equal(3, await db.StatusEvents.CountAsync(e => ids.Contains(e.AppointmentId) && e.State == AppointmentState.Cancelled && e.CancelReason == CancelReason.PatientUnwell));

        var events = (await db.Outbox.Where(e => e.Type == "AppointmentCancelled.v1" && ids.Contains(e.AggregateId)).ToListAsync()).OrderBy(e => e.Id).ToList();
        Assert.Equal([false, true, true], events.Select(e => e.Payload.Contains("\"quiet\":true", StringComparison.Ordinal))); // only the first tells the patient
        Assert.Equal(ids[2], events[0].AggregateId);
    }

    [Fact]
    public async Task AnOccurrenceAlreadyCancelledIsLeftOutOfTheFollowingCancel()
    {
        var ids = (await BookWeeklyAsync(3)).Occurrences.Select(o => o.Appointment!.Id).ToList();
        await Desk.CancelAsync(ids[1], new CancelAppointment { Reason = CancelReason.PatientRequest });

        var cancelled = await Desk.CancelFollowingAsync(ids[0], new CancelAppointment { Reason = CancelReason.ClinicClosed });

        Assert.Equal([ids[0], ids[2]], cancelled.Select(c => c.Id));
    }

    [Fact]
    public async Task CancelFollowingNeedsASeriesAndAReason()
    {
        var single = await h.BookAsync(10);
        Assert.Equal([new ValidationError("seriesId", "not_in_series")],
            (await Assert.ThrowsAsync<SchedulingValidationException>(() => Desk.CancelFollowingAsync(single.Id, new CancelAppointment { Reason = CancelReason.PatientRequest }))).Errors);
        Assert.Equal([new ValidationError("reason", "required")],
            (await Assert.ThrowsAsync<SchedulingValidationException>(() => Desk.CancelFollowingAsync(single.Id, new CancelAppointment()))).Errors);
    }
}

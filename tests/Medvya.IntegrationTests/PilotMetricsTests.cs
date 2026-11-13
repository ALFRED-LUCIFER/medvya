using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-041 on real PostgreSQL: the owner's pilot metrics (median check-in of returning patients, no-show rate, the database's
/// double-booking refusals) from what the desk already does, baselines with method and dates, practice configuration only, and
/// another tenant sees none of it. SAFETY: mutation check: dropping CheckInMs from the status event, the "first &lt; c.Start" returning
/// rule, the NoShow count, the conflict write in DoctorDayTransaction or the CanConfigure check each fails a test here.
/// MDV-P1-106: counting a not-measured check-in as zero again (in the query or in what check-in stores) fails the unmeasured test.
/// </summary>
public sealed class PilotMetricsTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    IPilotMetrics Metrics(Guid user) => h.Get<IPilotMetrics>(user);

    [Fact]
    public async Task CheckInMedianCountsReturningPatientsAndNoShowsAgainstVisitsAttended()
    {
        var first = await h.BookAsync(9);                       // Meera's first visit: not returning
        var second = await h.BookAsync(9, 20);                  // Meera again: returning
        var third = await h.BookAsync(9, 40);                   // returning
        var other = await h.BookAsync(10, 0, h.SecondPatient);  // first visit of another patient
        var missed = await h.BookAsync(10, 20);
        await h.BookAsync(10, 40);                              // still booked, outcome unknown: in neither number
        h.Clock.Advance(At(10, 30) - MondayMorning);
        var desk = h.FrontDesk(h.Reception);
        await desk.CheckInAsync(first.Id, new CheckIn { ElapsedMs = 80_000 });
        await desk.CheckInAsync(second.Id, new CheckIn { ElapsedMs = 30_000 });
        await desk.CheckInAsync(third.Id, new CheckIn { ElapsedMs = 50_000 });
        await desk.CheckInAsync(other.Id, new CheckIn { ElapsedMs = 90_000 });
        await h.Booking(h.Reception).MarkNoShowAsync(missed.Id);
        h.Clock.Advance(TimeSpan.FromMinutes(20));

        var m = await Metrics(h.Admin).GetAsync(h.Facility, 14);
        Assert.Equal(new CheckInMetric(40, 2, 65, 4, 60, 0), m.CheckIn);
        Assert.Equal(new NoShowMetric(1, 4, 20), m.NoShows);
        Assert.Equal((0, h.Clock.GetUtcNow()), (m.DoubleBookingRefusals, m.To));
        Assert.Empty(m.Baselines);
        var elsewhere = await Metrics(h.Elsewhere).GetAsync(h.OtherFacility, 14); // the other location's admin: none of Adyar's visits
        Assert.Equal((0, 0, 0), (elsewhere.CheckIn.Count, elsewhere.NoShows.NoShows, elsewhere.NoShows.Attended));
    }

    /// <summary>MDV-P1-106: a check-in with no start, an impossible time, or an old zero-second row is not measured: counted apart, never as 0 s.</summary>
    [Fact]
    public async Task ACheckInWithoutATrustworthyTimeIsLeftOutOfTheMedianNotCountedAsZero()
    {
        var visits = new List<AppointmentView>();
        for (var i = 0; i < 7; i++) visits.Add(await h.BookAsync(9 + i / 3, 20 * (i % 3))); // 09:00 to 11:00, one patient
        h.Clock.Advance(At(11, 30) - MondayMorning);
        var desk = h.FrontDesk(h.Reception);
        await desk.CheckInAsync(visits[0].Id, new CheckIn { ElapsedMs = 55_000 });  // the first visit: makes the rest returning
        await desk.CheckInAsync(visits[1].Id, new CheckIn { ElapsedMs = 40_000 });  // a walk-in checked in 40 s after Walk-in was opened
        await desk.CheckInAsync(visits[2].Id, new CheckIn());                       // no measured start
        await desk.CheckInAsync(visits[3].Id, new CheckIn { ElapsedMs = 0 });       // "created and checked in by one action"
        await desk.CheckInAsync(visits[4].Id, new CheckIn { ElapsedMs = -5_000 });  // a clock that went backwards
        await desk.CheckInAsync(visits[5].Id, new CheckIn { ElapsedMs = 400 });     // faster than any desk
        await desk.CheckInAsync(visits[6].Id, new CheckIn { ElapsedMs = long.MaxValue }); // absurd: must not overflow into a time
        await using (var db = sql.Scheduling(h.Tenant))
        {
            Assert.Equal([40_000, 55_000], (await db.StatusEvents.Where(e => e.CheckInMs != null).Select(e => e.CheckInMs!.Value).ToListAsync()).Order());
            // A row stored before the lower bound existed (a zero-second check-in of the old measure): excluded when read.
            var old = await db.StatusEvents.SingleAsync(e => e.AppointmentId == visits[5].Id && e.State == AppointmentState.CheckedIn);
            await db.StatusEvents.Where(e => e.Id == old.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.CheckInMs, 3));
        }
        h.Clock.Advance(TimeSpan.FromMinutes(1));

        // Counted as zero, the five would drag the returning median to 0 s ("target met"); left out, it is the one real 40 s.
        Assert.Equal(new CheckInMetric(40, 1, 47.5, 2, 60, 5), (await Metrics(h.Admin).GetAsync(h.Facility, 14)).CheckIn);
        // No timed check-in: no median at all (the panel says "No data yet"), not a number.
        Assert.Equal(new CheckInMetric(null, 0, null, 0, 60, 0), (await Metrics(h.Elsewhere).GetAsync(h.OtherFacility, 14)).CheckIn);
        await using var other = sql.Scheduling(Guid.NewGuid()); // another clinic reads none of these rows, with or without the EF filter
        Assert.Equal(0, await other.StatusEvents.IgnoreQueryFilters().CountAsync());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(-1L, null)]
    [InlineData(0L, null)]
    [InlineData(999L, null)]
    [InlineData(1000L, 1000)]
    [InlineData(1_800_000L, 1_800_000)]
    [InlineData(1_800_001L, null)]
    [InlineData(long.MaxValue, null)]
    public void ACheckInTimeIsTrustedOnlyBetweenOneSecondAndHalfAnHour(long? sent, int? stored) => Assert.Equal(stored, PilotMetricRules.TimedMs(sent));

    [Fact]
    public async Task ADoubleBookingTheDatabaseRefusesIsCounted()
    {
        var booked = await h.BookAsync(10);
        var scope = h.Get<IServiceProvider>(h.Reception);
        var db = scope.GetRequiredService<SchedulingDbContext>();
        // Past the service's checks, as a bug or a lost race would be: only the SCHEDULE_SLOT unique index is left to refuse it.
        var code = (await Assert.ThrowsAsync<SlotUnavailableException>(() => scope.GetRequiredService<DoctorDayTransaction>().RunAsync([(h.Doctor, Monday)], async () =>
        {
            var twin = new Appointment
            {
                Id = Guid.NewGuid(), TenantId = h.Tenant, FacilityId = h.Facility, CalendarId = h.Doctor, CalendarKind = CalendarKind.Doctor, PatientId = h.SecondPatient,
                VisitType = VisitType.FollowUp, Start = booked.Start, End = booked.End, State = AppointmentState.Booked, BookedAt = MondayMorning, ChangedAt = MondayMorning,
            };
            db.Appointments.Add(twin);
            db.ScheduleSlots.AddRange(ScheduleSlot.For(twin));
            await db.SaveChangesAsync();
            return twin;
        }, CancellationToken.None))).Code;
        Assert.Equal("slot_unavailable", code);
        h.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(1, (await Metrics(h.Admin).GetAsync(h.Facility, 14)).DoubleBookingRefusals);
        Assert.Equal(0, (await Metrics(h.Elsewhere).GetAsync(h.OtherFacility, 14)).DoubleBookingRefusals);
        await using var check = sql.Scheduling(h.Tenant);
        Assert.Equal(1, await check.Appointments.CountAsync()); // nothing of the refused booking was kept
    }

    [Fact]
    public async Task BaselinesKeepMethodAndDatesAndTheNewestPerMetricIsShown()
    {
        RecordBaseline Noshow(int value, string method) => new()
        {
            FacilityId = h.Facility, Metric = PilotMetric.NoShowPercent, Value = value, Method = method, FromDate = new(2026, 10, 5), ToDate = new(2026, 10, 18),
        };
        await Metrics(h.Doctor).RecordBaselineAsync(Noshow(18, "Paper register count"));
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await Metrics(h.Admin).RecordBaselineAsync(Noshow(16, "  Paper register, recounted  "));
        await Metrics(h.Admin).RecordBaselineAsync(new() { FacilityId = h.Facility, Metric = PilotMetric.CheckInSeconds, Value = 140, Method = "Stopwatch, 20 patients", FromDate = new(2026, 10, 12), ToDate = new(2026, 10, 12) });

        var baselines = (await Metrics(h.Admin).GetAsync(h.Facility, 14)).Baselines;
        Assert.Equal([PilotMetric.CheckInSeconds, PilotMetric.NoShowPercent], baselines.Select(b => b.Metric));
        Assert.Equal((16, "Paper register, recounted", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 18)), (baselines[1].Value, baselines[1].Method, baselines[1].FromDate, baselines[1].ToDate));
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(3, await db.MetricBaselines.CountAsync()); // append-only: the first count is kept
    }

    [Fact]
    public async Task ABaselineNeedsEveryFieldInRange()
    {
        var refused = await Assert.ThrowsAsync<SchedulingValidationException>(() => Metrics(h.Admin).RecordBaselineAsync(new()
        {
            FacilityId = h.Facility, Metric = PilotMetric.ReminderDeliveryPercent, Value = 120, Method = " ", FromDate = new(2026, 10, 18), ToDate = new(2026, 10, 5),
        }));
        Assert.Equal(["value:out_of_range", "method:required", "toDate:before_from"], refused.Errors.Select(e => $"{e.Field}:{e.Code}"));
        refused = await Assert.ThrowsAsync<SchedulingValidationException>(() => Metrics(h.Admin).RecordBaselineAsync(new() { FacilityId = h.Facility }));
        Assert.Equal(["metric", "value", "method", "fromDate", "toDate"], refused.Errors.Select(e => e.Field));
    }

    [Fact]
    public async Task OnlyPracticeConfigurationAtTheFacilitySeesOrRecordsMetrics()
    {
        var baseline = new RecordBaseline { FacilityId = h.Facility, Metric = PilotMetric.DoubleBookings, Value = 2, Method = "Complaints book", FromDate = new(2026, 10, 5), ToDate = new(2026, 10, 18) };
        foreach (var user in new[] { h.Reception, h.Nurse, h.SecondDoctor, h.Elsewhere })
        {
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => Metrics(user).GetAsync(h.Facility, 14));
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => Metrics(user).RecordBaselineAsync(baseline));
        }
        await Metrics(h.HospitalIt).RecordBaselineAsync(baseline); // Staff, roles, integrations at Config
        Assert.Single((await Metrics(h.Doctor).GetAsync(h.Facility, 14)).Baselines);
        Assert.Empty((await Metrics(h.Elsewhere).GetAsync(h.OtherFacility, 14)).Baselines);
    }

    [Fact]
    public async Task AnotherTenantSeesNoneOfTheMetricRows()
    {
        await Metrics(h.Admin).RecordBaselineAsync(new() { FacilityId = h.Facility, Metric = PilotMetric.DoubleBookings, Value = 1, Method = "Complaints book", FromDate = new(2026, 10, 5), ToDate = new(2026, 10, 18) });
        await using (var own = sql.Scheduling(h.Tenant))
            Assert.Equal(1, await own.MetricBaselines.CountAsync());
        await using var other = sql.Scheduling(Guid.NewGuid());
        Assert.Equal(0, await other.MetricBaselines.CountAsync());
        Assert.Equal(0, await other.MetricBaselines.IgnoreQueryFilters().CountAsync()); // row-level security, not only the EF filter
        // Layer one: the EF tenant filter on both new tables (row-level security alone would hide its removal above).
        foreach (var type in new[] { typeof(MetricBaseline), typeof(BookingConflict) })
            Assert.NotEmpty(other.Model.FindEntityType(type)!.GetDeclaredQueryFilters());
    }

    [Theory]
    [InlineData(new double[0], null)]
    [InlineData(new[] { 42.0 }, 42.0)]
    [InlineData(new[] { 90.0, 30, 50 }, 50.0)]
    [InlineData(new[] { 90.0, 30, 50, 80 }, 65.0)]
    public void MedianOfOddAndEvenCounts(double[] values, double? median) => Assert.Equal(median, PilotMetricRules.Median(values));

    [Fact]
    public void PercentIsNullWithoutAWhole() => Assert.Equal((null, 33.3), (PilotMetricRules.Percent(0, 0), PilotMetricRules.Percent(1, 3)));
}

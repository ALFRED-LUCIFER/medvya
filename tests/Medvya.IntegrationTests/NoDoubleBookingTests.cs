using System.Diagnostics;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-022 / P1 exit gate 2 on real SQL Server: no double booking under concurrency. A hundred desks at once get exactly one
/// booking; each guard is also tested on its own, so removing the unique index or the doctor-day lock fails a test.
/// </summary>
public sealed class NoDoubleBookingTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    const int Desks = 100;
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    HoldSlot Slot(int hour, int minute = 0, VisitType type = VisitType.FollowUp) =>
        new() { FacilityId = h.Facility, CalendarId = h.Doctor, Start = At(hour, minute), VisitType = type };

    BookAppointment Book(Guid holdId) => new() { HoldId = holdId, PatientId = h.Patient };

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

    /// <summary>Holds written straight to the table, one per desk and all for the same time: the hold step is bypassed, so only booking guards remain.</summary>
    async Task<List<Guid>> SneakHoldsAsync(IEnumerable<(Guid User, DateTimeOffset Start, DateTimeOffset End, VisitType Type)> holds)
    {
        await using var db = sql.Scheduling(h.Tenant);
        var rows = holds.Select(x => new SlotHold
        {
            Id = Guid.NewGuid(), TenantId = h.Tenant, FacilityId = h.Facility, CalendarId = h.Doctor, VisitType = x.Type, Start = x.Start, End = x.End,
            HeldBy = x.User, ExpiresAt = MondayMorning.AddMinutes(10),
        }).ToList();
        db.SlotHolds.AddRange(rows);
        await db.SaveChangesAsync();
        return [.. rows.Select(r => r.Id)];
    }

    [Fact]
    public async Task AHundredDesksBookingOneSlotAtOnceGiveExactlyOneAppointment()
    {
        var desks = await h.AddReceptionistsAsync(Desks);
        var holds = await SneakHoldsAsync(desks.Select(d => (d, At(10), At(10, 20), VisitType.FollowUp)));

        var (ok, codes) = await Race(desks.Zip(holds, (desk, hold) => (Func<Task>)(() => h.Booking(desk).BookAsync(Book(hold)))));

        Assert.Equal(1, ok);
        Assert.Equal(Desks - 1, codes.Count);
        Assert.All(codes, c => Assert.Equal("slot_unavailable", c)); // a clear "slot no longer available", never a crash or "busy"
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(1, await db.Appointments.CountAsync());
        Assert.Equal(4, await db.ScheduleSlots.CountAsync()); // 20 minutes = four 5-minute units
    }

    [Fact]
    public async Task AHundredDesksHoldingOneSlotAtOnceGiveExactlyOneHold()
    {
        var desks = await h.AddReceptionistsAsync(Desks);
        var (ok, codes) = await Race(desks.Select(d => (Func<Task>)(() => h.Booking(d).HoldAsync(Slot(10)))));

        Assert.Equal(1, ok);
        Assert.All(codes, c => Assert.Equal("being_booked", c));
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(1, await db.SlotHolds.CountAsync());
    }

    [Fact]
    public async Task DesksHoldingAndBookingDifferentSlotsAtOnceAllSucceedWithoutBusy()
    {
        // MDV-P1-037: unrelated slots must not queue on each other's range locks (the load test saw about 94 % "busy" on holds).
        await h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate() with { CalendarId = h.SecondDoctor });
        var desks = await h.AddReceptionistsAsync(40);
        var codes = new List<string>();
        var booked = 0;
        for (var round = 0; round < 3; round++) // two doctors, four days, a week a round, five desks on each doctor-day
        {
            var (ok, failed) = await Race(desks.Select((d, i) => (Func<Task>)(async () =>
            {
                var booking = h.Booking(d);
                var start = At(9).AddDays(round * 7 + i / 2 % 4).AddMinutes(i / 8 * 20);
                var hold = await booking.HoldAsync(new HoldSlot { FacilityId = h.Facility, CalendarId = i % 2 == 0 ? h.Doctor : h.SecondDoctor, Start = start, VisitType = VisitType.FollowUp });
                await booking.BookAsync(Book(hold.Id));
            })));
            (booked, codes) = (booked + ok, [.. codes, .. failed]);
        }

        Assert.Equal([], codes);
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(booked, await db.Appointments.CountAsync());
        Assert.Equal(120, booked);
    }

    [Fact]
    public async Task AHundredDesksOnOneDoctorDayBookEveryFreeSlotOnceAndNeverAnswerBusy()
    {
        // MDV-P1-022 / P1-037, the worst case for the doctor-day lock: every desk on the same doctor and day. 84 desks each take a
        // different free 5-minute slot (Tuesday 09:00-13:00 and 17:00-20:00) and must all book; 16 more race them for slots already taken.
        await h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate(slotMinutes: 5));
        var slots = Enumerable.Range(0, 48).Select(i => At(9).AddMinutes(5 * i)).Concat(Enumerable.Range(0, 36).Select(i => At(17).AddMinutes(5 * i)))
            .Select(s => s.AddDays(1)).ToList();
        var desks = await h.AddReceptionistsAsync(Desks);

        var (ok, codes) = await Race(desks.Select((d, i) => (Func<Task>)(async () =>
        {
            var booking = h.Booking(d);
            var hold = await booking.HoldAsync(new HoldSlot { FacilityId = h.Facility, CalendarId = h.Doctor, Start = slots[i % slots.Count], VisitType = VisitType.FollowUp });
            await booking.BookAsync(Book(hold.Id));
        })));

        Assert.Equal(slots.Count, ok); // every distinct free slot booked: no "busy", no deadlock or serialization error reaches a desk
        Assert.Equal(Desks - slots.Count, codes.Count);
        Assert.All(codes, c => Assert.True(c is "being_booked" or "slot_unavailable", c));
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(slots.Order(), (await db.Appointments.Select(a => a.Start).ToListAsync()).Order()); // one appointment per slot, none twice
        Assert.Equal(slots.Count, await db.ScheduleSlots.CountAsync());
    }

    [Fact]
    public async Task OverlappingMultiSlotBookingsAtOnceGiveExactlyOne()
    {
        var desks = await h.AddReceptionistsAsync(Desks);
        // Half want a 40-minute new consultation at 10:00, half a follow-up at 10:20 inside it.
        var holds = await SneakHoldsAsync(desks.Select((d, i) => i % 2 == 0
            ? (d, At(10), At(10, 40), VisitType.NewConsultation)
            : (d, At(10, 20), At(10, 40), VisitType.FollowUp)));

        var (ok, codes) = await Race(desks.Zip(holds, (desk, hold) => (Func<Task>)(() => h.Booking(desk).BookAsync(Book(hold)))));

        Assert.Equal(1, ok);
        Assert.All(codes, c => Assert.Equal("slot_unavailable", c));
        await using var db = sql.Scheduling(h.Tenant);
        var booked = await db.Appointments.SingleAsync();
        Assert.Equal((int)(booked.End - booked.Start).TotalMinutes / ScheduleSlot.UnitMinutes, await db.ScheduleSlots.CountAsync());
    }

    [Fact]
    public async Task TheDatabaseRefusesTwoAppointmentsInTheSameFiveMinutesEvenWithoutTheService()
    {
        await using var db = sql.Scheduling(h.Tenant);
        Appointment Visit(int hour, int minute, int length) => new()
        {
            Id = Guid.NewGuid(), TenantId = h.Tenant, FacilityId = h.Facility, CalendarId = h.Doctor, CalendarKind = CalendarKind.Doctor, PatientId = h.Patient,
            VisitType = VisitType.FollowUp, Start = At(hour, minute), End = At(hour, minute).AddMinutes(length), State = AppointmentState.Booked,
            BookedAt = MondayMorning, ChangedAt = MondayMorning,
        };
        var first = Visit(10, 0, 40);
        db.Appointments.Add(first);
        db.ScheduleSlots.AddRange(ScheduleSlot.For(first));
        await db.SaveChangesAsync();

        var overlapping = Visit(10, 35, 20); // shares 10:35–10:40 with the first, on a different grid
        db.Appointments.Add(overlapping);
        db.ScheduleSlots.AddRange(ScheduleSlot.For(overlapping));
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(refused.InnerException).SqlState);
    }

    /// <summary>Takes the doctor-day lock on another connection, as a second desk in mid-booking would.</summary>
    async Task<(NpgsqlConnection, NpgsqlTransaction)> HoldDoctorDayLockAsync()
    {
        var conn = new NpgsqlConnection(sql.ConnectionString);
        await conn.OpenAsync();
        var tx = conn.BeginTransaction();
        await using var cmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@r, 0))", conn, tx);
        cmd.Parameters.AddWithValue("r", $"doctor:{h.Tenant}:{h.Doctor}:2026-10-19");
        await cmd.ExecuteNonQueryAsync();
        return (conn, tx);
    }

    [Fact]
    public async Task BookingWaitsForTheDoctorDayLockAndRetriesSafely()
    {
        h.Options.LockTimeout = TimeSpan.FromMilliseconds(300);
        var hold = await h.Booking(h.Reception).HoldAsync(Slot(10));
        var (conn, tx) = await HoldDoctorDayLockAsync();
        var clock = Stopwatch.StartNew();
        var booking = h.Booking(h.Reception).BookAsync(Book(hold.Id));
        await Task.Delay(500);
        Assert.False(booking.IsCompleted); // waiting on the lock, not writing
        await tx.RollbackAsync();
        await conn.DisposeAsync();

        await booking; // an attempt timed out on the lock and was retried
        Assert.True(clock.ElapsedMilliseconds >= 500, $"booked after {clock.ElapsedMilliseconds} ms");
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(1, await db.Appointments.CountAsync()); // retries never duplicate
        Assert.Equal(1, await db.Outbox.CountAsync());
    }

    [Fact]
    public async Task ALockThatNeverFreesEndsInBusyAndWritesNothing()
    {
        h.Options.LockTimeout = TimeSpan.FromMilliseconds(200);
        var hold = await h.Booking(h.Reception).HoldAsync(Slot(10));
        var (conn, tx) = await HoldDoctorDayLockAsync();
        try
        {
            Assert.Equal("busy", (await Assert.ThrowsAsync<SlotUnavailableException>(() => h.Booking(h.Reception).BookAsync(Book(hold.Id)))).Code);
        }
        finally
        {
            await tx.RollbackAsync();
            await conn.DisposeAsync();
        }
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal((0, 0), (await db.Appointments.CountAsync(), await db.ScheduleSlots.CountAsync()));
        Assert.Equal(1, await db.SlotHolds.CountAsync()); // the hold survives, so the desk can try again
    }
}

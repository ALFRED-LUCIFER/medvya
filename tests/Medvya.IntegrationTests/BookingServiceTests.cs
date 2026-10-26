using System.Text.Json;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-021 on real SQL Server: a short slot hold, booking from it, expiry on a controllable clock, no booking outside the
/// template, multi-slot visits, the free-text reason kept away from reception, and AppointmentBooked.v1 without PHI.
/// </summary>
public sealed class BookingServiceTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    HoldSlot Slot(int hour, int minute = 0, VisitType type = VisitType.FollowUp) =>
        new() { FacilityId = h.Facility, CalendarId = h.Doctor, Start = At(hour, minute), VisitType = type };

    BookAppointment Book(Guid holdId) => new() { HoldId = holdId, PatientId = h.Patient, ReasonCategory = ReasonCategory.ChronicReview, Channel = BookingChannel.Phone };

    async Task<ScheduleColumn> DoctorColumn(Guid asUser) => (await h.Schedule(asUser).GetDayAsync(h.Facility, Monday)).Columns.Single(c => c.CalendarId == h.Doctor);

    static async Task<string> Code(Task task) => (await Assert.ThrowsAsync<SlotUnavailableException>(() => task)).Code;

    [Fact]
    public async Task AHeldSlotIsBeingBookedForOthersAndOnlyTheHolderCanConfirmIt()
    {
        var hold = await h.Booking(h.Reception).HoldAsync(Slot(9));
        Assert.Equal((At(9), At(9, 20), MondayMorning + TimeSpan.FromMinutes(2)), (hold.Start, hold.End, hold.ExpiresAt));

        Assert.True((await DoctorColumn(h.Doctor)).Slots.Single(s => s.Start == At(9)).Held); // "being booked" for everyone else
        Assert.False((await DoctorColumn(h.Reception)).Slots.Single(s => s.Start == At(9)).Held); // the holder has the panel open
        Assert.Equal("being_booked", await Code(h.Booking(h.Doctor).HoldAsync(Slot(9))));
        Assert.Equal("being_booked", await Code(h.Booking(h.Doctor).BookAsync(Book(hold.Id))));

        var booked = await h.Booking(h.Reception).BookAsync(Book(hold.Id));
        Assert.Equal((AppointmentState.Booked, "Meera Iyer (synthetic)", "000001", At(9), At(9, 20)), (booked.State, booked.PatientName, booked.Mrn, booked.Start, booked.End));
        Assert.StartsWith("APT-", booked.Reference, StringComparison.Ordinal);

        var column = await DoctorColumn(h.Reception);
        Assert.DoesNotContain(column.Slots, s => s.Start == At(9));
        var chip = Assert.Single(column.Appointments);
        Assert.Equal((booked.Id, "Meera Iyer (synthetic)", AppointmentState.Booked), (chip.Id, chip.PatientName, chip.State));
        Assert.Equal("hold_expired", await Code(h.Booking(h.Reception).BookAsync(Book(hold.Id)))); // a hold books once

        await using var db = sql.Scheduling(h.Tenant);
        Assert.Empty(await db.SlotHolds.ToListAsync());
        var status = Assert.Single(await db.StatusEvents.ToListAsync());
        Assert.Equal((booked.Id, AppointmentState.Booked, h.Reception), (status.AppointmentId, status.State, status.ActorUserId!.Value));
        var evt = Assert.Single(await db.Outbox.ToListAsync());
        Assert.Equal(("AppointmentBooked.v1", booked.Id), (evt.Type, evt.AggregateId));
        var payload = JsonDocument.Parse(evt.Payload).RootElement;
        Assert.Equal(["appointmentId", "facilityId", "calendarId", "patientId", "start", "end", "walkIn", "unscheduled"], payload.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("Meera", evt.Payload, StringComparison.Ordinal); // opaque ids only
    }

    [Fact]
    public async Task HoldsExpireAndTheSlotIsFreeAgain()
    {
        var hold = await h.Booking(h.Reception).HoldAsync(Slot(9));
        h.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False((await DoctorColumn(h.Doctor)).Slots.Single(s => s.Start == At(9)).Held);
        Assert.Equal("hold_expired", await Code(h.Booking(h.Reception).BookAsync(Book(hold.Id))));

        var taken = await h.Booking(h.Doctor).HoldAsync(Slot(9)); // anyone may hold it now
        Assert.NotEqual(hold.Id, taken.Id);
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal([taken.Id], await db.SlotHolds.Select(x => x.Id).ToListAsync()); // the expired hold was cleaned up
    }

    [Fact]
    public async Task TheHoldTimeIsConfigurable()
    {
        h.Options.SlotHold = TimeSpan.FromSeconds(30);
        var hold = await h.Booking(h.Reception).HoldAsync(Slot(9));
        Assert.Equal(MondayMorning + TimeSpan.FromSeconds(30), hold.ExpiresAt);
        h.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal("hold_expired", await Code(h.Booking(h.Reception).BookAsync(Book(hold.Id))));
    }

    [Fact]
    public async Task OnlySlotsOfAWorkingCalendarCanBeHeld()
    {
        Assert.Equal("not_bookable", await Code(h.Booking(h.Reception).HoldAsync(Slot(14)))); // not working 13–17
        Assert.Equal("not_bookable", await Code(h.Booking(h.Reception).HoldAsync(Slot(12)))); // lunch break
        Assert.Equal("not_bookable", await Code(h.Booking(h.Reception).HoldAsync(Slot(9, 10)))); // not a slot start
        Assert.Equal("not_bookable", await Code(h.Booking(h.Reception).HoldAsync(Slot(11, 40, VisitType.NewConsultation)))); // 40 min would run into lunch
        Assert.Equal("not_bookable", await Code(h.Booking(h.Reception).HoldAsync(Slot(9) with { CalendarId = h.SecondDoctor }))); // no template
        Assert.Equal("not_bookable", await Code(h.Booking(h.Reception).HoldAsync(Slot(9) with { Start = At(9).AddDays(5) }))); // Saturday
        h.Clock.Advance(TimeSpan.FromHours(2)); // 10:00 IST
        Assert.Equal("in_past", await Code(h.Booking(h.Reception).HoldAsync(Slot(9, 40))));

        await using (var tenancy = sql.Context(h.Tenant))
        {
            (await tenancy.Memberships.SingleAsync(m => m.UserId == h.Doctor)).Status = MembershipStatus.Suspended;
            await tenancy.SaveChangesAsync();
        }
        Assert.Equal("not_bookable", await Code(h.Booking(h.Reception).HoldAsync(Slot(11)))); // the doctor no longer works here
    }

    [Fact]
    public async Task AMultiSlotVisitTakesEverySlotItCovers()
    {
        var hold = await h.Booking(h.Reception).HoldAsync(Slot(9, 0, VisitType.NewConsultation));
        Assert.Equal(At(9, 40), hold.End);
        Assert.Equal("being_booked", await Code(h.Booking(h.Doctor).HoldAsync(Slot(9, 20)))); // the second slot is held too
        await h.Booking(h.Reception).BookAsync(Book(hold.Id));

        var column = await DoctorColumn(h.Doctor);
        Assert.DoesNotContain(column.Slots, s => s.Start < At(9, 40));
        Assert.Equal("slot_unavailable", await Code(h.Booking(h.Doctor).HoldAsync(Slot(9, 20))));
        Assert.Equal(TimeSpan.FromMinutes(40), Assert.Single(column.Appointments).End - At(9));
    }

    [Fact]
    public async Task OneHoldPerUserSoAnotherSlotOrVisitTypeReplacesIt()
    {
        var first = await h.Booking(h.Reception).HoldAsync(Slot(9));
        var longer = await h.Booking(h.Reception).HoldAsync(Slot(9, 0, VisitType.NewConsultation));
        Assert.Equal("hold_expired", await Code(h.Booking(h.Reception).BookAsync(Book(first.Id))));
        await h.Booking(h.Reception).ReleaseHoldAsync(longer.Id);
        Assert.False((await DoctorColumn(h.Doctor)).Slots.Single(s => s.Start == At(9)).Held);
        await h.Booking(h.Doctor).ReleaseHoldAsync(Guid.NewGuid()); // releasing a hold that is gone does nothing
    }

    [Fact]
    public async Task TheLongestVisitStillBlocksTheSlotsNearItsEnd()
    {
        // An 8-hour visit (the longest a template allows) from 09:00: the overlap check looks back that far, no further.
        await using (var db = sql.Scheduling(h.Tenant))
        {
            db.Appointments.Add(new Appointment
            {
                Id = Guid.NewGuid(), TenantId = h.Tenant, FacilityId = h.Facility, CalendarId = h.Doctor, CalendarKind = CalendarKind.Doctor, PatientId = h.Patient,
                VisitType = VisitType.Procedure, Start = At(9), End = At(9).AddMinutes(ScheduleRules.MaxVisitMinutes), State = AppointmentState.Booked,
                BookedAt = MondayMorning, ChangedAt = MondayMorning,
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal("slot_unavailable", await Code(h.Booking(h.Reception).HoldAsync(Slot(11, 40))));
        Assert.DoesNotContain((await DoctorColumn(h.Reception)).Slots, s => s.Start < At(17));
    }

    [Fact]
    public async Task TheDayViewReadsAppointmentsThroughAFacilityAndStartIndex()
    {
        await using var conn = await sql.OpenAs("dbo");
        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
            SELECT c.name, ic.is_included_column FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.name = N'IX_Appointments_TenantId_FacilityId_Start' AND i.object_id = OBJECT_ID(N'scheduling.Appointments')
            ORDER BY ic.is_included_column, ic.key_ordinal, c.name
            """, conn);
        var columns = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync()) columns.Add((reader.GetBoolean(1) ? "+" : "") + reader.GetString(0));
        Assert.Equal(["TenantId", "FacilityId", "Start", "+CalendarId", "+End", "+PatientId", "+State", "+VisitType"], columns);
    }

    [Fact]
    public async Task AnotherFacilitysPatientIsNeitherFoundNorBookable()
    {
        // Same tenant, registered at Velachery: reception at Adyar must not open or book them by id.
        Assert.Null(await h.Booking(h.Reception).FindPatientAsync(h.Facility, h.OtherFacilityPatient));
        var hold = await h.Booking(h.Reception).HoldAsync(Slot(9));
        var refused = await Assert.ThrowsAsync<SchedulingValidationException>(() => h.Booking(h.Reception).BookAsync(Book(hold.Id) with { PatientId = h.OtherFacilityPatient }));
        Assert.Equal([new ValidationError("patientId", "unknown")], refused.Errors);
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Empty(await db.Appointments.ToListAsync());
        Assert.Equal("Meera Iyer (synthetic)", (await h.Booking(h.Reception).BookAsync(Book(hold.Id))).PatientName); // the hold is still good
    }

    [Fact]
    public async Task OnlyStaffWhoMayBookCanHoldAndThePatientMustExistInThisTenant()
    {
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Booking(h.Nurse).HoldAsync(Slot(9))); // nurses read the calendar only
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Booking(h.Elsewhere).HoldAsync(Slot(9)));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Booking(h.Nurse).FindPatientAsync(h.Facility, h.Patient));

        var hold = await h.Booking(h.Reception).HoldAsync(Slot(9));
        var unknown = await Assert.ThrowsAsync<SchedulingValidationException>(() => h.Booking(h.Reception).BookAsync(Book(hold.Id) with { PatientId = Guid.NewGuid() }));
        Assert.Equal([new ValidationError("patientId", "unknown")], unknown.Errors);
        Assert.Null(await h.Booking(h.Reception).FindPatientAsync(h.Facility, Guid.NewGuid()));
        Assert.Equal("Deepak Nair (synthetic)", (await h.Booking(h.Reception).FindPatientAsync(h.Facility, h.SecondPatient))!.FullName);
        Assert.Equal("hold_expired", await Code(h.Booking(h.Reception, inTenant: Guid.NewGuid()).BookAsync(Book(hold.Id)))); // another tenant never finds the hold
    }

    [Fact]
    public async Task TheFreeTextReasonIsStoredForCliniciansButNeverReturnedToReception()
    {
        const string words = "Synthetic reason: sugar readings high since Diwali";
        var hold = await h.Booking(h.Reception).HoldAsync(Slot(9));
        var view = await h.Booking(h.Reception).BookAsync(Book(hold.Id) with { ReasonText = words });
        var day = await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday);

        Assert.DoesNotContain("Diwali", JsonSerializer.Serialize(view), StringComparison.Ordinal);
        Assert.DoesNotContain("Diwali", JsonSerializer.Serialize(day), StringComparison.Ordinal);
        Assert.Equal(ReasonCategory.ChronicReview, view.ReasonCategory);
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(words, (await db.Appointments.SingleAsync()).ReasonText);
        Assert.DoesNotContain("Diwali", (await db.Outbox.SingleAsync()).Payload, StringComparison.Ordinal);

        var tooLong = await h.Booking(h.Reception).HoldAsync(Slot(9, 20));
        var error = await Assert.ThrowsAsync<SchedulingValidationException>(() => h.Booking(h.Reception).BookAsync(Book(tooLong.Id) with { ReasonText = new string('x', 501) }));
        Assert.Equal([new ValidationError("reasonText", "too_long")], error.Errors);
    }

    [Fact]
    public async Task ChangingHoursListsTheAppointmentsTheyLeaveOutside()
    {
        var hold = await h.Booking(h.Reception).HoldAsync(Slot(17));
        var evening = await h.Booking(h.Reception).BookAsync(Book(hold.Id));

        var mornings = h.DoctorTemplate() with { Hours = [.. h.DoctorTemplate().Hours.Where(x => x.Start.Hour < 13)] };
        var saved = await h.Schedule(h.Admin).SaveTemplateAsync(mornings);
        Assert.Equal(evening.Id, Assert.Single(saved.AppointmentsOutsideHours).Id);

        var column = await DoctorColumn(h.Reception); // never moved or dropped: still on the schedule, for reception to act on
        Assert.Equal(evening.Id, Assert.Single(column.Appointments).Id);
        Assert.Equal(At(17, 20), (await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).End);
    }
}

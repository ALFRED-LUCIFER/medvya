using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-027 on real PostgreSQL: matching free slots in request order, an offer that holds the slot on the booking path and
/// queues a message of ids only, acceptance that books and removes, the hold window, and access and tenant isolation.
/// </summary>
public sealed class WaitlistTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql), other = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await other.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    IWaitlistService Desk(Guid? user = null) => h.Get<IWaitlistService>(user ?? h.Reception);

    Task<WaitlistView> AddAsync(Guid? patient = null, TimeOnly? from = null, TimeOnly? until = null, Guid? doctor = null, DayOfWeek[]? days = null) =>
        Desk().AddAsync(new AddToWaitlist
        {
            FacilityId = h.Facility, PatientId = patient ?? h.Patient, CalendarId = doctor, ReasonCategory = ReasonCategory.General,
            From = from, Until = until, Days = days ?? [],
        });

    async Task<WaitlistView> EntryAsync(Guid id) => (await Desk().ListAsync(h.Facility)).Entries.Single(e => e.Id == id);

    [Fact]
    public async Task EntriesAreInRequestOrderEachWithTheEarliestFreeSlotThatSuitsIt()
    {
        h.Clock.Advance(TimeSpan.FromHours(1));
        var first = await AddAsync(h.Patient, from: new(17, 0)); // wants the evening
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await AddAsync(h.SecondPatient, doctor: h.Doctor, days: [DayOfWeek.Tuesday]);
        await h.BookAsync(9, 20); // the morning is not free from the start, which must not matter to either

        var list = await Desk().ListAsync(h.Facility);
        Assert.Equal([first.Id, second.Id], list.Entries.Select(e => e.Id));
        Assert.Equal(At(17), list.Entries[0].Match!.Start);
        Assert.Equal(At(9).AddDays(1), list.Entries[1].Match!.Start); // Tuesday only
        Assert.Equal(h.Doctor, list.Entries[1].Match!.CalendarId);
        Assert.All(list.Entries, e => Assert.Equal(WaitlistState.Waiting, e.State));
    }

    [Fact]
    public async Task OfferHoldsTheSlotForEveryoneElseAndTheOtherCandidateIsOfferedTheNextOne()
    {
        var a = await AddAsync(h.Patient, from: new(9, 0), until: new(9, 0)); // exactly 09:00 on any day
        var b = await AddAsync(h.SecondPatient);
        var slot = (await EntryAsync(a.Id)).Match!;
        Assert.Equal(At(9), slot.Start);

        var offered = await Desk().OfferAsync(a.Id, new OfferSlot { CalendarId = slot.CalendarId, Start = slot.Start });
        Assert.Equal((WaitlistState.Offered, At(9)), (offered.State, offered.Offered!.Start));
        Assert.Equal(h.Clock.GetUtcNow().AddMinutes(30), offered.OfferExpiresAt);

        // Nobody else can hold, book or be offered it while the offer is open.
        var hold = new HoldSlot { FacilityId = h.Facility, CalendarId = h.Doctor, Start = At(9), VisitType = VisitType.FollowUp };
        Assert.Equal("being_booked", (await Assert.ThrowsAsync<SlotUnavailableException>(() => h.Booking(h.Admin).HoldAsync(hold))).Code);
        Assert.Equal("being_booked", (await Assert.ThrowsAsync<SlotUnavailableException>(() => h.Booking(h.Reception).HoldAsync(hold))).Code); // not even the desk that offered it
        Assert.Equal("being_booked", (await Assert.ThrowsAsync<SlotUnavailableException>(() => Desk().OfferAsync(b.Id, new OfferSlot { CalendarId = h.Doctor, Start = At(9) }))).Code);
        Assert.Equal(At(9, 20), (await EntryAsync(b.Id)).Match!.Start); // b's suggestion moved on

        // The offer runs out: the entry waits again, and the next candidate can be offered the same slot.
        h.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(WaitlistState.Waiting, (await EntryAsync(a.Id)).State);
        Assert.Equal(WaitlistState.Offered, (await Desk().OfferAsync(b.Id, new OfferSlot { CalendarId = h.Doctor, Start = At(9) })).State);
    }

    [Fact]
    public async Task OfferedSlotThatDoesNotSuitTheEntryOrIsTakenIsRefused()
    {
        var entry = await AddAsync(from: new(17, 0));
        Assert.Equal("not_a_match", (await Assert.ThrowsAsync<SlotUnavailableException>(() => Desk().OfferAsync(entry.Id, new OfferSlot { CalendarId = h.Doctor, Start = At(9) }))).Code);
        await h.BookAsync(17, 0, patient: h.SecondPatient);
        Assert.Equal("slot_unavailable", (await Assert.ThrowsAsync<SlotUnavailableException>(() => Desk().OfferAsync(entry.Id, new OfferSlot { CalendarId = h.Doctor, Start = At(17) }))).Code);
        Assert.Equal("in_past", (await Assert.ThrowsAsync<SlotUnavailableException>(() => Desk().OfferAsync(entry.Id, new OfferSlot { CalendarId = h.Doctor, Start = At(7) }))).Code);
        Assert.Equal(WaitlistState.Waiting, (await EntryAsync(entry.Id)).State);
    }

    [Fact]
    public async Task OfferWritesAnEventOfIdsAndTimeOnly()
    {
        var entry = await AddAsync();
        await Desk().OfferAsync(entry.Id, new OfferSlot { CalendarId = h.Doctor, Start = At(9) });
        await using var db = sql.Scheduling(h.Tenant);
        var evt = await db.Outbox.SingleAsync(e => e.AggregateId == entry.Id);
        Assert.Equal("WaitlistOffered.v1", evt.Type);
        Assert.DoesNotContain("Meera", evt.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("000001", evt.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceptingBooksTheHeldSlotAndRemovesThePatientFromTheList()
    {
        var entry = await AddAsync();
        await Desk().OfferAsync(entry.Id, new OfferSlot { CalendarId = h.Doctor, Start = At(9) });
        var visit = await Desk().AcceptAsync(entry.Id);

        Assert.Equal((h.Patient, At(9), AppointmentState.Booked, BookingChannel.Phone), (visit.PatientId, visit.Start, visit.State, visit.Channel));
        Assert.Empty((await Desk().ListAsync(h.Facility)).Entries);
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(4, await db.ScheduleSlots.CountAsync(s => s.AppointmentId == visit.Id)); // on the booking path: slot rows, so no double booking
        var row = await db.Waitlist.SingleAsync(w => w.Id == entry.Id);
        Assert.Equal((WaitlistState.Booked, visit.Id), (row.State, row.AppointmentId));
        Assert.False(await db.SlotHolds.AnyAsync(x => x.HeldBy == entry.Id));
        Assert.Contains(await db.Outbox.Where(e => e.AggregateId == visit.Id).ToListAsync(), e => e.Type == "AppointmentBooked.v1");
        Assert.Equal("slot_unavailable", (await Assert.ThrowsAsync<SlotUnavailableException>(() => h.BookAsync(9, 0, patient: h.SecondPatient))).Code);
        Assert.Equal("no_offer", (await Assert.ThrowsAsync<SlotUnavailableException>(() => Desk().AcceptAsync(entry.Id))).Code); // not twice
    }

    [Fact]
    public async Task AcceptingAfterTheHoldWindowIsRefusedAndDecliningFreesTheSlot()
    {
        var late = await AddAsync(h.Patient);
        await Desk().OfferAsync(late.Id, new OfferSlot { CalendarId = h.Doctor, Start = At(9) });
        h.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal("offer_expired", (await Assert.ThrowsAsync<SlotUnavailableException>(() => Desk().AcceptAsync(late.Id))).Code);
        await Desk().RemoveAsync(late.Id);

        var declined = await AddAsync(h.SecondPatient);
        await Desk().OfferAsync(declined.Id, new OfferSlot { CalendarId = h.Doctor, Start = At(17) });
        Assert.Equal(WaitlistState.Waiting, (await Desk().DeclineAsync(declined.Id)).State);
        Assert.Equal(At(17), (await h.BookAsync(17, 0, patient: h.Patient)).Start); // free at once, no waiting for the hold to run out
        Assert.Equal("invalid_transition", (await Assert.ThrowsAsync<VisitTransitionException>(() => Desk().DeclineAsync(declined.Id))).Code);
    }

    [Fact]
    public async Task TwoDesksOfferingOrAcceptingTheSameEntryCauseOneChange()
    {
        var entry = await AddAsync();
        var slot = new OfferSlot { CalendarId = h.Doctor, Start = At(9) };
        var offers = await Task.WhenAll(new[] { h.Reception, h.Admin }.Select(async u =>
        {
            try { await Desk(u).OfferAsync(entry.Id, slot); return true; } catch (VisitTransitionException) { return false; }
        }));
        Assert.Equal(1, offers.Count(o => o));

        var accepts = await Task.WhenAll(new[] { h.Reception, h.Admin }.Select(async u =>
        {
            try { await Desk(u).AcceptAsync(entry.Id); return true; } catch (Exception e) when (e is SlotUnavailableException or VisitTransitionException) { return false; }
        }));
        Assert.Equal(1, accepts.Count(a => a));
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(1, await db.Appointments.CountAsync(a => a.PatientId == h.Patient && a.Start == At(9)));
    }

    [Fact]
    public async Task OnlyDeskStaffWithBookingAccessAtTheFacilityMayUseIt()
    {
        var entry = await AddAsync();
        foreach (var user in new[] { h.Nurse, h.PortalUser, h.Elsewhere, h.CareCoordinator, h.HospitalIt })
        {
            var desk = Desk(user);
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => desk.ListAsync(h.Facility));
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => desk.AddAsync(new AddToWaitlist { FacilityId = h.Facility, PatientId = h.SecondPatient }));
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => desk.OfferAsync(entry.Id, new OfferSlot { CalendarId = h.Doctor, Start = At(9) }));
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => desk.AcceptAsync(entry.Id));
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => desk.DeclineAsync(entry.Id));
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => desk.RemoveAsync(entry.Id));
        }
        Assert.Equal(WaitlistState.Waiting, (await EntryAsync(entry.Id)).State);
        // Another practice sees nothing: the entry is "not found" there (query filter and row-level security).
        await Assert.ThrowsAsync<WaitlistNotFoundException>(() => other.Get<IWaitlistService>(other.Reception).RemoveAsync(entry.Id));
    }

    [Fact]
    public async Task AddingChecksThePatientDoctorWindowAndDuplicates()
    {
        async Task<IReadOnlyList<ValidationError>> Invalid(AddToWaitlist c) => (await Assert.ThrowsAsync<SchedulingValidationException>(() => Desk().AddAsync(c))).Errors;
        await h.SeedOtherOrganisationPatientAsync();
        Assert.Equal([new ValidationError("patientId", "unknown")], await Invalid(new() { FacilityId = h.Facility, PatientId = h.ForeignPatient })); // another organisation's patient
        Assert.Equal([new ValidationError("calendarId", "unknown")], await Invalid(new() { FacilityId = h.Facility, PatientId = h.Patient, CalendarId = Guid.NewGuid() }));
        Assert.Equal([new ValidationError("until", "before_from")], await Invalid(new() { FacilityId = h.Facility, PatientId = h.Patient, From = new(12, 0), Until = new(9, 0) }));
        await AddAsync();
        Assert.Equal([new ValidationError("patientId", "already_waiting")], await Invalid(new() { FacilityId = h.Facility, PatientId = h.Patient }));
    }

    [Fact]
    public void PreferencesMatchInTheFacilityTimeZone()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var doctor = Guid.NewGuid();
        var entry = new WaitlistEntry { CalendarId = doctor, Days = WaitlistEntry.DayBit(DayOfWeek.Monday), From = new(9, 0), Until = new(11, 0) };
        Assert.True(entry.Suits(doctor, At(9), zone));
        Assert.True(entry.Suits(doctor, At(11), zone)); // the window's end is inclusive
        Assert.False(entry.Suits(doctor, At(11, 20), zone));
        Assert.False(entry.Suits(doctor, At(8, 40), zone));
        Assert.False(entry.Suits(doctor, At(9).AddDays(1), zone)); // Tuesday
        Assert.False(entry.Suits(Guid.NewGuid(), At(9), zone)); // another doctor
        // 00:30 IST Tuesday is 19:00 UTC Monday: it must count as Tuesday.
        Assert.False(new WaitlistEntry { Days = WaitlistEntry.DayBit(DayOfWeek.Monday) }.Suits(doctor, At(0, 30).AddDays(1), zone));
    }
}

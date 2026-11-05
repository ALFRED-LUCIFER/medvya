using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-012 on real SQL Server: leave and one-off sessions, the action required for every affected booking before anything is
/// saved, the rebooking message without clinical details, slots for an extra session on its day only, and the audit trail.
/// </summary>
public sealed class ScheduleAdjustmentTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    static readonly DateOnly Saturday = new(2026, 10, 24);
    static readonly DateTimeOffset Tuesday = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime();
    readonly SchedulingHarness h = new(sql);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
    }

    public Task DisposeAsync() => h.CloseAsync();

    IScheduleExceptionService Desk => h.Adjustments(h.Reception);

    SaveAdjustment Leave(IReadOnlyList<AppointmentAction>? actions = null) => new()
    {
        FacilityId = h.Facility, CalendarId = h.Doctor, Kind = ExceptionKind.Leave, FromDate = Monday, ToDate = Monday, Reason = "Conference", Actions = actions ?? [],
    };

    async Task<(AppointmentView A, AppointmentView B)> TwoVisitsAsync() => (await h.BookAsync(10), await h.BookAsync(11, patient: h.SecondPatient));

    async Task<AppointmentState> StateAsync(Guid id)
    {
        await using var db = sql.Scheduling(h.Tenant);
        return (await db.Appointments.SingleAsync(a => a.Id == id)).State;
    }

    [Fact]
    public async Task ReleasingAnOffboardedDoctorMovesTheirFutureVisitsToNeedsRebookingAndFreesTheSlots()
    {
        var (a, b) = await TwoVisitsAsync();
        Assert.Equal(2, await Desk.ReleaseDoctorAsync(h.Doctor));
        Assert.Equal([AppointmentState.NeedsRebooking, AppointmentState.NeedsRebooking], [await StateAsync(a.Id), await StateAsync(b.Id)]);
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(0, await db.ScheduleSlots.CountAsync(s => s.AppointmentId == a.Id || s.AppointmentId == b.Id));
        Assert.Equal(0, await Desk.ReleaseDoctorAsync(h.Doctor)); // nothing left to move
    }

    [Fact]
    public async Task LeaveOverBookedVisitsListsThemAndSavesNothingUntilEachHasAnAction()
    {
        var (a, b) = await TwoVisitsAsync();

        var none = await Assert.ThrowsAsync<ExceptionActionsRequiredException>(() => Desk.SaveAsync(Leave()));
        Assert.Equal([a.Id, b.Id], none.Affected.Select(x => x.Id));
        var one = await Assert.ThrowsAsync<ExceptionActionsRequiredException>(() => Desk.SaveAsync(Leave([new(a.Id, RebookAction.Offer)])));
        Assert.Equal(2, one.Affected.Count);

        Assert.Empty(await Desk.ListAsync(h.Facility, null)); // no leave was saved
        Assert.Equal([AppointmentState.Booked, AppointmentState.Booked], [await StateAsync(a.Id), await StateAsync(b.Id)]);
        var column = (await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).Columns.Single(c => c.CalendarId == h.Doctor);
        Assert.NotEmpty(column.Slots); // still working that day
    }

    [Fact]
    public async Task SavedLeaveMovesVisitsToNeedsRebookingFreesTheirSlotsAndMessagesOnlyTheOffered()
    {
        var (a, b) = await TwoVisitsAsync();
        var saved = await Desk.SaveAsync(Leave([new(a.Id, RebookAction.Offer), new(b.Id, RebookAction.Manual)]));
        Assert.Equal((1, 1), (saved.Offered, saved.Manual));

        Assert.Equal([AppointmentState.NeedsRebooking, AppointmentState.NeedsRebooking], [await StateAsync(a.Id), await StateAsync(b.Id)]);
        await using (var db = sql.Scheduling(h.Tenant))
        {
            Assert.Equal(0, await db.ScheduleSlots.CountAsync(s => s.AppointmentId == a.Id || s.AppointmentId == b.Id));
            var events = await db.Outbox.Where(e => e.Type == "AppointmentNeedsRebooking.v1").ToListAsync();
            var offered = Assert.Single(events); // the manual one sends nothing
            Assert.Equal(a.Id, offered.AggregateId);
            Assert.DoesNotContain("Meera", offered.Payload, StringComparison.Ordinal);
            Assert.DoesNotContain("chest", offered.Payload, StringComparison.Ordinal);
            Assert.Equal(2, await db.StatusEvents.CountAsync(e => e.State == AppointmentState.NeedsRebooking && e.ActorUserId == h.Reception));
        }

        // The day shows the leave under its reason, offers no slot and cannot be booked into.
        var column = (await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).Columns.Single(c => c.CalendarId == h.Doctor);
        Assert.Empty(column.Slots);
        Assert.Contains(column.Unavailable, u => u is { Reason: UnavailableReason.Leave, Label: "Conference" });
        var booking = h.Booking(h.Reception);
        var refused = await Assert.ThrowsAsync<SlotUnavailableException>(() => booking.HoldAsync(new HoldSlot { FacilityId = h.Facility, CalendarId = h.Doctor, Start = At(10), VisitType = VisitType.FollowUp }));
        Assert.Equal("not_bookable", refused.Code);

        // Audited: the leave and each moved visit.
        await using var audit = sql.Audit(h.Tenant);
        var actions = await audit.Events.Select(e => e.Action).ToListAsync();
        Assert.Contains(SchedulingAudit.ExceptionCreate, actions);
        Assert.Equal(2, actions.Count(x => x == SchedulingAudit.NeedsRebooking));

        // Reception then moves the visit by hand to another day: it is booked again.
        var tuesday = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime();
        var moved = await booking.RescheduleAsync(b.Id, new RescheduleAppointment { CalendarId = h.Doctor, Start = tuesday });
        Assert.Equal(AppointmentState.Booked, moved.State);
    }

    [Fact]
    public async Task ActionForAVisitThatIsNotAffectedIsRefused()
    {
        var elsewhere = await h.BookAsync(10, start: new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.FromHours(5.5)).ToUniversalTime());
        var e = await Assert.ThrowsAsync<SchedulingValidationException>(() => Desk.SaveAsync(Leave([new(elsewhere.Id, RebookAction.Offer)])));
        Assert.Equal([new ValidationError("actions", "unknown")], e.Errors);
        Assert.Empty(await Desk.ListAsync(h.Facility, null));
    }

    [Fact]
    public async Task ShortenedDayAffectsOnlyVisitsOutsideTheKeptWindow()
    {
        var (inside, outside) = (await h.BookAsync(10), await h.BookAsync(11, 20, patient: h.SecondPatient));
        var shorten = new SaveAdjustment { FacilityId = h.Facility, CalendarId = h.Doctor, Kind = ExceptionKind.ShortenedDay, FromDate = Monday, ToDate = Monday, Start = new(9, 0), End = new(11, 0) };

        var required = await Assert.ThrowsAsync<ExceptionActionsRequiredException>(() => Desk.SaveAsync(shorten));
        Assert.Equal([outside.Id], required.Affected.Select(x => x.Id));
        await Desk.SaveAsync(shorten with { Actions = [new(outside.Id, RebookAction.Offer)] });

        Assert.Equal([AppointmentState.Booked, AppointmentState.NeedsRebooking], [await StateAsync(inside.Id), await StateAsync(outside.Id)]);
        var column = (await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).Columns.Single(c => c.CalendarId == h.Doctor);
        Assert.All(column.Slots, s => Assert.True(s.End <= At(11)));
    }

    [Fact]
    public async Task ExtraSessionCreatesSlotsForThatDayOnly()
    {
        Assert.Empty(ColumnOf(await h.Schedule(h.Reception).GetDayAsync(h.Facility, Saturday)).Slots); // a Saturday is closed

        var extra = new SaveAdjustment { FacilityId = h.Facility, CalendarId = h.Doctor, Kind = ExceptionKind.ExtraSession, FromDate = Saturday, ToDate = Saturday, Start = new(10, 0), End = new(11, 0) };
        await Desk.SaveAsync(extra);

        var slots = ColumnOf(await h.Schedule(h.Reception).GetDayAsync(h.Facility, Saturday)).Slots;
        Assert.Equal(3, slots.Count); // 20-minute slots in an hour
        Assert.Empty(ColumnOf(await h.Schedule(h.Reception).GetDayAsync(h.Facility, Saturday.AddDays(7))).Slots);
        var hold = await h.Booking(h.Reception).HoldAsync(new HoldSlot { FacilityId = h.Facility, CalendarId = h.Doctor, Start = slots[0].Start, VisitType = VisitType.FollowUp });
        Assert.Equal(slots[0].Start, hold.Start);

        var overlap = await Assert.ThrowsAsync<SchedulingValidationException>(() => Desk.SaveAsync(extra with { Start = new(10, 30), End = new(11, 30) }));
        Assert.Equal([new ValidationError("start", "overlap")], overlap.Errors);
    }

    [Fact]
    public async Task DeletingLeaveGivesTheSlotsBackAndLeavesVisitsAlreadyMovedToNeedsRebooking()
    {
        var (a, b) = await TwoVisitsAsync();
        var saved = await Desk.SaveAsync(Leave([new(a.Id, RebookAction.Offer), new(b.Id, RebookAction.Manual)]));
        var elsewhere = await h.BookAsync(10, start: Tuesday);
        Assert.Empty(ColumnOf(await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).Slots);

        await Desk.DeleteAsync(h.Facility, saved.Exception.Id);

        Assert.Empty(await Desk.ListAsync(h.Facility, null));
        Assert.NotEmpty(ColumnOf(await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).Slots); // bookable again
        // Nobody is re-booked silently: the moved visits stay for reception, the other day's visit is untouched.
        Assert.Equal([AppointmentState.NeedsRebooking, AppointmentState.NeedsRebooking, AppointmentState.Booked], [await StateAsync(a.Id), await StateAsync(b.Id), await StateAsync(elsewhere.Id)]);
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(0, await db.ScheduleSlots.CountAsync(s => s.AppointmentId == a.Id || s.AppointmentId == b.Id));
        await using var audit = sql.Audit(h.Tenant);
        Assert.Contains(SchedulingAudit.ExceptionDelete, await audit.Events.Select(e => e.Action).ToListAsync());
        var gone = await Assert.ThrowsAsync<SchedulingValidationException>(() => Desk.DeleteAsync(h.Facility, saved.Exception.Id));
        Assert.Equal([new ValidationError("id", "unknown")], gone.Errors);
    }

    [Fact]
    public async Task EditingLeaveGivesDroppedDaysBackAndAsksAnActionForVisitsOnAddedDays()
    {
        var onTuesday = await h.BookAsync(10, start: Tuesday);
        var saved = await Desk.SaveAsync(Leave());
        var edit = Leave() with { ToDate = Monday.AddDays(1) };

        var required = await Assert.ThrowsAsync<ExceptionActionsRequiredException>(() => Desk.SaveAsync(edit with { ReplacesId = saved.Exception.Id }));
        Assert.Equal([onTuesday.Id], required.Affected.Select(x => x.Id));
        Assert.Equal(AppointmentState.Booked, await StateAsync(onTuesday.Id));

        var longer = await Desk.SaveAsync(edit with { ReplacesId = saved.Exception.Id, Actions = [new(onTuesday.Id, RebookAction.Manual)] });
        Assert.Equal(saved.Exception.Id, longer.Exception.Id);
        Assert.Equal(AppointmentState.NeedsRebooking, await StateAsync(onTuesday.Id));

        await Desk.SaveAsync(Leave() with { FromDate = Monday.AddDays(1), ToDate = Monday.AddDays(1), ReplacesId = saved.Exception.Id }); // Monday is given back
        var only = Assert.Single(await Desk.ListAsync(h.Facility, null));
        Assert.Equal(Monday.AddDays(1), only.FromDate);
        Assert.NotEmpty(ColumnOf(await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).Slots);
        var unknown = await Assert.ThrowsAsync<SchedulingValidationException>(() => Desk.SaveAsync(Leave() with { ReplacesId = Guid.NewGuid() }));
        Assert.Equal([new ValidationError("id", "unknown")], unknown.Errors);
    }

    [Fact]
    public async Task ExtraSessionWithBookedVisitsCannotBeDeletedOrMovedOffThem()
    {
        var extra = new SaveAdjustment { FacilityId = h.Facility, CalendarId = h.Doctor, Kind = ExceptionKind.ExtraSession, FromDate = Saturday, ToDate = Saturday, Start = new(10, 0), End = new(11, 0) };
        var saved = await Desk.SaveAsync(extra);
        var slot = ColumnOf(await h.Schedule(h.Reception).GetDayAsync(h.Facility, Saturday)).Slots[0].Start;
        var visit = await h.BookAsync(10, start: slot);

        var refused = await Assert.ThrowsAsync<SchedulingValidationException>(() => Desk.DeleteAsync(h.Facility, saved.Exception.Id));
        Assert.Equal([new ValidationError("id", "has_bookings")], refused.Errors);
        Assert.Single(await Desk.ListAsync(h.Facility, null));
        Assert.Equal(AppointmentState.Booked, await StateAsync(visit.Id));

        var moved = await Assert.ThrowsAsync<ExceptionActionsRequiredException>(() => Desk.SaveAsync(extra with { Start = new(12, 0), End = new(13, 0), ReplacesId = saved.Exception.Id }));
        Assert.Equal([visit.Id], moved.Affected.Select(x => x.Id));
    }

    [Fact]
    public async Task OnlyStaffWhoCanBookDeleteAdjustmentsAndOnlyAtTheirOwnFacility()
    {
        var saved = await Desk.SaveAsync(Leave());
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Adjustments(h.Nurse).DeleteAsync(h.Facility, saved.Exception.Id));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Adjustments(h.Elsewhere).DeleteAsync(h.Facility, saved.Exception.Id));
        Assert.Single(await Desk.ListAsync(h.Facility, null));
    }

    static ScheduleColumn ColumnOf(ScheduleDay day) => day.Columns.Single(c => c.Name.StartsWith("Dr. Arjun", StringComparison.Ordinal));

    [Fact]
    public async Task OnlyStaffWhoCanBookSaveAdjustmentsAndOnlyAtTheirOwnFacility()
    {
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Adjustments(h.Nurse).SaveAsync(Leave()));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Adjustments(h.Elsewhere).SaveAsync(Leave()));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Adjustments(h.Elsewhere).ListAsync(h.Facility, null));
        Assert.Empty(await Desk.ListAsync(h.Facility, null));
    }

    [Fact]
    public async Task PastLeaveAndBadWindowsAreRefused()
    {
        h.Clock.Advance(TimeSpan.FromDays(1));
        var past = await Assert.ThrowsAsync<SchedulingValidationException>(() => Desk.SaveAsync(Leave()));
        Assert.Equal([new ValidationError("fromDate", "past")], past.Errors);
        var window = await Assert.ThrowsAsync<SchedulingValidationException>(() => Desk.SaveAsync(new SaveAdjustment
        {
            FacilityId = h.Facility, CalendarId = h.Doctor, Kind = ExceptionKind.ExtraSession, FromDate = Saturday, ToDate = Saturday.AddDays(1), Start = new(10, 0), End = new(9, 0),
        }));
        Assert.Contains(new ValidationError("toDate", "single_day"), window.Errors);
    }

    [Fact]
    public async Task TenantCannotReadAnotherTenantsAdjustments()
    {
        await Desk.SaveAsync(Leave());
        await using var db = sql.Scheduling(Guid.NewGuid());
        Assert.Empty(await db.Exceptions.ToListAsync());
    }

    [Fact]
    public void RebookingMessageNamesOnlyClinicDateAndTimeInEveryLanguage()
    {
        foreach (var lang in new[] { "en", "hi", "ar" })
        {
            var message = MessageCatalog.Render(NotificationKind.NeedsRebooking, lang, "Adyar (synthetic)", new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.FromHours(5.5)));
            Assert.Equal($"NeedsRebooking.{lang}", message.TemplateKey);
            Assert.Equal(message.TemplateKey, MessageCatalog.RegisteredTemplate(message.Body)); // a registered DLT text, so it can be sent
            Assert.DoesNotContain(MessageCatalog.Slot, message.Body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ScheduleRulesApplyLeaveExtraSessionAndShortenedDayToTheirDatesOnly()
    {
        var template = ScheduleRulesTests.MorningAndEvening();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        ScheduleAdjustment Adj(ExceptionKind kind, DateOnly date, TimeOnly? s = null, TimeOnly? e = null) =>
            new() { Kind = kind, FromDate = date, ToDate = date, Start = s, End = e };

        Assert.Empty(ScheduleRules.Day(template, Monday, zone, [Adj(ExceptionKind.Leave, Monday)]).Slots);
        Assert.NotEmpty(ScheduleRules.Day(template, Monday.AddDays(1), zone, [Adj(ExceptionKind.Leave, Monday)]).Slots);
        Assert.Single(ScheduleRules.Day(template, Saturday, zone, [Adj(ExceptionKind.ExtraSession, Saturday, new(10, 0), new(10, 20))]).Slots);
        var shortened = ScheduleRules.Day(template, Monday, zone, [Adj(ExceptionKind.ShortenedDay, Monday, new(10, 0), new(10, 40))]);
        Assert.Equal(2, shortened.Slots.Count);
    }
}

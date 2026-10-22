using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Microsoft.Data.SqlClient;
using Medvya.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-011 on real SQL Server: templates per doctor and resource, the day view in facility time, role checks against the
/// membership at the facility, and both tenant isolation layers. Wired through the real module registrations. Synthetic data only.
/// </summary>
public sealed class ScheduleServiceTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    static readonly DateOnly Monday = SchedulingHarness.Monday;
    readonly SchedulingHarness h = new(sql);

    public Task InitializeAsync() => h.SeedAsync();

    public Task DisposeAsync() => h.CloseAsync();

    [Fact]
    public async Task ASavedTemplateGivesBookableSlotsInFacilityLocalTime()
    {
        var saved = await h.DoctorTemplateAsync();
        Assert.Equal(("Dr. Arjun Rao (synthetic)", 20), (saved.Name, saved.SlotMinutes));

        var day = await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday);
        Assert.Equal(("Asia/Kolkata", Monday), (day.TimeZone, day.Date));
        Assert.Equal((new DateTimeOffset(2026, 10, 19, 3, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 19, 14, 30, 0, TimeSpan.Zero)), (day.Start, day.End));
        var column = day.Columns.Single(c => c.CalendarId == h.Doctor);
        Assert.Equal((CalendarKind.Doctor, 18), (column.CalendarKind, column.Slots.Count)); // 21 slots minus the lunch hour
        Assert.Equal([UnavailableReason.Break, UnavailableReason.NotWorking], column.Unavailable.Select(u => u.Reason));
        Assert.Equal("Lunch break", column.Unavailable[0].Label);
        Assert.Equal(40, column.VisitLengths.Single(v => v.VisitType == VisitType.NewConsultation).Minutes);
        Assert.Equal(UnavailableReason.NoSchedule, Assert.Single(day.Columns.Single(c => c.CalendarId == h.SecondDoctor).Unavailable).Reason);

        var sunday = await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday.AddDays(6));
        Assert.Equal(UnavailableReason.NotWorking, Assert.Single(sunday.Columns.Single(c => c.CalendarId == h.Doctor).Unavailable).Reason); // closed all day
    }

    [Fact]
    public async Task TodayIsTheFacilitysDate()
    {
        h.Clock.Advance(TimeSpan.FromHours(17)); // 01:00 IST on Tuesday is still Monday 19:30 in UTC
        Assert.Equal(Monday.AddDays(1), (await h.Schedule(h.Reception).GetDayAsync(h.Facility, null)).Date);
    }

    [Fact]
    public async Task DoctorNamesComeFromOneBatchedLookup()
    {
        await using var provider = new ServiceCollection().AddIdentityModule(sql.ConnectionString).BuildServiceProvider();
        var staff = provider.GetRequiredService<Medvya.Identity.Application.IStaffUsers>();
        var names = await staff.DisplayNamesAsync([h.Doctor, h.Doctor, Guid.NewGuid()]);
        Assert.Equal(new Dictionary<Guid, string> { [h.Doctor] = "Dr. Arjun Rao (synthetic)" }, names); // unknown ids left out, duplicates once
        Assert.Empty(await staff.DisplayNamesAsync([]));
    }

    [Fact]
    public async Task AResourceIsAColumnLikeADoctor()
    {
        var room = await h.Schedule(h.Admin).AddResourceAsync(new AddResource { FacilityId = h.Facility, Name = "  Procedure room  " });
        Assert.Equal("Procedure room", room.Name);
        var before = await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday);
        Assert.Equal(["Dr. Arjun Rao (synthetic)", "Dr. S. Menon (synthetic)", "Procedure room"], before.Columns.Select(c => c.Name));
        Assert.All(before.Columns, c => Assert.Equal(UnavailableReason.NoSchedule, Assert.Single(c.Unavailable).Reason)); // nothing set up yet

        await h.Schedule(h.Admin).SaveTemplateAsync(new SaveTemplate
        {
            FacilityId = h.Facility, CalendarId = room.Id, CalendarKind = CalendarKind.Resource, SlotMinutes = 30,
            Hours = [new WorkingHours { Weekday = DayOfWeek.Monday, Start = new(10, 0), End = new(12, 0) }],
        });
        var resource = (await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).Columns.Single(c => c.CalendarKind == CalendarKind.Resource);
        Assert.Equal(4, resource.Slots.Count);

        var duplicate = await Assert.ThrowsAsync<SchedulingValidationException>(() => h.Schedule(h.Admin).AddResourceAsync(new AddResource { FacilityId = h.Facility, Name = "Procedure room" }));
        Assert.Equal([new ValidationError("name", "duplicate")], duplicate.Errors);
    }

    [Fact]
    public async Task ChangingATemplateReplacesTheOldOne()
    {
        await h.DoctorTemplateAsync();
        await h.Schedule(h.Doctor).SaveTemplateAsync(h.DoctorTemplate(slotMinutes: 15) with { Breaks = [], VisitLengths = [] }); // the owner doctor configures too

        Assert.Equal(15, (await h.Schedule(h.Admin).ListTemplatesAsync(h.Facility)).Single(t => t.CalendarId == h.Doctor).SlotMinutes);
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(1, await db.Templates.CountAsync(t => t.CalendarId == h.Doctor));
        Assert.Equal(28, (await h.Schedule(h.Reception).GetDayAsync(h.Facility, Monday)).Columns.Single(c => c.CalendarId == h.Doctor).Slots.Count); // 16 + 12 quarter hours
    }

    [Fact]
    public async Task OnlyMembersWithTheRightRoleAtThisFacilityGetIn()
    {
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Schedule(h.Reception).SaveTemplateAsync(h.DoctorTemplate()));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Schedule(h.Reception).AddResourceAsync(new AddResource { FacilityId = h.Facility, Name = "Room" }));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Schedule(h.PortalUser).GetDayAsync(h.Facility, Monday)); // patients use the portal
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Schedule(h.Elsewhere).GetDayAsync(h.Facility, Monday)); // admin of another facility
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Schedule(h.Admin, inTenant: Guid.NewGuid()).GetDayAsync(h.Facility, Monday)); // another tenant
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Schedule(Guid.NewGuid()).GetDayAsync(h.Facility, Monday));
        Assert.Equal(2, (await h.Schedule(h.Nurse).GetDayAsync(h.Facility, Monday)).Columns.Count); // nurses read the calendar
    }

    [Fact]
    public async Task OnlyADoctorOrResourceOfTheFacilityCanHaveATemplate()
    {
        var notADoctor = await Assert.ThrowsAsync<SchedulingValidationException>(() => h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate() with { CalendarId = h.Reception }));
        Assert.Equal([new ValidationError("calendarId", "unknown")], notADoctor.Errors);
        var wrongKind = await Assert.ThrowsAsync<SchedulingValidationException>(() => h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate() with { CalendarKind = CalendarKind.Resource }));
        Assert.Equal([new ValidationError("calendarId", "unknown")], wrongKind.Errors);
        var invalid = await Assert.ThrowsAsync<SchedulingValidationException>(() => h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate(slotMinutes: 7)));
        Assert.Contains(new ValidationError("slotMinutes", "invalid"), invalid.Errors);

        await using var db = sql.Scheduling(h.Tenant);
        Assert.False(await db.Templates.AnyAsync()); // nothing rejected was saved
    }

    [Fact]
    public async Task AnotherTenantNeverSeesTheScheduleEvenWithQueryFiltersOff()
    {
        await h.DoctorTemplateAsync();
        await h.Schedule(h.Admin).AddResourceAsync(new AddResource { FacilityId = h.Facility, Name = "Procedure room" });
        var hold = await h.Booking(h.Reception).HoldAsync(new HoldSlot { FacilityId = h.Facility, CalendarId = h.Doctor, Start = SchedulingHarness.At(9), VisitType = VisitType.FollowUp });
        await h.Booking(h.Reception).BookAsync(new BookAppointment { HoldId = hold.Id, PatientId = h.Patient });
        await h.Booking(h.Reception).HoldAsync(new HoldSlot { FacilityId = h.Facility, CalendarId = h.Doctor, Start = SchedulingHarness.At(9, 20), VisitType = VisitType.FollowUp });

        await using var other = sql.Scheduling(Guid.NewGuid());
        Assert.Empty(await other.Templates.IgnoreQueryFilters().Where(t => t.TenantId == h.Tenant).ToListAsync()); // row-level security
        Assert.Empty(await other.Resources.IgnoreQueryFilters().Where(r => r.TenantId == h.Tenant).ToListAsync());
        Assert.Empty(await other.Appointments.IgnoreQueryFilters().Where(a => a.TenantId == h.Tenant).ToListAsync());
        Assert.Empty(await other.SlotHolds.IgnoreQueryFilters().Where(x => x.TenantId == h.Tenant).ToListAsync());
        Assert.Empty(await other.StatusEvents.IgnoreQueryFilters().Where(x => x.TenantId == h.Tenant).ToListAsync());
        Assert.Empty(await other.Outbox.IgnoreQueryFilters().Where(x => x.TenantId == h.Tenant).ToListAsync());
        Assert.Empty(await other.Database.SqlQuery<Guid>($"SELECT TenantId AS Value FROM scheduling.Appointments").ToListAsync());
        await using var none = sql.Scheduling(null);
        Assert.Empty(await none.Templates.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await none.Appointments.IgnoreQueryFilters().ToListAsync());

        await using var mine = sql.Scheduling(h.Tenant);
        Assert.Equal(1, await mine.Appointments.CountAsync()); // the tenant itself sees its rows

        other.Resources.Add(new ScheduleResource { Id = Guid.NewGuid(), TenantId = h.Tenant, FacilityId = h.Facility, Name = "Injected" });
        var blocked = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
        Assert.Contains("block predicate", Assert.IsType<SqlException>(blocked.InnerException).Message, StringComparison.Ordinal);
        var move = await Assert.ThrowsAsync<SqlException>(() => mine.Database.ExecuteSqlAsync($"UPDATE scheduling.Appointments SET TenantId = {Guid.NewGuid()}"));
        Assert.Contains("block predicate", move.Message, StringComparison.Ordinal);
    }
}

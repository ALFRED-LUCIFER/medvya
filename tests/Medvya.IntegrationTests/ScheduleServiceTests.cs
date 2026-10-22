using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Domain;
using Medvya.Identity.Infrastructure;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Medvya.IntegrationTests.ScheduleRulesTests;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-011 on real SQL Server: templates per doctor and resource, the day view in facility time, role checks against the
/// membership at the facility, and both tenant isolation layers. Wired through the real module registrations. Synthetic data only.
/// </summary>
public sealed class ScheduleServiceTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    internal static readonly DateOnly Monday = new(2026, 10, 19);

    readonly Guid tenant = Guid.NewGuid(), facility = Guid.NewGuid(), otherFacility = Guid.NewGuid();
    readonly Guid doctor = Guid.NewGuid(), reception = Guid.NewGuid(), admin = Guid.NewGuid(), portalUser = Guid.NewGuid(), elsewhere = Guid.NewGuid();
    readonly List<ServiceProvider> providers = [];

    public async Task InitializeAsync()
    {
        await using (var db = sql.Context(tenant))
        {
            db.Tenants.Add(new Tenant { Id = tenant, Name = "Iyer Family Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            db.Facilities.Add(new Facility { Id = facility, TenantId = tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" });
            db.Facilities.Add(new Facility { Id = otherFacility, TenantId = tenant, Name = "Velachery (synthetic)", TimeZone = "Asia/Kolkata" });
            foreach (var (user, role, at) in new[]
            {
                (doctor, Role.SoloDoctor, facility), (reception, Role.Receptionist, facility), (admin, Role.PracticeAdmin, facility),
                (portalUser, Role.Patient, facility), (elsewhere, Role.PracticeAdmin, otherFacility),
            })
                db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = user, FacilityId = at, Role = role, Status = MembershipStatus.Active });
            await db.SaveChangesAsync();
        }
        await using var identity = sql.Identity();
        identity.Users.Add(new StaffUser { Id = doctor, Issuer = "test-idp", Subject = doctor.ToString(), DisplayName = "Dr. Arjun Rao (synthetic)" });
        await identity.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var p in providers) await p.DisposeAsync();
    }

    sealed class FixedUser(Guid id) : ICurrentUser { public Guid? UserId => id; }

    /// <summary>The scheduling service as the host builds it, for one user in one tenant.</summary>
    IScheduleService As(Guid user, Guid? inTenant = null)
    {
        var provider = new ServiceCollection()
            .AddSingleton<ICurrentTenant>(new SqlServerFixture.FixedTenant(inTenant ?? tenant))
            .AddSingleton<ICurrentUser>(new FixedUser(user))
            .AddTenancyModule(sql.ConnectionString).AddIdentityModule(sql.ConnectionString).AddSchedulingModule(sql.ConnectionString)
            .BuildServiceProvider();
        providers.Add(provider);
        return provider.CreateScope().ServiceProvider.GetRequiredService<IScheduleService>();
    }

    SaveTemplate DoctorTemplate(int slotMinutes = 20) => new()
    {
        FacilityId = facility, CalendarId = doctor, CalendarKind = CalendarKind.Doctor, SlotMinutes = slotMinutes,
        Hours = MorningAndEvening().Hours,
        Breaks = [new ScheduleBreak { Weekday = DayOfWeek.Monday, Start = new(12, 0), End = new(13, 0), Label = "Lunch break" }],
        VisitLengths = [new VisitLength { VisitType = VisitType.NewConsultation, Minutes = 40 }],
    };

    [Fact]
    public async Task ASavedTemplateGivesBookableSlotsInFacilityLocalTime()
    {
        var saved = await As(admin).SaveTemplateAsync(DoctorTemplate());
        Assert.Equal(("Dr. Arjun Rao (synthetic)", 20), (saved.Name, saved.SlotMinutes));

        var day = await As(reception).GetDayAsync(facility, Monday);
        Assert.Equal(("Asia/Kolkata", Monday), (day.TimeZone, day.Date));
        Assert.Equal((new DateTimeOffset(2026, 10, 19, 3, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 19, 14, 30, 0, TimeSpan.Zero)), (day.Start, day.End));
        var column = Assert.Single(day.Columns);
        Assert.Equal((doctor, CalendarKind.Doctor, 18), (column.CalendarId, column.CalendarKind, column.Slots.Count)); // 21 slots minus the lunch hour
        Assert.Equal([UnavailableReason.Break, UnavailableReason.NotWorking], column.Unavailable.Select(u => u.Reason));
        Assert.Equal("Lunch break", column.Unavailable[0].Label);

        var sunday = await As(reception).GetDayAsync(facility, Monday.AddDays(6));
        Assert.Equal(UnavailableReason.NotWorking, Assert.Single(Assert.Single(sunday.Columns).Unavailable).Reason); // closed all day
    }

    [Fact]
    public async Task DoctorNamesComeFromOneBatchedLookup()
    {
        await using var provider = new ServiceCollection().AddIdentityModule(sql.ConnectionString).BuildServiceProvider();
        var staff = provider.GetRequiredService<Medvya.Identity.Application.IStaffUsers>();
        var names = await staff.DisplayNamesAsync([doctor, doctor, Guid.NewGuid()]);
        Assert.Equal(new Dictionary<Guid, string> { [doctor] = "Dr. Arjun Rao (synthetic)" }, names); // unknown ids left out, duplicates once
        Assert.Empty(await staff.DisplayNamesAsync([]));
    }

    [Fact]
    public async Task AResourceIsAColumnLikeADoctor()
    {
        var room = await As(admin).AddResourceAsync(new AddResource { FacilityId = facility, Name = "  Procedure room  " });
        Assert.Equal("Procedure room", room.Name);
        var before = await As(reception).GetDayAsync(facility, Monday);
        Assert.Equal(["Dr. Arjun Rao (synthetic)", "Procedure room"], before.Columns.Select(c => c.Name));
        Assert.All(before.Columns, c => Assert.Equal(UnavailableReason.NoSchedule, Assert.Single(c.Unavailable).Reason)); // nothing set up yet

        await As(admin).SaveTemplateAsync(new SaveTemplate
        {
            FacilityId = facility, CalendarId = room.Id, CalendarKind = CalendarKind.Resource, SlotMinutes = 30,
            Hours = [new WorkingHours { Weekday = DayOfWeek.Monday, Start = new(10, 0), End = new(12, 0) }],
        });
        var resource = (await As(reception).GetDayAsync(facility, Monday)).Columns.Single(c => c.CalendarKind == CalendarKind.Resource);
        Assert.Equal(4, resource.Slots.Count);

        var duplicate = await Assert.ThrowsAsync<SchedulingValidationException>(() => As(admin).AddResourceAsync(new AddResource { FacilityId = facility, Name = "Procedure room" }));
        Assert.Equal([new ValidationError("name", "duplicate")], duplicate.Errors);
    }

    [Fact]
    public async Task ChangingATemplateReplacesTheOldOne()
    {
        await As(admin).SaveTemplateAsync(DoctorTemplate());
        await As(doctor).SaveTemplateAsync(DoctorTemplate(slotMinutes: 15) with { Breaks = [], VisitLengths = [] }); // the owner doctor configures too

        Assert.Equal(15, Assert.Single(await As(admin).ListTemplatesAsync(facility)).SlotMinutes);
        await using var db = sql.Scheduling(tenant);
        Assert.Equal(1, await db.Templates.CountAsync(t => t.CalendarId == doctor));
        Assert.Equal(28, Assert.Single((await As(reception).GetDayAsync(facility, Monday)).Columns).Slots.Count); // 16 + 12 quarter hours
    }

    [Fact]
    public async Task OnlyMembersWithTheRightRoleAtThisFacilityGetIn()
    {
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => As(reception).SaveTemplateAsync(DoctorTemplate()));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => As(reception).AddResourceAsync(new AddResource { FacilityId = facility, Name = "Room" }));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => As(portalUser).GetDayAsync(facility, Monday)); // patients use the portal
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => As(elsewhere).GetDayAsync(facility, Monday)); // admin of another facility
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => As(admin, inTenant: Guid.NewGuid()).GetDayAsync(facility, Monday)); // another tenant
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => As(Guid.NewGuid()).GetDayAsync(facility, Monday));
        Assert.Single((await As(reception).GetDayAsync(facility, Monday)).Columns);
    }

    [Fact]
    public async Task OnlyADoctorOrResourceOfTheFacilityCanHaveATemplate()
    {
        var notADoctor = await Assert.ThrowsAsync<SchedulingValidationException>(() => As(admin).SaveTemplateAsync(DoctorTemplate() with { CalendarId = reception }));
        Assert.Equal([new ValidationError("calendarId", "unknown")], notADoctor.Errors);
        var wrongKind = await Assert.ThrowsAsync<SchedulingValidationException>(() => As(admin).SaveTemplateAsync(DoctorTemplate() with { CalendarKind = CalendarKind.Resource }));
        Assert.Equal([new ValidationError("calendarId", "unknown")], wrongKind.Errors);
        var invalid = await Assert.ThrowsAsync<SchedulingValidationException>(() => As(admin).SaveTemplateAsync(DoctorTemplate(slotMinutes: 7)));
        Assert.Contains(new ValidationError("slotMinutes", "invalid"), invalid.Errors);

        await using var db = sql.Scheduling(tenant);
        Assert.False(await db.Templates.AnyAsync()); // nothing rejected was saved
    }

    [Fact]
    public async Task AnotherTenantNeverSeesTheScheduleEvenWithQueryFiltersOff()
    {
        await As(admin).SaveTemplateAsync(DoctorTemplate());
        await As(admin).AddResourceAsync(new AddResource { FacilityId = facility, Name = "Procedure room" });

        await using var other = sql.Scheduling(Guid.NewGuid());
        Assert.Empty(await other.Templates.IgnoreQueryFilters().Where(t => t.TenantId == tenant).ToListAsync()); // row-level security
        Assert.Empty(await other.Resources.IgnoreQueryFilters().Where(r => r.TenantId == tenant).ToListAsync());
        Assert.Empty(await other.Database.SqlQuery<Guid>($"SELECT TenantId AS Value FROM scheduling.Templates").ToListAsync());
        await using var none = sql.Scheduling(null);
        Assert.Empty(await none.Templates.IgnoreQueryFilters().ToListAsync());

        other.Resources.Add(new ScheduleResource { Id = Guid.NewGuid(), TenantId = tenant, FacilityId = facility, Name = "Injected" });
        var blocked = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
        Assert.Contains("block predicate", Assert.IsType<SqlException>(blocked.InnerException).Message, StringComparison.Ordinal);
    }
}

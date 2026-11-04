using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-005 on real SQL Server: profile, locations, hours, holidays and the logo; the server refuses roles without practice
/// configuration; a holiday stops new bookings on the booking path; every change keeps its before/after and an audit event.
/// </summary>
public sealed class PracticeSettingsTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program>? factory;

    public Task InitializeAsync() => h.SeedAsync();

    public async Task DisposeAsync()
    {
        await h.CloseAsync();
        if (factory is not null) await factory.DisposeAsync();
    }

    // ---- 403 from the server ----

    /// <summary>The user in X-Test-User with the role in X-Test-Role, in the test tenant (sign-in sets these claims at the facility picker).</summary>
    sealed class SignedIn(Guid tenant) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                if (ctx.Request.Headers["X-Test-User"].ToString() is { Length: > 0 } user)
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, user), new Claim("tenant_id", tenant.ToString()), new Claim("role", ctx.Request.Headers["X-Test-Role"].ToString())], "synthetic"));
                return nxt(ctx);
            });
            next(app);
        };
    }

    HttpClient Client(Role? role, Guid? user = null)
    {
        factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureServices(s => s.AddSingleton<IStartupFilter>(new SignedIn(h.Tenant)))
            .ConfigureTestServices(s => s.Replace(ServiceDescriptor.Singleton<ILogoStore>(h.Logos))));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (role is { } r)
        {
            client.DefaultRequestHeaders.Add("X-Test-User", (user ?? h.Admin).ToString());
            client.DefaultRequestHeaders.Add("X-Test-Role", r.ToString());
        }
        return client;
    }

    HttpRequestMessage[] Writes() =>
    [
        new(HttpMethod.Put, "/v1/settings/profile") { Content = JsonContent.Create(new { name = "Renamed Clinic (synthetic)" }) },
        new(HttpMethod.Post, "/v1/settings/locations") { Content = JsonContent.Create(new { name = "New site (synthetic)", timeZone = "Asia/Kolkata" }) },
        new(HttpMethod.Put, $"/v1/settings/locations/{h.Facility}") { Content = JsonContent.Create(new { name = "Renamed site (synthetic)" }) },
        new(HttpMethod.Put, $"/v1/settings/locations/{h.Facility}/hours") { Content = JsonContent.Create(Array.Empty<object>()) },
        new(HttpMethod.Post, $"/v1/settings/locations/{h.Facility}/closures") { Content = JsonContent.Create(new { date = "2026-11-01", name = "Diwali" }) },
        new(HttpMethod.Put, "/v1/settings/logo") { Content = new ByteArrayContent(Png) },
        new(HttpMethod.Get, "/v1/settings/profile"),
    ];

    [Theory]
    [InlineData(Role.Receptionist)]
    [InlineData(Role.Nurse)]
    [InlineData(Role.CareCoordinator)]
    [InlineData(Role.HospitalDoctor)]
    [InlineData(Role.Patient)]
    public async Task RolesWithoutPracticeConfigurationGet403AndNothingChanges(Role role)
    {
        using var client = Client(role, h.Reception);
        foreach (var request in Writes())
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
        await using var db = sql.Context(h.Tenant);
        Assert.Equal("Iyer Family Clinic (synthetic)", (await db.Tenants.SingleAsync()).Name);
        Assert.Empty(await db.SettingsChanges.ToListAsync());
    }

    [Fact]
    public async Task SignedOutGets401AndPracticeAdminAndOwnerDoctorAreLetThrough()
    {
        using (var anonymous = Client(null))
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/v1/settings/profile")).StatusCode);
        foreach (var role in new[] { Role.PracticeAdmin, Role.SoloDoctor })
        {
            using var client = Client(role);
            var profile = await client.GetFromJsonAsync<JsonElement>("/v1/settings/profile");
            Assert.Equal("Iyer Family Clinic (synthetic)", profile.GetProperty("name").GetString());
        }
    }

    // ---- Profile, locations, hours ----

    [Fact]
    public async Task SavingTheProfileKeepsBeforeAndAfterAndAuditsIt()
    {
        var settings = h.Settings(h.Admin);
        var saved = await settings.SaveProfileAsync(new SaveProfile
        {
            Name = "  Iyer Family Clinic Adyar (synthetic) ", Address = "12 Synthetic Road, Chennai", RegistrationNumber = "TN-0000", TaxNumber = "GST-0000",
            Phone = "+91 90000 00101", Email = "desk@clinic.example",
        });
        Assert.Equal(("Iyer Family Clinic Adyar (synthetic)", "TN-0000", false), (saved.Name, saved.RegistrationNumber, saved.HasLogo));

        await using var db = sql.Context(h.Tenant);
        var change = await db.SettingsChanges.SingleAsync();
        Assert.Equal(("profile", h.Admin), (change.Section, change.ActorUserId));
        Assert.Contains("Iyer Family Clinic (synthetic)", change.Before); // the old name
        Assert.Contains("Iyer Family Clinic Adyar (synthetic)", change.After); // the new one
        await using var audit = sql.Audit(h.Tenant);
        var events = await audit.Events.ToListAsync();
        var row = Assert.Single(events);
        Assert.Equal(("settings.profile.update", "settings", Medvya.Audit.Domain.AuditOutcome.Allowed), (row.Action, row.ResourceType, row.Outcome));
    }

    [Fact]
    public async Task InvalidProfileIsRejectedWithFieldCodesAndNothingIsSaved()
    {
        var ex = await Assert.ThrowsAsync<SettingsValidationException>(() => h.Settings(h.Admin).SaveProfileAsync(
            new SaveProfile { Name = " ", Phone = "call me", Email = "Joseph <j@example.test>", Address = new string('x', 501) }));
        Assert.Equal(["address:too_long", "email:invalid", "name:required", "phone:invalid"], ex.Errors.Select(e => $"{e.Field}:{e.Code}").Order());
        await using var db = sql.Context(h.Tenant);
        Assert.Empty(await db.SettingsChanges.ToListAsync());
    }

    [Fact]
    public async Task ALocationIsAddedAndRenamedAndAnotherTenantsIsNotFound()
    {
        var settings = h.Settings(h.Admin);
        var added = await settings.AddLocationAsync(new SaveLocation { Name = "Velachery 2 (synthetic)", Address = "4 Synthetic Street", TimeZone = "Asia/Kolkata" });
        Assert.Contains(added.Id, (await settings.ListLocationsAsync()).Select(l => l.Id));
        await using (var db = sql.Context(h.Tenant)) // the creator can work at the new site
            Assert.True(await db.Memberships.AnyAsync(m => m.FacilityId == added.Id && m.UserId == h.Admin && m.Role == Role.PracticeAdmin && m.Status == MembershipStatus.Active));

        var renamed = await settings.UpdateLocationAsync(added.Id, new SaveLocation { Name = "Velachery North (synthetic)" });
        Assert.Equal(("Velachery North (synthetic)", "Asia/Kolkata"), (renamed!.Name, renamed.TimeZone)); // the zone stays when not sent
        var bad = await Assert.ThrowsAsync<SettingsValidationException>(() => settings.AddLocationAsync(new SaveLocation { Name = "X", TimeZone = "Mars/Olympus" }));
        Assert.Equal("timeZone:invalid", $"{bad.Errors[0].Field}:{bad.Errors[0].Code}");

        var otherTenant = Guid.NewGuid();
        Assert.Null(await h.Settings(h.Admin, otherTenant).UpdateLocationAsync(h.Facility, new SaveLocation { Name = "Stolen" }));
        Assert.Null(await h.Settings(h.Admin, otherTenant).GetHoursAsync(h.Facility));
    }

    [Fact]
    public async Task OpeningHoursAreReplacedPerLocationAndOverlappingOrReversedDaysAreRejected()
    {
        var settings = h.Settings(h.Admin);
        await settings.SetHoursAsync(h.Facility, [new(DayOfWeek.Monday, new(9, 0), new(17, 0)), new(DayOfWeek.Tuesday, new(9, 0), new(13, 0))]);
        var again = await settings.SetHoursAsync(h.Facility, [new(DayOfWeek.Saturday, new(10, 0), new(14, 0))]);
        Assert.Equal([DayOfWeek.Saturday], again!.Select(d => d.Weekday));
        Assert.Equal([DayOfWeek.Saturday], (await settings.GetHoursAsync(h.Facility))!.Select(d => d.Weekday));
        Assert.Empty((await settings.GetHoursAsync(h.OtherFacility))!); // per location

        var ex = await Assert.ThrowsAsync<SettingsValidationException>(() =>
            settings.SetHoursAsync(h.Facility, [new(DayOfWeek.Monday, new(17, 0), new(9, 0)), new(DayOfWeek.Tuesday, new(9, 0), new(10, 0)), new(DayOfWeek.Tuesday, new(11, 0), new(12, 0))]));
        Assert.Equal(["hours[0]:invalid", "hours[2]:duplicate"], ex.Errors.Select(e => $"{e.Field}:{e.Code}"));
    }

    // ---- Logo ----

    [Fact]
    public async Task OnlyPngAndJpegWithinTheSizeLimitAreStoredAndServedByExpiringLink()
    {
        var settings = h.Settings(h.Admin);
        var svg = "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray();
        foreach (var (bad, code) in new[] { (svg, "type"), ([0x47, 0x49, 0x46, 0x38, 0x39, 0x61], "type"), (Png.Concat(new byte[LogoImage.MaxBytes]).ToArray(), "too_large"), ([], "too_large") })
            Assert.Equal($"logo:{code}", (await Assert.ThrowsAsync<SettingsValidationException>(() => settings.SetLogoAsync(bad))).Errors.Select(e => $"{e.Field}:{e.Code}").Single());
        Assert.Empty(h.Logos.Stored);
        Assert.Null(await settings.LogoLinkAsync(TimeSpan.FromMinutes(5)));

        await settings.SetLogoAsync(Png);
        var (key, stored) = Assert.Single(h.Logos.Stored);
        Assert.Equal("image/png", stored.ContentType); // from the bytes
        Assert.StartsWith($"{h.Tenant}/logo/", key);
        Assert.DoesNotContain("png", key, StringComparison.OrdinalIgnoreCase); // no file names in blob paths
        var link = await settings.LogoLinkAsync(TimeSpan.FromMinutes(5));
        Assert.Contains("se=300", link!.Query);
        Assert.True((await settings.GetProfileAsync()).HasLogo);

        await settings.SetLogoAsync([0xFF, 0xD8, 0xFF, 0xE0, 0, 0]); // replacing deletes the old object
        Assert.Equal("image/jpeg", Assert.Single(h.Logos.Stored).Value.ContentType);
        Assert.True(await settings.RemoveLogoAsync());
        Assert.Empty(h.Logos.Stored);
    }

    // ---- Holidays ----

    [Fact]
    public async Task AHolidayStopsNewBookingsAndMovesAndListsVisitsAlreadyBookedThatDay()
    {
        await h.DoctorTemplateAsync();
        var booked = await h.BookAsync(9); // Monday 09:00, before the holiday exists
        var tuesday = SchedulingHarness.Monday.AddDays(1);
        var closure = await h.Settings(h.Admin).AddClosureAsync(new AddClosure { FacilityId = h.Facility, Date = SchedulingHarness.Monday, Name = "Synthetic holiday" });
        Assert.NotNull(closure);

        var reception = h.Booking(h.Reception);
        var hold = await Assert.ThrowsAsync<SlotUnavailableException>(() => reception.HoldAsync(Hold(SchedulingHarness.At(10))));
        Assert.Equal("closed", hold.Code);
        // Moving a visit onto the holiday is a new booking too.
        var other = await h.BookAsync(0, start: SchedulingHarness.At(9).AddDays(1));
        var move = await Assert.ThrowsAsync<SlotUnavailableException>(() =>
            reception.RescheduleAsync(other.Id, new RescheduleAppointment { CalendarId = h.Doctor, Start = SchedulingHarness.At(11) }));
        Assert.Equal("closed", move.Code);
        // Another day is untouched.
        await reception.HoldAsync(Hold(SchedulingHarness.At(10).AddDays(1)));

        var day = await h.Schedule(h.Reception).GetDayAsync(h.Facility, SchedulingHarness.Monday);
        var column = day.Columns.Single(c => c.CalendarId == h.Doctor);
        Assert.Empty(column.Slots);
        var block = Assert.Single(column.Unavailable);
        Assert.Equal((UnavailableReason.Closed, "Synthetic holiday"), (block.Reason, block.Label));
        Assert.NotEmpty((await h.Schedule(h.Reception).GetDayAsync(h.Facility, tuesday)).Columns.Single(c => c.CalendarId == h.Doctor).Slots);

        var toMove = Assert.Single(await h.Schedule(h.Admin).AppointmentsOnAsync(h.Facility, SchedulingHarness.Monday));
        Assert.Equal((booked.Reference, "Dr. Arjun Rao (synthetic)"), (toMove.Reference, toMove.CalendarName));
        await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => h.Schedule(h.Nurse).AppointmentsOnAsync(h.Facility, SchedulingHarness.Monday));

        // Removing the holiday opens the day again.
        Assert.True(await h.Settings(h.Admin).RemoveClosureAsync(closure!.Id));
        await reception.HoldAsync(Hold(SchedulingHarness.At(10)));
    }

    [Fact]
    public async Task AHolidayIsPerLocationUniquePerDateAndNeedsAName()
    {
        var settings = h.Settings(h.Admin);
        await settings.AddClosureAsync(new AddClosure { FacilityId = h.Facility, Date = new(2026, 11, 8), Name = "Diwali" });
        var duplicate = await Assert.ThrowsAsync<SettingsValidationException>(() => settings.AddClosureAsync(new AddClosure { FacilityId = h.Facility, Date = new(2026, 11, 8), Name = "Again" }));
        Assert.Equal("date:duplicate", $"{duplicate.Errors[0].Field}:{duplicate.Errors[0].Code}");
        await settings.AddClosureAsync(new AddClosure { FacilityId = h.OtherFacility, Date = new(2026, 11, 8), Name = "Diwali" }); // the other location is its own
        Assert.Equal("name:required", (await Assert.ThrowsAsync<SettingsValidationException>(() =>
            settings.AddClosureAsync(new AddClosure { FacilityId = h.Facility, Date = new(2026, 11, 9), Name = " " }))).Errors.Select(e => $"{e.Field}:{e.Code}").Single());
        Assert.Null(await settings.AddClosureAsync(new AddClosure { FacilityId = Guid.NewGuid(), Date = new(2026, 11, 9), Name = "Nowhere" }));
        Assert.Single((await settings.ListClosuresAsync(h.Facility))!);

        await using var audit = sql.Audit(h.Tenant);
        Assert.Equal(["settings.closure.create", "settings.closure.create"], await audit.Events.Select(e => e.Action).ToListAsync());
    }

    HoldSlot Hold(DateTimeOffset start) => new() { FacilityId = h.Facility, CalendarId = h.Doctor, Start = start, VisitType = Scheduling.Domain.VisitType.FollowUp };
}

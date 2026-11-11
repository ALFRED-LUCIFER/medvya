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
public sealed class PracticeSettingsTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    static readonly byte[] Png = TestImages.Png;
    const string Gstin = "27AAAAA0000A1Z5"; // format only (synthetic)
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program>? factory;

    public Task InitializeAsync() => h.SeedAsync();

    public async Task DisposeAsync()
    {
        await h.CloseAsync();
        if (factory is not null) await factory.DisposeAsync();
    }

    // ---- 403 from the server ----

    /// <summary>The user in X-Test-User, in the test tenant and facility (sign-in sets these claims at the facility picker).</summary>
    sealed class SignedIn(Guid tenant, Guid facility) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                if (ctx.Request.Headers["X-Test-User"].ToString() is { Length: > 0 } user)
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, user), new Claim("tenant_id", tenant.ToString()), new Claim("facility_id", facility.ToString())], "synthetic"));
                return nxt(ctx);
            });
            next(app);
        };
    }

    HttpClient Client(Role? role, Guid? user = null)
    {
        factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureServices(s => s.AddSingleton<IStartupFilter>(new SignedIn(h.Tenant, h.Facility)))
            .ConfigureTestServices(s => s.Replace(ServiceDescriptor.Singleton<ILogoStore>(h.Logos))));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (role is { } r)
        {
            client.DefaultRequestHeaders.Add("X-Test-User", (user ?? h.ByRole[role!.Value]).ToString());
        }
        return client;
    }

    HttpRequestMessage[] Writes() =>
    [
        new(HttpMethod.Put, "/v1/settings/profile") { Content = JsonContent.Create(new { name = "Renamed Clinic (synthetic)" }) },
        new(HttpMethod.Post, "/v1/settings/locations") { Content = JsonContent.Create(new { name = "New site (synthetic)", timeZone = "Asia/Kolkata", countryCode = "IN", taxNumber = Gstin, certificateName = "reg.png", certificate = Png }) },
        new(HttpMethod.Put, $"/v1/settings/locations/{h.Facility}") { Content = JsonContent.Create(new { name = "Renamed site (synthetic)" }) },
        new(HttpMethod.Put, $"/v1/settings/locations/{h.Facility}/hours") { Content = JsonContent.Create(Array.Empty<object>()) },
        new(HttpMethod.Post, $"/v1/settings/locations/{h.Facility}/closures") { Content = JsonContent.Create(new { date = "2026-11-01", name = "Diwali" }) },
        new(HttpMethod.Put, "/v1/settings/logo") { Content = new ByteArrayContent(Png) },
        new(HttpMethod.Get, "/v1/settings/profile"),
        // MDV-P1-006
        new(HttpMethod.Put, "/v1/settings/notifications") { Content = JsonContent.Create(new { confirmation = false, reminder24h = false, reminder2h = false, changes = false, email = false, sms = false, quietStart = "21:00:00", quietEnd = "08:00:00" }) },
        new(HttpMethod.Get, "/v1/settings/notifications"),
        new(HttpMethod.Get, "/v1/settings/notifications/templates?language=en"),
    ];

    [Theory]
    [InlineData(Role.Receptionist)]
    [InlineData(Role.Nurse)]
    [InlineData(Role.CareCoordinator)]
    [InlineData(Role.HospitalDoctor)]
    [InlineData(Role.Patient)]
    public async Task RolesWithoutPracticeConfigurationGet403AndNothingChanges(Role role)
    {
        using var client = Client(role);
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

    [Fact]
    public async Task PracticeAdminSavesNotificationPreferencesOverHttpAndSeesThemAgain()
    {
        using var client = Client(Role.PracticeAdmin);
        var body = new { confirmation = true, reminder24h = false, reminder2h = true, changes = true, email = true, sms = false, quietStart = "20:00:00", quietEnd = "09:00:00" };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/v1/settings/notifications", body)).StatusCode);
        var saved = await client.GetFromJsonAsync<JsonElement>("/v1/settings/notifications");
        Assert.Equal((false, false, "20:00:00"), (saved.GetProperty("reminder24h").GetBoolean(), saved.GetProperty("sms").GetBoolean(), saved.GetProperty("quietStart").GetString()));
        Assert.Equal(7, (await client.GetFromJsonAsync<JsonElement>("/v1/settings/notifications/templates?language=hi")).GetArrayLength());
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

    /// <summary>#889: two administrators open the profile, both save. The second sends a version the first already replaced, so it is refused.</summary>
    [Fact]
    public async Task ASecondAdministratorSavingFromAStaleProfileIsRefusedAndNothingIsOverwritten()
    {
        var settings = h.Settings(h.Admin);
        var loaded = await settings.GetProfileAsync();
        var first = await settings.SaveProfileAsync(new SaveProfile { Name = loaded.Name, Phone = "+91 90000 00201", Version = loaded.Version });
        Assert.NotEqual(loaded.Version, first.Version);
        Assert.Equal(first.Version, (await settings.GetProfileAsync()).Version); // what the save returned is what the next read shows

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => h.Settings(h.Admin).SaveProfileAsync(new SaveProfile { Name = loaded.Name, Phone = "+91 90000 00202", Version = loaded.Version }));
        Assert.Equal("+91 90000 00201", (await settings.GetProfileAsync()).Phone);
        var again = await settings.SaveProfileAsync(new SaveProfile { Name = loaded.Name, Phone = "+91 90000 00202", Version = first.Version }); // reloaded, saves
        Assert.Equal("+91 90000 00202", again.Phone);
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
        var added = await settings.AddLocationAsync(Branch());
        Assert.Contains(added.Id, (await settings.ListLocationsAsync()).Select(l => l.Id));
        await using (var db = sql.Context(h.Tenant)) // the creator can work at the new site
            Assert.True(await db.Memberships.AnyAsync(m => m.FacilityId == added.Id && m.UserId == h.Admin && m.Role == Role.PracticeAdmin && m.Status == MembershipStatus.Active));

        var renamed = await settings.UpdateLocationAsync(added.Id, new SaveLocation { Name = "Velachery North (synthetic)" });
        Assert.Equal(("Velachery North (synthetic)", "Asia/Kolkata"), (renamed!.Name, renamed.TimeZone)); // the zone stays when not sent
        var bad = await Assert.ThrowsAsync<SettingsValidationException>(() => settings.AddLocationAsync(Branch() with { Name = "X", TimeZone = "Mars/Olympus" }));
        Assert.Equal("timeZone:invalid", $"{bad.Errors[0].Field}:{bad.Errors[0].Code}");

        var otherTenant = Guid.NewGuid();
        Assert.Null(await h.Settings(h.Admin, otherTenant).UpdateLocationAsync(h.Facility, new SaveLocation { Name = "Stolen" }));
        Assert.Null(await h.Settings(h.Admin, otherTenant).GetHoursAsync(h.Facility));
    }

    [Theory]
    [InlineData(OrganisationState.Draft, true)]
    [InlineData(OrganisationState.Submitted, true)]
    [InlineData(OrganisationState.NeedsChanges, true)]
    [InlineData(OrganisationState.Rejected, true)]
    [InlineData(OrganisationState.Approved, true)]
    [InlineData(OrganisationState.Suspended, false)]
    public async Task ALocationCanBeAddedInSetUpStatesAndNotWhenSuspended(OrganisationState state, bool allowed)
    {
        await using (var db = sql.Context(h.Tenant)) await db.Tenants.ExecuteUpdateAsync(s => s.SetProperty(t => t.State, state));
        await using var before = sql.Context(h.Tenant);
        var sites = await before.Facilities.CountAsync();
        var add = () => h.Settings(h.Admin).AddLocationAsync(Branch());
        if (allowed) { await add(); return; }
        Assert.Equal("state:suspended", $"{(await Assert.ThrowsAsync<SettingsValidationException>(add)).Errors[0].Field}:suspended");
        await using var check = sql.Context(h.Tenant);
        Assert.Equal(sites, await check.Facilities.CountAsync()); // nothing stored
    }

    static AddLocation Branch() => new()
    {
        Name = "Velachery 2 (synthetic)", Address = "4 Synthetic Street", TimeZone = "Asia/Kolkata", CountryCode = "IN", TaxNumber = Gstin, CertificateName = "reg.png", Certificate = Png,
    };

    [Fact]
    public async Task AddedLocationKeepsItsCertificatePrivatelyAndIsAuditedAndHiddenFromOtherTenants()
    {
        var added = await h.Settings(h.Admin).AddLocationAsync(Branch());
        await using (var db = sql.Context(h.Tenant))
        {
            var site = await db.Facilities.SingleAsync(f => f.Id == added.Id);
            Assert.Equal((Gstin, "reg.png"), (site.TaxNumber, site.CertificateName));
            Assert.Equal(Png, h.Logos.Stored[site.CertificateKey!].Content);
            Assert.StartsWith($"{h.Tenant}/location-certificate/", site.CertificateKey); // random key, no file name
        }
        await using var audit = sql.Audit(h.Tenant);
        var row = Assert.Single(await audit.Events.ToListAsync());
        Assert.Equal(("settings.location.create", h.Admin, added.Id), (row.Action, row.ActorUserId, row.ResourceId));
        Assert.DoesNotContain(added.Id, (await h.Settings(h.Admin, Guid.NewGuid()).ListLocationsAsync()).Select(l => l.Id));
    }

    [Fact]
    public async Task LocationInAnotherCountryOrWithBadTaxOrCertificateIsRefusedAndNothingIsStored()
    {
        var settings = h.Settings(h.Admin);
        async Task<string[]> Refused(AddLocation command) =>
            (await Assert.ThrowsAsync<SettingsValidationException>(() => settings.AddLocationAsync(command))).Errors.Select(e => $"{e.Field}:{e.Code}").Order().ToArray();
        Assert.Equal(["countryCode:mismatch"], await Refused(Branch() with { CountryCode = "AE" }));
        Assert.Equal(["countryCode:required"], await Refused(Branch() with { CountryCode = null }));
        Assert.Equal(["taxNumber:format"], await Refused(Branch() with { TaxNumber = "not-a-gstin" }));
        Assert.Equal(["taxNumber:required"], await Refused(Branch() with { TaxNumber = " " }));
        Assert.Equal(["certificate:required"], await Refused(Branch() with { Certificate = null }));
        Assert.Equal(["certificate:type"], await Refused(Branch() with { Certificate = "<svg onload=x>"u8.ToArray() }));
        Assert.Equal(["certificate:too_large"], await Refused(Branch() with { Certificate = [.. "%PDF-"u8, .. new byte[LocationCertificate.MaxBytes]] }));
        Assert.Empty(h.Logos.Stored);
        await using var db = sql.Context(h.Tenant);
        Assert.DoesNotContain(await db.Facilities.ToListAsync(), f => f.CertificateKey is not null);
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

        await settings.SetLogoAsync(TestImages.Jpeg); // replacing deletes the old object
        Assert.Equal("image/png", Assert.Single(h.Logos.Stored).Value.ContentType); // stored as a re-encoded PNG
        Assert.True(await settings.RemoveLogoAsync());
        Assert.Empty(h.Logos.Stored);
    }

    [Fact]
    public async Task OnlyThePixelsOfALogoAreStoredAndAFakeOrHugeImageIsRefused()
    {
        // #211, owner decision 7 Nov: re-encoding instead of a malware scan. A payload appended to a valid image never reaches storage.
        var settings = h.Settings(h.Admin);
        var payload = "<script>alert('x')</script>MZ-not-a-picture"u8.ToArray();
        await settings.SetLogoAsync([.. Png, .. payload]);
        var stored = Assert.Single(h.Logos.Stored).Value;
        Assert.Equal("image/png", stored.ContentType);
        Assert.Equal(-1, stored.Content.AsSpan().IndexOf(payload.AsSpan(0, 8)));
        using (var decoded = SkiaSharp.SKBitmap.Decode(stored.Content)) Assert.Equal((4, 4), (decoded.Width, decoded.Height));

        // A PNG signature with nothing decodable behind it, and an image wider than the limit, are refused and nothing changes.
        foreach (var bad in new[] { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0], TestImages.Make(Medvya.Tenancy.Infrastructure.CleanImage.MaxSide + 1, 1, SkiaSharp.SKEncodedImageFormat.Png) })
            Assert.Equal("logo:type", (await Assert.ThrowsAsync<SettingsValidationException>(() => settings.SetLogoAsync(bad))).Errors.Select(e => $"{e.Field}:{e.Code}").Single());
        Assert.Single(h.Logos.Stored);
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

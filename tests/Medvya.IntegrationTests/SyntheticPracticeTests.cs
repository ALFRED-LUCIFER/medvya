using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Medvya.Scheduling.Domain;
using Medvya.SyntheticData;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Medvya.IntegrationTests;

/// <summary>Runs only when MEDVYA_PRACTICE_CONNECTION names an already seeded database (the medvya_app login); see docs/05-delivery/load-testing.md.</summary>
public sealed class PracticeLatencyFactAttribute : FactAttribute
{
    public PracticeLatencyFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MEDVYA_PRACTICE_CONNECTION"))) Skip = "set MEDVYA_PRACTICE_CONNECTION to measure a seeded practice"; }
}

/// <summary>The multi-doctor practice seed: complete cast, verified and pending licences, idempotent, refused for production-looking names, schedule with open slots.</summary>
public sealed class SyntheticPracticeTests(PostgresFixture sql, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    const string Practice = "Practice Seed Test Clinic";
    static readonly Regex IndiaLicence = new(@"^[A-Za-z0-9][A-Za-z0-9/\-]{2,19}$"); // the India licence format of OnboardingRules

    [Fact]
    public async Task SeedsTheWholeCastOnceAndASecondRunAddsNothing()
    {
        var first = await SyntheticPractice.SeedAsync(sql.ConnectionString, "Development", Practice, 200, 100_000);
        Assert.Equal((200, 8, 8, 3, 1, 3), (first.Patients, first.Doctors, first.Nurses, first.Receptionists, first.Admins, first.Rooms));
        var again = await SyntheticPractice.SeedAsync(sql.ConnectionString, "Development", Practice, 200, 100_000);
        Assert.Equal(0, again.Patients);
        Assert.Equal(0, again.Visits);
        Assert.Equal((8, 8, 3, 1, 3), (again.Doctors, again.Nurses, again.Receptionists, again.Admins, again.Rooms));

        await using var db = sql.Context(first.Tenant);
        Assert.Equal(20, await db.Memberships.CountAsync());
        var licences = await db.DoctorLicences.OrderBy(l => l.Number).ToListAsync();
        Assert.Equal(8, licences.Select(l => l.Number).Distinct().Count());
        Assert.All(licences, l => Assert.Matches(IndiaLicence, l.Number));
        Assert.Equal(6, licences.Count(l => l.VerifiedAt is not null && l.VerifiedBy is not null));
        Assert.Equal(2, licences.Count(l => l.VerifiedAt is null && l.RejectedAt is null)); // "Verification pending"
        var profiles = await db.DoctorProfiles.ToListAsync();
        Assert.Equal(2, profiles.Count(p => p.Specialty == "General Practice"));
        Assert.All(profiles, p => Assert.EndsWith(SyntheticSeed.Marker, p.FullName, StringComparison.Ordinal));
        await using var identity = sql.Identity();
        var userIds = await db.Memberships.Select(m => m.UserId!.Value).ToListAsync();
        Assert.Equal(20, await identity.Users.CountAsync(u => userIds.Contains(u.Id)));

        await using var sched = sql.Scheduling(first.Tenant);
        Assert.Equal(11, await sched.Templates.CountAsync()); // 8 doctors and 3 rooms, each with weekly hours
        Assert.Equal(first.Visits, await sched.Appointments.CountAsync());
        Assert.Equal(await sched.Appointments.CountAsync(a => !a.Unscheduled), (await sched.ScheduleSlots.Select(s => s.AppointmentId).Distinct().CountAsync()));

        // Another tenant sees none of it (row-level security).
        await using var other = sql.Scheduling(Guid.NewGuid());
        Assert.Empty(await other.Appointments.ToListAsync());
    }

    [Fact]
    public async Task ProductionLookingNamesAndEnvironmentsAreRefused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => SyntheticPractice.SeedAsync(sql.ConnectionString, "Development", "Acme Pilot Clinic", 10, 100_000));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SyntheticPractice.SeedAsync(sql.ConnectionString, "Production", Practice + " Two", 10, 100_000));
    }

    [Fact]
    public async Task TheScheduleShowsEveryDoctorWithOpenSlotsAndTheBoardTheQueue()
    {
        var seeded = await SyntheticPractice.SeedAsync(sql.ConnectionString, "Development", Practice + " Api", 200, 100_000);
        await using var db = sql.Context(seeded.Tenant);
        var reception = (await db.Memberships.Where(m => m.Role == Role.Receptionist).Select(m => m.UserId!.Value).ToListAsync())[0];
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s => PatientEndpointTests.SignInFromHeaders(s, seeded.Tenant)));
        using var desk = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        desk.DefaultRequestHeaders.Add("X-Test-User", reception.ToString());
        desk.DefaultRequestHeaders.Add("X-Test-Facility", seeded.Facility.ToString());

        var ist = DateOnly.FromDateTime((DateTimeOffset.UtcNow + TimeSpan.FromHours(5.5)).DateTime);
        var monday = ist.AddDays(((int)DayOfWeek.Monday - (int)ist.DayOfWeek + 7) % 7 + (ist.DayOfWeek == DayOfWeek.Monday ? 7 : 0));
        var day = await desk.GetFromJsonAsync<JsonElement>(new Uri(FormattableString.Invariant($"/v1/schedule?facilityId={seeded.Facility}&date={monday:yyyy-MM-dd}"), UriKind.Relative));
        var columns = day.GetProperty("columns").EnumerateArray().Where(c => c.GetProperty("calendarKind").GetString() == "Doctor").ToList();
        Assert.Equal(8, columns.Count);
        // Monday: everyone but the dermatologist works; each of those has open slots, among them both general practitioners.
        var open = columns.Where(c => c.GetProperty("slots").GetArrayLength() > 0).ToList();
        Assert.Equal(7, open.Count);
        Assert.All(columns.Where(c => c.GetProperty("name").GetString()!.StartsWith("Dr. Anil", StringComparison.Ordinal) || c.GetProperty("name").GetString()!.StartsWith("Dr. Suma", StringComparison.Ordinal)),
            c => Assert.True(c.GetProperty("slots").GetArrayLength() > 8));
        Assert.Contains(open, c => c.GetProperty("slotMinutes").GetInt32() == 15);
        Assert.Contains(open, c => c.GetProperty("slotMinutes").GetInt32() == 30);

        var board = await desk.GetFromJsonAsync<JsonElement>(new Uri(FormattableString.Invariant($"/v1/front-desk/board?facilityId={seeded.Facility}"), UriKind.Relative));
        var cards = board.GetProperty("cards").GetArrayLength();
        if (ist.DayOfWeek == DayOfWeek.Sunday) Assert.Equal(0, cards);
        else Assert.True(cards >= 16, $"{cards} cards"); // walk-ins and checked-in visits of the doctors working today
    }

    /// <summary>Measures the practice seeded in MEDVYA_PRACTICE_CONNECTION through the real API (a signed-in receptionist): p50 and p95 over 30 calls each.</summary>
    [PracticeLatencyFact]
    public async Task LatencyAtScale()
    {
        var connection = Environment.GetEnvironmentVariable("MEDVYA_PRACTICE_CONNECTION")!;
        var practice = Environment.GetEnvironmentVariable("MEDVYA_PRACTICE_NAME") ?? SyntheticPractice.DefaultPractice;
        var (tenant, facility) = SyntheticPatients.Ids(practice);
        var reception = Guid.Empty;
        await using (var members = new Medvya.Tenancy.Infrastructure.Persistence.TenancyDbContext(
            new DbContextOptionsBuilder<Medvya.Tenancy.Infrastructure.Persistence.TenancyDbContext>().UseNpgsql(Medvya.BuildingBlocks.Infrastructure.PostgresDataSources.For(connection))
                .AddInterceptors(new Medvya.BuildingBlocks.Infrastructure.TenantSessionInterceptor(new PostgresFixture.FixedTenant(tenant))).Options, new PostgresFixture.FixedTenant(tenant)))
            reception = (await members.Memberships.Where(m => m.Role == Role.Receptionist).Select(m => m.UserId!.Value).ToListAsync())[0];
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", connection)
            .UseSetting("RateLimiting:PatientSearchPerMinute", "100000")
            .ConfigureTestServices(s => PatientEndpointTests.SignInFromHeaders(s, tenant)));
        using var desk = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        desk.DefaultRequestHeaders.Add("X-Test-User", reception.ToString());
        desk.DefaultRequestHeaders.Add("X-Test-Facility", facility.ToString());

        var ist = DateOnly.FromDateTime((DateTimeOffset.UtcNow + TimeSpan.FromHours(5.5)).DateTime);
        string[] queries = ["kavit", "mohamed", "krsihnan", "sreen kumar", "112345", "+91 90 0011 2345"];
        async Task<long> Time(Func<Task<HttpResponseMessage>> call)
        {
            var clock = Stopwatch.StartNew();
            using var res = await call();
            await res.Content.ReadAsStringAsync();
            var ms = clock.ElapsedMilliseconds;
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return ms;
        }
        void Report(string what, List<long> ms)
        {
            ms.Sort();
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{what}: p50 {ms[ms.Count / 2]} ms, p95 {ms[(int)Math.Ceiling(ms.Count * 0.95) - 1]} ms, max {ms[^1]} ms over {ms.Count}"));
        }
        var calls = new (string Name, Func<int, Task<HttpResponseMessage>> Call)[]
        {
            ("patient search", i => desk.PostAsJsonAsync(new Uri("/v1/patients/search", UriKind.Relative), new { query = queries[i % queries.Length], filter = "All" })),
            ("booking list (day schedule)", i => desk.GetAsync(new Uri(FormattableString.Invariant($"/v1/schedule?facilityId={facility}&date={ist.AddDays(i % 5):yyyy-MM-dd}"), UriKind.Relative))),
            ("board", _ => desk.GetAsync(new Uri(FormattableString.Invariant($"/v1/front-desk/board?facilityId={facility}"), UriKind.Relative))),
        };
        foreach (var (name, call) in calls)
        {
            for (var i = 0; i < 3; i++) await Time(() => call(i)); // warm the plan cache and pool
            var timings = new List<long>();
            for (var i = 0; i < 30; i++) timings.Add(await Time(() => call(i)));
            Report(name, timings);
        }
    }
}

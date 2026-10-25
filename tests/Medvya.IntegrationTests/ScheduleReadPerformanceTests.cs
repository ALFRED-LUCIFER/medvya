using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using Medvya.Identity.Domain;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-023: the day (and list) and week reads stay inside the read budget, p95 under 300 ms, with a year of a busy clinic on
/// real SQL Server: three doctors fully booked Monday to Friday for 26 weeks either side of today (16,380 visits), the same again
/// at the tenant's other facility, and 2,000 patients. Measured through the endpoints, so the capability check, the audit write,
/// the (facility, start) index seek and the batched patient-name lookup are all inside the timing.
/// </summary>
[Collection(nameof(RunsAlone))]
public sealed class ScheduleReadPerformanceTests(SqlServerFixture sql, ITestOutputHelper output) : IClassFixture<SqlServerFixture>
{
    const int Weeks = 26, PatientCount = 2_000;
    static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    [Fact]
    public async Task DayAndWeekReadsStayUnder300MillisecondsAtP95()
    {
        var h = new SchedulingHarness(sql);
        await h.SeedAsync();
        var third = Guid.NewGuid();
        await using (var db = sql.Context(h.Tenant))
        {
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = h.Tenant, UserId = third, FacilityId = h.Facility, Role = Role.SoloDoctor, Status = MembershipStatus.Active });
            await db.SaveChangesAsync();
        }
        await using (var identity = sql.Identity())
        {
            identity.Users.Add(new StaffUser { Id = third, Issuer = "test-idp", Subject = third.ToString(), DisplayName = "Dr. Fatima Haddad (synthetic)" });
            await identity.SaveChangesAsync();
        }
        Guid[] doctors = [h.Doctor, h.SecondDoctor, third];
        foreach (var d in doctors) await h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate() with { CalendarId = d });

        var today = ScheduleRulesLocalToday();
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var patients = await SeedPatientsAsync(h.Tenant, h.Facility);
        await SeedVisitsAsync(h.Tenant, h.Facility, doctors, patients, monday);
        await SeedVisitsAsync(h.Tenant, h.OtherFacility, [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], patients, monday); // another facility's load

        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s => PatientEndpointTests.SignInFromHeaders(s, h.Tenant)));
        using var desk = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        desk.DefaultRequestHeaders.Add("X-Test-User", h.Reception.ToString());
        desk.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());

        async Task<long> Get(string path)
        {
            var clock = Stopwatch.StartNew();
            using var res = await desk.GetAsync(new Uri(path, UriKind.Relative));
            var body = await res.Content.ReadAsStringAsync();
            var ms = clock.ElapsedMilliseconds;
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Contains("Synthetic Patient", body, StringComparison.Ordinal); // a busy day or week, with names
            return ms;
        }
        string Day(int offset) => FormattableString.Invariant($"/v1/schedule?facilityId={h.Facility}&date={monday.AddDays(offset):yyyy-MM-dd}");
        string Week(int i) => FormattableString.Invariant($"/v1/schedule/week?facilityId={h.Facility}&calendarId={doctors[i % 3]}&date={monday.AddDays(7 * (i % 9 - 4)):yyyy-MM-dd}");

        for (var i = 0; i < 3; i++) { await Get(Day(i)); await Get(Week(i)); } // warm the plan cache and the connection pool
        var days = new List<long>();
        var weeks = new List<long>();
        for (var i = 0; i < 40; i++)
        {
            days.Add(await Get(Day(7 * (i % 9 - 4) + i % 5))); // weekdays from four weeks back to four ahead
            weeks.Add(await Get(Week(i)));
        }
        AssertP95(days, "day reads (day and list views, three doctors)");
        AssertP95(weeks, "week reads (one doctor)");
        await h.CloseAsync();
    }

    static DateOnly ScheduleRulesLocalToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist).DateTime);

    void AssertP95(List<long> timings, string what)
    {
        timings.Sort();
        var p95 = timings[(int)Math.Ceiling(timings.Count * 0.95) - 1];
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{what}: p50 {timings[timings.Count / 2]} ms, p95 {p95} ms, max {timings[^1]} ms over {timings.Count} ({string.Join(" ", timings)})"));
        Assert.True(p95 < 300, string.Create(CultureInfo.InvariantCulture, $"p95 {p95} ms over {timings.Count} {what} (max {timings[^1]} ms)"));
    }

    async Task<Guid[]> SeedPatientsAsync(Guid tenant, Guid facility)
    {
        var table = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
        {
            ("Id", typeof(Guid)), ("TenantId", typeof(Guid)), ("FacilityId", typeof(Guid)), ("Mrn", typeof(string)), ("CountryCode", typeof(string)),
            ("FullName", typeof(string)), ("DateOfBirth", typeof(DateTime)), ("DateOfBirthEstimated", typeof(bool)), ("SexAtBirth", typeof(string)),
            ("Mobile", typeof(string)), ("Status", typeof(string)), ("RegisteredAt", typeof(DateTimeOffset)), ("ChangedAt", typeof(DateTimeOffset)),
        }) table.Columns.Add(name, type);
        var ids = new Guid[PatientCount];
        for (var n = 0; n < PatientCount; n++)
        {
            ids[n] = Guid.CreateVersion7();
            table.Rows.Add(ids[n], tenant, facility, (100_000 + n).ToString(CultureInfo.InvariantCulture), "IN", $"Synthetic Patient {n}", new DateTime(1950 + n % 60, 1 + n % 12, 1 + n % 28),
                false, n % 2 == 0 ? "Female" : "Male", $"+9191{n:D8}", "Complete", MondayMorning, MondayMorning);
        }
        await using var conn = await BulkAsync(tenant, "patients.Patients", table);
        await using var stats = new SqlCommand("UPDATE STATISTICS patients.Patients", conn);
        await stats.ExecuteNonQueryAsync();
        return ids;
    }

    /// <summary>Every 20-minute slot of 09:00–13:00 and 17:00–20:00 IST, Monday to Friday, booked for each calendar.</summary>
    async Task SeedVisitsAsync(Guid tenant, Guid facility, Guid[] calendars, Guid[] patients, DateOnly monday)
    {
        var table = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
        {
            ("Id", typeof(Guid)), ("TenantId", typeof(Guid)), ("FacilityId", typeof(Guid)), ("CalendarId", typeof(Guid)), ("CalendarKind", typeof(string)),
            ("PatientId", typeof(Guid)), ("VisitType", typeof(string)), ("Start", typeof(DateTimeOffset)), ("End", typeof(DateTimeOffset)),
            ("State", typeof(string)), ("ReasonCategory", typeof(string)), ("Channel", typeof(string)), ("BookedAt", typeof(DateTimeOffset)), ("ChangedAt", typeof(DateTimeOffset)),
        }) table.Columns.Add(name, type);
        var times = Enumerable.Range(0, 12).Select(i => new TimeOnly(9, 0).AddMinutes(20 * i)).Concat(Enumerable.Range(0, 9).Select(i => new TimeOnly(17, 0).AddMinutes(20 * i))).ToList();
        var n = 0;
        for (var day = monday.AddDays(-7 * Weeks); day < monday.AddDays(7 * Weeks); day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            foreach (var calendar in calendars)
                foreach (var t in times)
                {
                    var start = new DateTimeOffset(day.ToDateTime(t), TimeSpan.FromHours(5.5)).ToUniversalTime();
                    var state = n % 17 == 0 ? "Cancelled" : n % 23 == 0 ? "NoShow" : "Booked";
                    table.Rows.Add(Guid.CreateVersion7(), tenant, facility, calendar, "Doctor", patients[n++ % patients.Length], "FollowUp", start, start.AddMinutes(20),
                        state, "General", "Phone", MondayMorning, MondayMorning);
                }
        }
        await using var conn = await BulkAsync(tenant, "scheduling.Appointments", table);
        await using var stats = new SqlCommand("UPDATE STATISTICS scheduling.Appointments", conn);
        await stats.ExecuteNonQueryAsync();
    }

    /// <summary>Bulk-loads the rows inside the tenant (row-level security applies) and returns the open connection for statistics.</summary>
    async Task<SqlConnection> BulkAsync(Guid tenant, string destination, DataTable table)
    {
        var conn = await sql.OpenAs("dbo", tenant);
        using var copy = new SqlBulkCopy(conn) { DestinationTableName = destination, BulkCopyTimeout = 600 };
        foreach (DataColumn c in table.Columns) copy.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await copy.WriteToServerAsync(table);
        return conn;
    }
}

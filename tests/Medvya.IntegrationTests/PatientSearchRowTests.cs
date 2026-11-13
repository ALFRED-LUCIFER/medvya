using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Patients.Domain;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-101 over HTTP on real PostgreSQL: a search row also carries the date of birth, the day of the last attended visit and the duplicate mark,
/// and nothing else. Who may search is unchanged, another organisation's visits and merge requests never show, and no clinical text travels.
/// Synthetic data only.
/// </summary>
public sealed class PatientSearchRowTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    static readonly string[] DeskFields =
        ["ageEstimated", "ageYears", "dateOfBirth", "duplicate", "fullName", "id", "initials", "lastVisitOn", "mobileTail", "mrnTail", "sexAtBirth", "status"];
    const string ClinicalReason = "Synthetic: chest tightness on stairs";

    readonly SchedulingHarness h = new(sql);
    // The newer record gets the id that sorts first (PostgreSQL orders a uuid as its text): only the merge request can put the main record ahead of it.
    readonly Guid[] pair = [.. new[] { Guid.NewGuid(), Guid.NewGuid() }.OrderBy(g => g.ToString(), StringComparer.Ordinal)];
    Guid main => pair[1];
    Guid duplicate => pair[0];
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await using (var patients = sql.Patients(h.Tenant))
        {
            // Two people the desk cannot tell apart by name, age and sex (audit finding F-08).
            foreach (var (id, mrn, born, estimated, at) in new[] { (main, "000010", new DateOnly(1979, 2, 1), false, -30), (duplicate, "000011", new DateOnly(1979, 6, 15), true, -1) })
                patients.Patients.Add(new Patient
                {
                    Id = id, TenantId = h.Tenant, FacilityId = h.Facility, Mrn = mrn, CountryCode = "IN", FullName = "Farhan Qureshi (synthetic)", DateOfBirth = born, DateOfBirthEstimated = estimated,
                    SexAtBirth = SexAtBirth.Male, Mobile = "+919000000201", Status = RegistrationStatus.Complete,
                    RegisteredAt = SchedulingHarness.MondayMorning.AddDays(at), ChangedAt = SchedulingHarness.MondayMorning,
                });
            patients.MergeRequests.Add(Request(h.Tenant, duplicate, main, MergeRequestStatus.Proposed));
            // "Not the same person" closed this one: neither record is marked.
            patients.MergeRequests.Add(Request(h.Tenant, h.SecondPatient, h.Patient, MergeRequestStatus.NotSame));
            await patients.SaveChangesAsync();
        }
        await using (var visits = sql.Scheduling(h.Tenant))
        {
            // Sunday 18 Oct at 23:30 UTC is already Monday 19 Oct in India: the row shows the clinic's day.
            visits.Appointments.Add(Visit(h.Tenant, main, new(2026, 10, 18, 23, 30, 0, TimeSpan.Zero), AppointmentState.Done));
            visits.Appointments.Add(Visit(h.Tenant, main, new(2026, 9, 1, 5, 0, 0, TimeSpan.Zero), AppointmentState.Done));
            visits.Appointments.Add(Visit(h.Tenant, main, new(2026, 10, 30, 5, 0, 0, TimeSpan.Zero), AppointmentState.CheckedIn)); // after "now": not a past visit
            visits.Appointments.Add(Visit(h.Tenant, h.Patient, new(2026, 10, 12, 5, 0, 0, TimeSpan.Zero), AppointmentState.NoShow)); // never attended
            visits.Appointments.Add(Visit(h.Tenant, h.Patient, new(2026, 10, 13, 5, 0, 0, TimeSpan.Zero), AppointmentState.Booked));
            await visits.SaveChangesAsync();
        }
        // Another organisation holds a visit and an open merge request under the very same patient ids: none of it may show here.
        var foreign = Guid.NewGuid();
        await using (var visits = sql.Scheduling(foreign))
        {
            visits.Appointments.Add(Visit(foreign, h.Patient, new(2026, 10, 15, 5, 0, 0, TimeSpan.Zero), AppointmentState.Done));
            visits.Appointments.Add(Visit(foreign, main, new(2026, 10, 19, 1, 0, 0, TimeSpan.Zero), AppointmentState.Done));
            await visits.SaveChangesAsync();
        }
        await using (var patients = sql.Patients(foreign))
        {
            patients.MergeRequests.Add(Request(foreign, h.Patient, h.OtherFacilityPatient, MergeRequestStatus.Proposed));
            await patients.SaveChangesAsync();
        }
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s =>
            {
                PatientEndpointTests.SignInFromHeaders(s, h.Tenant);
                s.AddSingleton<TimeProvider>(h.Clock);
            }));
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    static MergeRequest Request(Guid tenant, Guid patient, Guid candidate, MergeRequestStatus status) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant, PatientId = patient, CandidatePatientId = candidate, Status = status, DecidedAt = SchedulingHarness.MondayMorning,
    };

    Appointment Visit(Guid tenant, Guid patient, DateTimeOffset start, AppointmentState state) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant, FacilityId = h.Facility, CalendarId = h.Doctor, CalendarKind = CalendarKind.Doctor, PatientId = patient,
        Start = start, End = start.AddMinutes(20), State = state, ReasonCategory = ReasonCategory.ChronicReview, ReasonText = ClinicalReason, BookedAt = start, ChangedAt = start,
    };

    async Task<(HttpStatusCode Status, string Json)> Post(Guid user, string path, string query)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        using var res = await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), new { query });
        return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    static List<JsonElement> Items(string json) => [.. JsonDocument.Parse(json).RootElement.GetProperty("items").EnumerateArray()];

    static string? Text(JsonElement row, string name) => row.GetProperty(name).GetString();

    [Fact]
    public async Task EveryRoleThatMaySearchGetsOnlyTheDeskFieldsAndTheOthersStillGetNothing()
    {
        foreach (var (role, user) in h.ByRole)
        {
            var (status, json) = await Post(user, "/v1/patients/search", "farhan");
            if (role is Role.Patient or Role.HospitalIt) // Register / book patients: own data only, and none
            {
                Assert.Equal(HttpStatusCode.Forbidden, status);
                Assert.DoesNotContain("Farhan", json, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            Assert.Equal(HttpStatusCode.OK, status);
            var rows = Items(json);
            Assert.Equal(2, rows.Count);
            // The whole shape, so a field added later fails here until someone decides the desk may see it in a list.
            foreach (var row in rows) Assert.Equal(DeskFields, row.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            // The last visit is a calendar day and nothing else: no reason, visit type, doctor or note travels with it.
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", Text(rows[0], "lastVisitOn")!);
            foreach (var leak in new[] { "chest", "stairs", "ChronicReview", "FollowUp", "reason", "+91", "9000000201", "000010", "000011" })
                Assert.DoesNotContain(leak, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TwoPeopleWithOneNameAgeAndSexDifferAndTheMainRecordComesFirst()
    {
        var rows = Items((await Post(h.Reception, "/v1/patients/search", "farhan")).Json);
        Assert.Equal([main, duplicate], rows.Select(r => r.GetProperty("id").GetGuid()));
        Assert.Equal(rows[0].GetProperty("ageYears").GetInt32(), rows[1].GetProperty("ageYears").GetInt32());
        Assert.Equal(("MainRecord", "1979-02-01", "2026-10-19"), (Text(rows[0], "duplicate"), Text(rows[0], "dateOfBirth"), Text(rows[0], "lastVisitOn")));
        // A date estimated from a stated age is not shown as a date of birth; a patient who never attended has no last visit.
        Assert.Equal(("PossibleDuplicate", null, null), (Text(rows[1], "duplicate"), Text(rows[1], "dateOfBirth"), Text(rows[1], "lastVisitOn")));
    }

    [Fact]
    public async Task AClosedRequestMarksNothingAndABookedOrMissedVisitIsNotALastVisit()
    {
        var row = Assert.Single(Items((await Post(h.Reception, "/v1/patients/search", "meera")).Json));
        Assert.Equal((null, null, "1972-03-12"), (Text(row, "duplicate"), Text(row, "lastVisitOn"), Text(row, "dateOfBirth")));
    }

    [Fact]
    public async Task AnotherOrganisationsVisitsAndMergeRequestsNeverShow()
    {
        // Meera has a Done visit and an open merge request only in the other organisation; Farhan's later visit there must not replace his own.
        var meera = Assert.Single(Items((await Post(h.Reception, "/v1/patients/search", "meera")).Json));
        Assert.Equal((null, null), (Text(meera, "lastVisitOn"), Text(meera, "duplicate")));
        h.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal("2026-10-19", Text(Items((await Post(h.Reception, "/v1/patients/search", "farhan")).Json)[0], "lastVisitOn"));
    }

    [Fact]
    public async Task TheDoctorsLookupShowsTheSameRowAndNothingMore()
    {
        var (status, json) = await Post(h.Doctor, "/v1/patients/lookup", "farhan");
        Assert.Equal(HttpStatusCode.OK, status);
        var rows = Items(json);
        Assert.Equal([main, duplicate], rows.Select(r => r.GetProperty("id").GetGuid()));
        foreach (var row in rows)
            Assert.Equal(DeskFields.Where(f => f != "status").Append("access").Order(StringComparer.Ordinal), row.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(("MainRecord", "1979-02-01", "2026-10-19"), (Text(rows[0], "duplicate"), Text(rows[0], "dateOfBirth"), Text(rows[0], "lastVisitOn")));
        foreach (var leak in new[] { "chest", "ChronicReview", "+91", "9000000201", "000010" }) Assert.DoesNotContain(leak, json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(h.HospitalIt, "/v1/patients/lookup", "farhan")).Status);
    }
}

using System.Net;
using System.Text.Json;
using Medvya.Encounters.Application;
using Medvya.Prescribing.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-053 over HTTP on real PostgreSQL: the owner's Clinic today gives the five counts the system can stand behind, equal to the board and the doctors'
/// own days at the same moment; only practice configuration at the facility may read it; the answer holds counts and staff names only. Synthetic data only.
/// </summary>
public sealed class ClinicTodayTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.NoKnownAllergiesAsync(h.Patient);
        await h.DoctorTemplateAsync();
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

    HttpClient Client(Guid user, Guid? facility = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", (facility ?? h.Facility).ToString());
        return client;
    }

    async Task<(HttpStatusCode Status, JsonElement Json, string Body)> GetAsync(Guid user, string path, Guid? facility = null)
    {
        using var r = await Client(user, facility).GetAsync(new Uri(path, UriKind.Relative));
        var body = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, r.IsSuccessStatusCode ? JsonDocument.Parse(body).RootElement.Clone() : default, body);
    }

    Task<(HttpStatusCode Status, JsonElement Json, string Body)> TodayAsync(Guid user, Guid? facility = null) =>
        GetAsync(user, $"/v1/front-desk/clinic-today?facilityId={facility ?? h.Facility}", facility);

    [Fact]
    public async Task TheFiveCountsEqualTheBoardAndTheDoctorsDaysAndHoldOnlyCountsAndStaffNames()
    {
        // Meera (09:00) is seen: the doctor ends the visit with the note unsigned and a medicine still to authorise. Deepak (09:20) waits; 09:40 is still to come.
        var (seen, waiting, toCome) = (await h.BookAsync(9), await h.BookAsync(9, 20, h.SecondPatient), await h.BookAsync(9, 40));
        h.Clock.Advance(TimeSpan.FromHours(1.5)); // 09:30, inside Dr. Rao's morning
        var desk = h.FrontDesk(h.Reception);
        await desk.CheckInAsync(seen.Id, new CheckIn());
        var encounter = (await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(seen.Id)))!.Id;
        var metformin = (await h.Get<IFormulary>(h.Doctor).SearchAsync("Metformin", 1)).Single().Id;
        await h.Get<IPrescriptions>(h.Doctor).AddAsync(encounter, new AddRxLine(metformin, null, null, null));
        await h.Get<IEncounters>(h.Doctor).EndAsync(encounter);
        await desk.CheckInAsync(waiting.Id, new CheckIn());
        // Meera comes back the same morning as a walk-in: checked in, so she waits too.
        var walkIn = await h.Booking(h.Reception).WalkInAsync(new WalkIn { FacilityId = h.Facility, PatientId = h.Patient, VisitType = VisitType.FollowUp, ReasonCategory = ReasonCategory.General, CalendarId = h.Doctor, AllowRepeat = true });
        await desk.CheckInAsync(walkIn.Id, new CheckIn());
        h.Clock.Advance(TimeSpan.FromMinutes(25));

        var (status, today, body) = await TodayAsync(h.Admin);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["now", "timeZone", "booked", "toArrive", "arrivedWalkIn", "arrivedBooked", "waiting", "longestWaitMinutes", "incompleteRegistrations", "doctors"],
            today.EnumerateObject().Select(p => p.Name));
        Assert.Equal((3, 1, 1, 2, 2), (today.GetProperty("booked").GetInt32(), today.GetProperty("toArrive").GetInt32(), today.GetProperty("arrivedWalkIn").GetInt32(),
            today.GetProperty("arrivedBooked").GetInt32(), today.GetProperty("waiting").GetInt32()));
        Assert.Equal(25, today.GetProperty("longestWaitMinutes").GetInt32());
        Assert.Equal(3, today.GetProperty("incompleteRegistrations").GetInt32()); // the organisation's three quick registrations, as the directory's Incomplete filter
        var rao = today.GetProperty("doctors").EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == h.Doctor);
        Assert.Equal(["id", "name", "notesToSign", "notesBeforeToday", "toAuthorise"], rao.EnumerateObject().Select(p => p.Name));
        Assert.Equal(("Dr. Arjun Rao (synthetic)", 1, 0, 1), (rao.GetProperty("name").GetString(), rao.GetProperty("notesToSign").GetInt32(), rao.GetProperty("notesBeforeToday").GetInt32(), rao.GetProperty("toAuthorise").GetInt32()));

        // AC4: the board and the doctor's own day agree at the same moment.
        var board = (await GetAsync(h.Reception, $"/v1/front-desk/board?facilityId={h.Facility}")).Json.GetProperty("cards").EnumerateArray()
            .Select(c => (State: Enum.Parse<AppointmentState>(c.GetProperty("state").GetString()!), WalkIn: c.GetProperty("walkIn").GetBoolean())).ToList();
        Assert.Equal(board.Count(c => c.State == AppointmentState.Booked), today.GetProperty("toArrive").GetInt32());
        Assert.Equal(board.Count(c => VisitFlow.CanReassign(c.State)), today.GetProperty("waiting").GetInt32());
        Assert.Equal(board.Count(c => c.State != AppointmentState.Booked && c.WalkIn), today.GetProperty("arrivedWalkIn").GetInt32());
        var notes = (await GetAsync(h.Doctor, "/v1/notes/pending")).Json.GetProperty("items").EnumerateArray().Count(n => !n.GetProperty("noteSigned").GetBoolean());
        var rx = (await GetAsync(h.Doctor, "/v1/prescriptions/pending")).Json.GetProperty("items").GetArrayLength();
        Assert.Equal((notes, rx), (rao.GetProperty("notesToSign").GetInt32(), rao.GetProperty("toAuthorise").GetInt32()));

        // AC6: counts and staff names only.
        foreach (var patient in new[] { "Meera", "Deepak", "Lakshmi", "+9190", "000001" }) Assert.DoesNotContain(patient, body, StringComparison.Ordinal);
        Assert.DoesNotContain(walkIn.Id.ToString(), body, StringComparison.Ordinal);
        Assert.DoesNotContain(encounter.ToString(), body, StringComparison.Ordinal);

        // The next day the unsigned note is from before today; nobody waits any more on a day with no visits, and that is null, never 0.
        h.Clock.Advance(TimeSpan.FromDays(1));
        var tomorrow = (await TodayAsync(h.Admin)).Json;
        Assert.Equal(1, tomorrow.GetProperty("doctors").EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == h.Doctor).GetProperty("notesBeforeToday").GetInt32());
        Assert.Equal(JsonValueKind.Null, tomorrow.GetProperty("longestWaitMinutes").ValueKind);
        Assert.Equal(0, tomorrow.GetProperty("booked").GetInt32());
    }

    // SAFETY mutation check (AC5): drop the RequireAsync(CanConfigure) line in FrontDeskService.GetClinicTodayAsync and the receptionist, the doctor without
    // Config and the other location's admin read Adyar's counts.
    [Fact]
    public async Task OnlyPracticeConfigurationAtTheFacilityReadsIt()
    {
        Assert.Equal(HttpStatusCode.OK, (await TodayAsync(h.Admin)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await TodayAsync(h.Reception)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await TodayAsync(h.SecondDoctor)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await TodayAsync(h.Nurse)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(h.Elsewhere, $"/v1/front-desk/clinic-today?facilityId={h.Facility}", h.OtherFacility)).Status);

        // Another clinic's admin, signed in to their own clinic, naming this clinic's facility.
        var other = new SchedulingHarness(sql);
        try
        {
            await other.SeedAsync();
            await Assert.ThrowsAsync<SchedulingAccessDeniedException>(() => other.FrontDesk(other.Admin).GetClinicTodayAsync(h.Facility));
        }
        finally
        {
            await other.CloseAsync();
        }
    }
}

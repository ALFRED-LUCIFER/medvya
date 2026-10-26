using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-030 on real SQL Server: the clinic overview lists every doctor working today with visits per state, delay and next
/// patients, plus the shared waiting room; reassigning a waiting patient moves the visit to the other doctor's queue
/// atomically (one of two racing desks wins), keeps its token, frees its booked slot and is audited in the same transaction.
/// </summary>
public sealed class ClinicOverviewTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly BoardHubTests.SignedInDesks desks = new();
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        foreach (var user in new[] { h.Reception, h.SecondDoctor, h.PortalUser }) await desks.SignInAsync(sql, h.Tenant, user, h.Facility);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s => s.AddSingleton<TimeProvider>(h.Clock).AddSingleton<IStartupFilter>(desks)));
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    HttpClient Desk(Guid user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        return client;
    }

    async Task To(Guid visit, params string[] states)
    {
        var desk = Desk(h.Reception);
        foreach (var state in states)
            (await (state == "CheckedIn" ? desk.PostAsync($"/v1/appointments/{visit}/check-in", null) : desk.PostAsJsonAsync($"/v1/appointments/{visit}/status", new { state })))
                .EnsureSuccessStatusCode();
    }

    async Task<JsonElement> Overview(Guid user)
    {
        using var response = await Desk(user).GetAsync($"/v1/front-desk/overview?facilityId={h.Facility}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    Task<TemplateView> SecondDoctorWorksToday() => h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate() with { CalendarId = h.SecondDoctor });

    [Fact]
    public async Task TheOverviewListsEveryDoctorWorkingTodayWithVisitsPerStateDelayAndNextPatients()
    {
        var (seen, waiting, booked) = (await h.BookAsync(9), await h.BookAsync(9, 20, h.SecondPatient), await h.BookAsync(9, 40));
        await To(seen.Id, "CheckedIn");
        await To(waiting.Id, "CheckedIn");
        h.Clock.Advance(TimeSpan.FromMinutes(75)); // 09:15 IST: Dr. Rao calls the 09:00 patient in, 15 minutes late
        await To(seen.Id, "WithNurse", "ReadyForDoctor", "InConsult");
        h.Clock.Advance(TimeSpan.FromMinutes(35)); // 09:50: the 09:20 patient has waited 30 minutes past their time

        var json = await Overview(h.SecondDoctor); // a doctor sees it too, read only
        // Only Dr. Rao works today: Dr. Menon has no hours and no visits.
        var rao = Assert.Single(json.GetProperty("doctors").EnumerateArray());
        Assert.Equal(["calendarId", "name", "counts", "delayMinutes", "next"], rao.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Dr. Arjun Rao (synthetic)", rao.GetProperty("name").GetString());
        var counts = rao.GetProperty("counts");
        Assert.Equal((1, 1, 1, 0), (counts.GetProperty("Booked").GetInt32(), counts.GetProperty("CheckedIn").GetInt32(), counts.GetProperty("InConsult").GetInt32(), counts.GetProperty("Done").GetInt32()));
        Assert.Equal(30, rao.GetProperty("delayMinutes").GetInt32());
        Assert.Equal([waiting.Id, booked.Id], rao.GetProperty("next").EnumerateArray().Select(n => n.GetProperty("id").GetGuid()));

        var room = Assert.Single(json.GetProperty("waiting").EnumerateArray());
        Assert.Equal(["id", "token", "patientName", "calendarId", "state", "start", "since"], room.EnumerateObject().Select(p => p.Name));
        Assert.Equal((waiting.Id, "A-02", "Deepak Nair (synthetic)"), (room.GetProperty("id").GetGuid(), room.GetProperty("token").GetString(), room.GetProperty("patientName").GetString()));
        var body = json.GetRawText();
        Assert.DoesNotContain("chest", body, StringComparison.OrdinalIgnoreCase); // never the reason in the patient's words
        Assert.DoesNotContain("+9190", body, StringComparison.Ordinal);

        await SecondDoctorWorksToday();
        Assert.Equal(2, (await Overview(h.Reception)).GetProperty("doctors").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await Desk(h.PortalUser).GetAsync($"/v1/front-desk/overview?facilityId={h.Facility}")).StatusCode);
    }

    [Fact]
    public async Task ReassigningAWaitingPatientMovesTheVisitAtomicallyKeepsTheTokenAndIsAudited()
    {
        await SecondDoctorWorksToday();
        var visit = await h.BookAsync(9, 20);
        await To(visit.Id, "CheckedIn");

        using var response = await Desk(h.Reception).PostAsJsonAsync($"/v1/appointments/{visit.Id}/reassign", new { calendarId = h.SecondDoctor, reason = "DoctorRunningLate" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var card = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal((h.SecondDoctor, "CheckedIn", "A-01"), (card.GetProperty("calendarId").GetGuid(), card.GetProperty("state").GetString(), card.GetProperty("token").GetString()));

        await using (var db = sql.Scheduling(h.Tenant))
        {
            Assert.Equal(h.SecondDoctor, (await db.Appointments.SingleAsync(a => a.Id == visit.Id)).CalendarId);
            Assert.False(await db.ScheduleSlots.AnyAsync(s => s.AppointmentId == visit.Id)); // Dr. Rao's 09:20 slot is free again
            var history = await db.Reschedules.SingleAsync(r => r.AppointmentId == visit.Id);
            Assert.Equal((h.Doctor, h.SecondDoctor, ReassignReason.DoctorRunningLate, (Guid?)h.Reception), (history.FromCalendarId, history.ToCalendarId, history.ReassignReason, history.ActorUserId));
            Assert.Single(await db.Outbox.Where(e => e.Type == "AppointmentReassigned.v1" && e.AggregateId == visit.Id).ToListAsync());
        }
        await using (var audit = sql.Audit(h.Tenant))
        {
            var row = await audit.Events.SingleAsync(e => e.Action == SchedulingAudit.Reassign);
            Assert.Equal((AuditOutcome.Allowed, (Guid?)h.Patient, (Guid?)visit.Id), (row.Outcome, row.PatientId, row.ResourceId));
        }
        var overview = await Overview(h.Reception);
        Assert.Equal(h.SecondDoctor, overview.GetProperty("waiting")[0].GetProperty("calendarId").GetGuid());
        // The freed slot can be booked again.
        await h.BookAsync(9, 20, h.SecondPatient);
    }

    [Fact]
    public async Task OnlyAWaitingVisitMovesOnlyToAnotherActiveDoctorWithAReasonAndOnlyByTheQueue()
    {
        await SecondDoctorWorksToday();
        var (waiting, booked) = (await h.BookAsync(9, 20), await h.BookAsync(9, 40));
        await To(waiting.Id, "CheckedIn");
        var desk = Desk(h.Reception);
        async Task<(HttpStatusCode, string)> Reassign(Guid visit, object body, HttpClient? by = null)
        {
            using var r = await (by ?? desk).PostAsJsonAsync($"/v1/appointments/{visit}/reassign", body);
            return (r.StatusCode, await r.Content.ReadAsStringAsync());
        }

        Assert.Equal(HttpStatusCode.Conflict, (await Reassign(booked.Id, new { calendarId = h.SecondDoctor, reason = "PatientRequest" })).Item1); // not waiting
        Assert.Contains("unchanged", (await Reassign(waiting.Id, new { calendarId = h.Doctor, reason = "PatientRequest" })).Item2, StringComparison.Ordinal);
        Assert.Contains("unknown", (await Reassign(waiting.Id, new { calendarId = h.Elsewhere, reason = "PatientRequest" })).Item2, StringComparison.Ordinal);
        Assert.Contains("reason", (await Reassign(waiting.Id, new { calendarId = h.SecondDoctor })).Item2, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await Reassign(waiting.Id, new { calendarId = h.SecondDoctor, reason = "PatientRequest" }, Desk(h.SecondDoctor))).Item1); // read-only board
        Assert.Equal(HttpStatusCode.NotFound, (await Reassign(Guid.NewGuid(), new { calendarId = h.SecondDoctor, reason = "PatientRequest" })).Item1);
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Equal(h.Doctor, (await db.Appointments.SingleAsync(a => a.Id == waiting.Id)).CalendarId); // nothing moved
    }

    [Fact]
    public async Task OfTwoDesksReassigningTheSamePatientExactlyOneWins()
    {
        await SecondDoctorWorksToday();
        var visit = await h.BookAsync(9, 20);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        var desksAtOnce = await h.AddReceptionistsAsync(2);
        var results = await Task.WhenAll(desksAtOnce.Select(async d =>
        {
            try
            {
                await h.FrontDesk(d).ReassignAsync(visit.Id, new ReassignVisit { CalendarId = h.SecondDoctor, Reason = ReassignReason.BalanceWorkload });
                return "moved";
            }
            catch (Exception e) when (e is VisitTransitionException or SchedulingValidationException)
            {
                return "refused"; // moved by the other desk first: "changed", or already in that queue: "unchanged"
            }
        }));
        Assert.Equal(["moved", "refused"], results.Order(StringComparer.Ordinal));
        await using var db = sql.Scheduling(h.Tenant);
        Assert.Single(await db.Reschedules.Where(r => r.AppointmentId == visit.Id).ToListAsync());
    }

    [Fact]
    public void DelayIsTheLaterOfALateConsultationAndTheLongestWaitPastItsTime()
    {
        var nine = SchedulingHarness.At(9);
        Assert.Equal(0, ClinicDelay.Minutes([], nine));
        Assert.Equal(0, ClinicDelay.Minutes([(nine.AddMinutes(30), AppointmentState.CheckedIn, nine)], nine)); // early arrival
        Assert.Equal(15, ClinicDelay.Minutes([(nine, AppointmentState.InConsult, nine.AddMinutes(15))], nine.AddMinutes(20)));
        Assert.Equal(40, ClinicDelay.Minutes(
            [(nine, AppointmentState.InConsult, nine.AddMinutes(15)), (nine.AddMinutes(20), AppointmentState.ReadyForDoctor, nine.AddMinutes(10))], nine.AddHours(1)));
        Assert.Equal(0, ClinicDelay.Minutes([(nine, AppointmentState.Booked, nine), (nine, AppointmentState.Done, nine.AddHours(2))], nine.AddHours(3))); // not waiting
    }
}

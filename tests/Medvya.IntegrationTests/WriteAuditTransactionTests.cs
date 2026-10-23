using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Audit.Infrastructure;
using Medvya.Audit.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.Scheduling.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P0-017 on real SQL Server, through HTTP: every scheduling write commits its audit event in the same transaction as the
/// change. With the audit store down, the request fails and nothing it would have changed is in the database; with it up, each
/// write leaves exactly one Allowed event, and a rejected write one Rejected event without the values that were sent.
/// </summary>
public sealed class WriteAuditTransactionTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly AuditSwitch audit = new();
    WebApplicationFactory<Program> factory = null!;

    /// <summary>Turns the audit store off for in-transaction writes, as if the insert failed.</summary>
    sealed class AuditSwitch { public bool Down { get; set; } }

    sealed class SwitchableAudit(AuditLog real, AuditSwitch state) : IAuditLog
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => real.WriteAsync(auditEvent, cancellationToken);

        public Task WriteAsync(AuditEvent auditEvent, DbTransaction transaction, CancellationToken cancellationToken = default) =>
            state.Down ? throw new InvalidOperationException("audit store down") : real.WriteAsync(auditEvent, transaction, cancellationToken);
    }

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s => s
                .AddSingleton<TimeProvider>(h.Clock).AddSingleton(audit)
                .AddSingleton<IStartupFilter>(new FrontDeskEndpointTests.SignedInFromHeader(h.Tenant))
                .AddScoped<IAuditLog>(sp => new SwitchableAudit(new AuditLog(sp.GetRequiredService<AuditDbContext>(), sp.GetService<IRequestOrigin>()), sp.GetRequiredService<AuditSwitch>()))));
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

    async Task<List<AuditEvent>> Events()
    {
        await using var db = sql.Audit(h.Tenant);
        return await db.Events.OrderBy(e => e.OccurredAt).ToListAsync();
    }

    /// <summary>The request fails (the test server surfaces the error, a real host answers 500) and writes no audit event.</summary>
    async Task FailsUnaudited(Func<Task<HttpResponseMessage>> send)
    {
        var before = (await Events()).Count;
        audit.Down = true;
        try
        {
            using var response = await send();
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        catch (InvalidOperationException e) when (e.Message == "audit store down") { }
        finally
        {
            audit.Down = false;
        }
        Assert.Equal(before, (await Events()).Count);
    }

    object Template(Guid calendar) => new
    {
        facilityId = h.Facility, calendarId = calendar, calendarKind = "Doctor", slotMinutes = 20,
        hours = new[] { new { weekday = 1, start = "09:00:00", end = "13:00:00", room = "Room 4" } }, breaks = Array.Empty<object>(), visitLengths = Array.Empty<object>(),
    };

    object Hold(int hour) => new { facilityId = h.Facility, calendarId = h.Doctor, start = At(hour), visitType = "FollowUp" };

    static async Task<Guid> Id(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task WithTheAuditStoreDownNoSchedulingWriteIsPersisted()
    {
        var admin = Desk(h.Admin);
        var desk = Desk(h.Reception);

        await FailsUnaudited(() => admin.PutAsJsonAsync("/v1/schedule/templates", Template(h.SecondDoctor)));
        await FailsUnaudited(() => admin.PostAsJsonAsync("/v1/schedule/resources", new { facilityId = h.Facility, name = "Dressing room (synthetic)" }));
        await FailsUnaudited(() => desk.PostAsJsonAsync("/v1/slot-holds", Hold(9)));

        await using (var db = sql.Scheduling(h.Tenant))
        {
            Assert.False(await db.Templates.AnyAsync(t => t.CalendarId == h.SecondDoctor));
            Assert.False(await db.Resources.AnyAsync(r => r.Name == "Dressing room (synthetic)"));
            Assert.False(await db.SlotHolds.AnyAsync());
        }

        // A hold to release and one to book from, then a booked visit to check in and move, all made with the audit store up.
        var release = await Id(await desk.PostAsJsonAsync("/v1/slot-holds", Hold(10)));
        await FailsUnaudited(() => desk.DeleteAsync($"/v1/slot-holds/{release}"));
        await using (var db = sql.Scheduling(h.Tenant)) Assert.True(await db.SlotHolds.AnyAsync(x => x.Id == release));

        var hold = await Id(await desk.PostAsJsonAsync("/v1/slot-holds", Hold(9)));
        var booking = new { holdId = hold, patientId = h.Patient, reasonCategory = "General", channel = "Phone" };
        await FailsUnaudited(() => desk.PostAsJsonAsync("/v1/appointments", booking));
        await using (var db = sql.Scheduling(h.Tenant))
        {
            Assert.False(await db.Appointments.AnyAsync());
            Assert.False(await db.ScheduleSlots.AnyAsync());
            Assert.True(await db.SlotHolds.AnyAsync(x => x.Id == hold)); // the hold was not used up either
        }

        var visit = await Id(await desk.PostAsJsonAsync("/v1/appointments", booking));
        await FailsUnaudited(() => desk.PostAsync($"/v1/appointments/{visit}/check-in", null));
        await using (var db = sql.Scheduling(h.Tenant))
            Assert.Equal((AppointmentState.Booked, 1), ((await db.Appointments.SingleAsync()).State, await db.StatusEvents.CountAsync(e => e.AppointmentId == visit)));

        (await desk.PostAsync($"/v1/appointments/{visit}/check-in", null)).EnsureSuccessStatusCode();
        await FailsUnaudited(() => desk.PostAsJsonAsync($"/v1/appointments/{visit}/status", new { state = "WithNurse" }));
        await using (var db = sql.Scheduling(h.Tenant))
            Assert.Equal((AppointmentState.CheckedIn, 2), ((await db.Appointments.SingleAsync()).State, await db.StatusEvents.CountAsync(e => e.AppointmentId == visit)));
    }

    [Fact]
    public async Task EachSchedulingWriteLeavesOneAllowedEventInItsTransaction()
    {
        var admin = Desk(h.Admin);
        var desk = Desk(h.Reception);
        var before = (await Events()).Select(e => e.Id).ToHashSet(); // the harness's own template setup is audited too
        (await admin.PutAsJsonAsync("/v1/schedule/templates", Template(h.SecondDoctor))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/v1/schedule/resources", new { facilityId = h.Facility, name = "Dressing room (synthetic)" })).EnsureSuccessStatusCode();
        var release = await Id(await desk.PostAsJsonAsync("/v1/slot-holds", Hold(10)));
        (await desk.DeleteAsync($"/v1/slot-holds/{release}")).EnsureSuccessStatusCode();
        var hold = await Id(await desk.PostAsJsonAsync("/v1/slot-holds", Hold(9)));
        var visit = await Id(await desk.PostAsJsonAsync("/v1/appointments", new { holdId = hold, patientId = h.Patient, reasonCategory = "General", channel = "Phone" }));
        (await desk.PostAsync($"/v1/appointments/{visit}/check-in", null)).EnsureSuccessStatusCode();
        (await desk.PostAsync($"/v1/appointments/{visit}/check-in", null)).EnsureSuccessStatusCode(); // a repeat is an attempt too
        (await desk.PostAsJsonAsync($"/v1/appointments/{visit}/status", new { state = "WithNurse" })).EnsureSuccessStatusCode();

        var events = (await Events()).Where(e => !before.Contains(e.Id)).ToList();
        // The test clock stands still, so the events are compared as a set of attempts, one each.
        Assert.Equal(
            ((string[])["schedule.template.update", "schedule.resource.create", "slot.hold", "slot.release", "slot.hold", "appointment.book",
             "appointment.check_in", "appointment.check_in", "appointment.status"]).Order(StringComparer.Ordinal),
            events.Select(e => e.Action).Order(StringComparer.Ordinal));
        Assert.All(events, e => Assert.Equal(AuditOutcome.Allowed, e.Outcome));
        // Written by the use case, so a booking and its visit moves now name the patient; the request's origin is kept.
        Assert.All(events.Where(e => e.ResourceType == "appointment"), e => Assert.Equal((h.Patient, visit), (e.PatientId!.Value, e.ResourceId!.Value)));
        Assert.All(events, e => Assert.False(string.IsNullOrEmpty(e.CorrelationId), $"{e.Action}: no correlation id"));
    }

    [Fact]
    public async Task ARejectedWriteLeavesOneRejectedEventWithoutTheValuesSent()
    {
        var desk = Desk(h.Reception);
        var hold = await Id(await desk.PostAsJsonAsync("/v1/slot-holds", Hold(9)));
        var before = (await Events()).Select(e => e.Id).ToHashSet();

        // An unknown patient, with the reason in the patient's own words.
        using var rejected = await desk.PostAsJsonAsync("/v1/appointments",
            new { holdId = hold, patientId = Guid.NewGuid(), reasonCategory = "General", channel = "Phone", reasonText = "Synthetic: chest tightness on stairs" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var row = Assert.Single(await Events(), e => !before.Contains(e.Id));
        Assert.Equal(("appointment.book", AuditOutcome.Rejected), (row.Action, row.Outcome));
        var stored = JsonSerializer.Serialize(row);
        Assert.DoesNotContain("chest", stored, StringComparison.OrdinalIgnoreCase); // codes only, never what was entered
        Assert.Null(row.PatientId); // nothing was done for any patient
        await using var db = sql.Scheduling(h.Tenant);
        Assert.False(await db.Appointments.AnyAsync());
    }
}

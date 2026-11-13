using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-007 over HTTP on real PostgreSQL: the endpoints are only for roles with vitals entry, answer
/// validation and handoff blockers with codes the screen translates, keep the generic status move from skipping the handoff, and audit
/// every request with the patient and no values.
/// </summary>
public sealed class IntakeEndpointTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
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

    HttpClient Client(Guid user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        return client;
    }

    static Uri U(string path) => new(path, UriKind.Relative);

    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    Task FlagAsync(bool on) => h.Get<IFeatureFlags>(h.Admin).SetAsync(IntakeFlag.Key, on, "clinical-team", "Nurse intake (synthetic test)");

    async Task<Guid> WithNurseAsync()
    {
        var visit = await h.BookAsync(10);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        await h.FrontDesk(h.Reception).MoveAsync(visit.Id, new MoveVisit { State = Medvya.Scheduling.Domain.AppointmentState.WithNurse });
        return visit.Id;
    }

    /// <summary>
    /// MDV-P0-028: with the switch off the nurse's own workflow (escalate, medicines, handoff) does not exist, but the doctor's consultation
    /// entry (audit P0-2) still reads the intake and records vitals and allergies on the same routes. Mutation checks: gate the whole group
    /// and the doctor's calls answer 404; ungate a nurse-only route and it answers.
    /// </summary>
    [Fact]
    public async Task WithTheSwitchOffOnlyTheNurseWorkflowRoutesDoNotExistAndTurningItOnNeedsNoRedeploy()
    {
        await FlagAsync(true);
        var visit = await WithNurseAsync();
        await FlagAsync(false);

        using (var escalate = await Client(h.Nurse).PostAsJsonAsync(U($"/v1/intake/{visit}/escalate"), new { reason = "Synthetic" }))
            Assert.Equal(HttpStatusCode.NotFound, escalate.StatusCode);
        using (var medicine = await Client(h.Nurse).PostAsJsonAsync(U($"/v1/intake/{visit}/medicines"), new { name = "Synthetic" }))
            Assert.Equal(HttpStatusCode.NotFound, medicine.StatusCode);
        using (var handoff = await Client(h.Nurse).PostAsync(U($"/v1/intake/{visit}/handoff"), null)) Assert.Equal(HttpStatusCode.NotFound, handoff.StatusCode);
        using (var deskHandoff = await Client(h.Reception).PostAsync(U($"/v1/intake/{visit}/handoff"), null))
            Assert.Equal(HttpStatusCode.Forbidden, deskHandoff.StatusCode); // the role check still comes first

        // A clinic with no nurse step: the doctor's consultation entry.
        var consult = await h.BookAsync(11);
        await h.FrontDesk(h.Reception).CheckInAsync(consult.Id, new CheckIn());
        _ = await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(consult.Id)) ?? throw new InvalidOperationException("No encounter.");
        using (var desk = await Client(h.Reception).GetAsync(U($"/v1/intake/{consult.Id}"))) Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode);
        using (var read = await Client(h.Doctor).GetAsync(U($"/v1/intake/{consult.Id}"))) Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using (var none = await Client(h.Doctor).PostAsync(U($"/v1/intake/{consult.Id}/no-known-allergies"), null))
            Assert.Equal((HttpStatusCode.OK, true), (none.StatusCode, (await Json(none)).GetProperty("noKnownAllergiesConfirmed").GetBoolean()));
        using (var vitals = await Client(h.Doctor).PutAsJsonAsync(U($"/v1/intake/{consult.Id}"), new { vitals = new[] { new { kind = "Weight", value = 18.4 }, new { kind = "SystolicBp", value = 168.0 } } }))
            Assert.Equal(HttpStatusCode.OK, vitals.StatusCode);
        using (var keep = await Client(h.Doctor).PostAsync(U($"/v1/intake/{consult.Id}/vitals/SystolicBp/keep"), null)) Assert.Equal(HttpStatusCode.OK, keep.StatusCode);
        string allergyId;
        using (var allergy = await Client(h.Doctor).PostAsJsonAsync(U($"/v1/intake/{consult.Id}/allergies"), new { substance = "Amoxicillin (synthetic)", status = "Confirmed" }))
        {
            Assert.Equal(HttpStatusCode.OK, allergy.StatusCode);
            allergyId = (await Json(allergy)).GetProperty("allergies")[0].GetProperty("id").GetString()!;
        }
        using (var status = await Client(h.Doctor).PutAsJsonAsync(U($"/v1/intake/{consult.Id}/allergies/{allergyId}/status"), new { status = "PatientReported" }))
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);

        await FlagAsync(true);
        using (var on = await Client(h.Nurse).PostAsync(U($"/v1/intake/{visit}/handoff"), null)) Assert.NotEqual(HttpStatusCode.NotFound, on.StatusCode);
    }

    [Fact]
    public async Task TheNurseWorksTheIntakeEndToEndAndOthersAreRefused()
    {
        await FlagAsync(true);
        var visit = await WithNurseAsync();

        using (var desk = await Client(h.Reception).GetAsync(U($"/v1/intake/{visit}")))
            Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode); // reception has no vitals entry: refused by the endpoint's policy
        using (var admin = await Client(h.Admin).PutAsJsonAsync(U($"/v1/intake/{visit}"), new { vitals = new[] { new { kind = "Pulse", value = 70 } } }))
            Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        using (var doctorList = await Client(h.Nurse).GetAsync(U($"/v1/intake-escalations?facilityId={h.Facility}")))
            Assert.Equal(HttpStatusCode.Forbidden, doctorList.StatusCode); // the urgent list is the doctor's capability, not the nurse's

        using (var screen = await Client(h.Nurse).GetAsync(U($"/v1/intake/{visit}")))
        {
            var body = await Json(screen);
            Assert.Equal(("Meera Iyer (synthetic)", "WithNurse", "Asia/Kolkata"),
                (body.GetProperty("patient").GetProperty("fullName").GetString(), body.GetProperty("intake").GetProperty("state").GetString(), body.GetProperty("timeZone").GetString()));
        }

        using (var impossible = await Client(h.Nurse).PutAsJsonAsync(U($"/v1/intake/{visit}"), new { vitals = new[] { new { kind = "OxygenSaturation", value = 130 } } }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, impossible.StatusCode);
            Assert.Equal("implausible", (await Json(impossible)).GetProperty("errors").GetProperty("vitals[0].value")[0].GetString());
        }
        using (var flagged = await Client(h.Nurse).PutAsJsonAsync(U($"/v1/intake/{visit}"),
            new { chiefComplaint = "Chest tightness on stairs", vitals = new[] { new { kind = "SystolicBp", value = 168 }, new { kind = "DiastolicBp", value = 96 } } }))
            Assert.Equal("High", (await Json(flagged)).GetProperty("vitals")[0].GetProperty("flag").GetString());

        using (var noReason = await Client(h.Nurse).PostAsJsonAsync(U($"/v1/intake/{visit}/escalate"), new { reason = " " }))
            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        using (var blocked = await Client(h.Nurse).PostAsync(U($"/v1/intake/{visit}/handoff"), null))
        {
            var body = await Json(blocked);
            Assert.Equal((HttpStatusCode.Conflict, "handoff_blocked", "vitals_undecided"), (blocked.StatusCode, body.GetProperty("code").GetString(), body.GetProperty("blockers")[0].GetString()));
        }
        // The board's own status move cannot go around it.
        using (var around = await Client(h.Reception).PostAsJsonAsync(U($"/v1/appointments/{visit}/status"), new { state = "ReadyForDoctor" }))
            Assert.Equal(("intake_required", HttpStatusCode.Conflict), ((await Json(around)).GetProperty("code").GetString(), around.StatusCode));

        using (var keep = await Client(h.Nurse).PostAsync(U($"/v1/intake/{visit}/vitals/SystolicBp/keep"), null)) Assert.Equal(HttpStatusCode.OK, keep.StatusCode);
        using (var keep = await Client(h.Nurse).PostAsync(U($"/v1/intake/{visit}/vitals/DiastolicBp/keep"), null)) Assert.Equal(HttpStatusCode.OK, keep.StatusCode);
        using (var escalate = await Client(h.Nurse).PostAsJsonAsync(U($"/v1/intake/{visit}/escalate"), new { reason = "Headache with high BP (synthetic)" })) Assert.Equal(HttpStatusCode.OK, escalate.StatusCode);
        using (var urgent = await Client(h.Doctor).GetAsync(U($"/v1/intake-escalations?facilityId={h.Facility}")))
            Assert.Equal("Headache with high BP (synthetic)", (await Json(urgent))[0].GetProperty("reason").GetString());
        using (var none = await Client(h.Nurse).PostAsync(U($"/v1/intake/{visit}/no-known-allergies"), null)) Assert.Equal(HttpStatusCode.OK, none.StatusCode);
        using (var handed = await Client(h.Nurse).PostAsync(U($"/v1/intake/{visit}/handoff"), null))
        {
            var body = await Json(handed);
            Assert.Equal(("ReadyForDoctor", true), (body.GetProperty("state").GetString(), body.GetProperty("handedOff").GetBoolean()));
        }

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.ResourceType == "intake" && e.Outcome == AuditOutcome.Allowed && e.PatientId != null).Select(e => new { e.Action, e.PatientId, e.Purpose }).ToListAsync();
        Assert.All(rows, r => Assert.Equal((h.Patient, AuditPurpose.Treatment), (r.PatientId!.Value, r.Purpose)));
        Assert.Contains("intake.handoff", rows.Select(r => r.Action));
        Assert.Contains(await audit.Events.Where(e => e.ResourceType == "intake").Select(e => e.Outcome).ToListAsync(), o => o == AuditOutcome.Rejected || o == AuditOutcome.Denied);
    }
}

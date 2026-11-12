using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Encounters.Domain;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-001 over HTTP on real PostgreSQL: the identity banner's exact fields (no allergy claim, consent from receipts), the refusal a
/// clinician can act on ("Request access"), the open-consultation conflict, and one audit row with the patient for every request.
/// </summary>
public sealed class EncounterEndpointTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
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

    async Task<Guid> ArrivedAsync(Guid patient, int hour)
    {
        var visit = await h.BookAsync(hour, patient: patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        return visit.Id;
    }

    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task TheDoctorStartsAndOpensTheConsultationWithTheIdentityBanner()
    {
        var visit = await ArrivedAsync(h.Patient, 10);
        using var started = await Client(h.Doctor).PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = visit });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var id = (await Json(started)).GetProperty("id").GetGuid();

        using var opened = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{id}"));
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var banner = await Json(opened);
        var patient = banner.GetProperty("patient");
        // Exactly these fields: identity for the banner, nothing from the registration record beyond it (the language is a banner field, MDV-P2-032).
        Assert.Equal(["ageYears", "dateOfBirth", "fullName", "id", "mrn", "preferredLanguage", "sexAtBirth"], patient.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(("Meera Iyer (synthetic)", "000001", "1972-03-12", 54, "Female"),
            (patient.GetProperty("fullName").GetString(), patient.GetProperty("mrn").GetString(), patient.GetProperty("dateOfBirth").GetString(),
             patient.GetProperty("ageYears").GetInt32(), patient.GetProperty("sexAtBirth").GetString()));
        Assert.Equal("Asia/Kolkata", banner.GetProperty("timeZone").GetString()); // the encounter facility's zone, for its times
        Assert.False(banner.GetProperty("treatmentConsent").GetBoolean()); // the seeded patient has no receipt: never shown as given
        // Nothing recorded reads "not recorded", never "none"; then a listed allergy and a recorded "no known allergies" are told apart.
        Assert.Equal(("NotRecorded", 0), (banner.GetProperty("allergies").GetProperty("state").GetString(), banner.GetProperty("allergies").GetProperty("allergies").GetArrayLength()));
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.Allergies.Add(new() { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Kind = AllergyKind.Substance, Substance = "Penicillin", Status = AllergyStatus.Confirmed, Source = ChartSource.ClinicianVerified, RecordedAt = h.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        using (var again = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{id}")))
        {
            var listed = (await Json(again)).GetProperty("allergies");
            Assert.Equal(("Listed", "Penicillin", "Confirmed"), (listed.GetProperty("state").GetString(), listed.GetProperty("allergies")[0].GetProperty("substance").GetString(), listed.GetProperty("allergies")[0].GetProperty("status").GetString()));
        }
        var encounter = banner.GetProperty("encounter");
        Assert.Equal(("Active", "Room 3"), (encounter.GetProperty("status").GetString(), encounter.GetProperty("room").GetString()));

        using (var ended = await Client(h.Doctor).PostAsync(U($"/v1/encounters/{id}/end"), null))
            Assert.Equal("Ended", (await Json(ended)).GetProperty("status").GetString());

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.ResourceType == "encounter").OrderBy(e => e.Id).Select(e => new { e.Action, e.Outcome, e.PatientId, e.Purpose }).ToListAsync();
        Assert.Equal(["encounter.start", "encounter.read", "encounter.read", "encounter.end"], rows.Select(r => r.Action)); // the banner was opened twice
        Assert.All(rows, r => Assert.Equal((AuditOutcome.Allowed, (Guid?)h.Patient, AuditPurpose.Treatment), (r.Outcome, r.PatientId, r.Purpose)));
    }

    [Fact]
    public async Task ARefusalTellsTheClinicianWhetherAskingForAccessCanHelp()
    {
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        var visit = await ArrivedAsync(h.Patient, 10);

        using (var desk = await Client(h.Reception).PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = visit }))
            Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode); // no Patient 360 at all: refused by the endpoint's policy
        using (var nurse = await Client(h.Nurse).PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = visit }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode);
            Assert.Equal("no_access", (await Json(nurse)).GetProperty("code").GetString());
        }
        using (var colleague = await Client(h.SecondDoctor).PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = visit }))
        {
            var body = await Json(colleague);
            Assert.Equal((HttpStatusCode.Forbidden, "no_care_relationship", false), (colleague.StatusCode, body.GetProperty("code").GetString(), body.GetProperty("requestPending").GetBoolean()));
            Assert.DoesNotContain("Meera", body.ToString(), StringComparison.Ordinal);
        }
        using (await Client(h.SecondDoctor).PostAsJsonAsync(U($"/v1/patients/{h.Patient}/access-requests"), new { reason = "CoveringColleague" })) { }
        using (var again = await Client(h.SecondDoctor).PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = visit }))
            Assert.True((await Json(again)).GetProperty("requestPending").GetBoolean());
        using (var unknown = await Client(h.Doctor).PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = Guid.NewGuid() }))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        await using var audit = sql.Audit(h.Tenant);
        Assert.Equal([AuditOutcome.Denied, AuditOutcome.Denied, AuditOutcome.Denied, AuditOutcome.Denied, AuditOutcome.NotFound],
            await audit.Events.Where(e => e.Action == "encounter.start").OrderBy(e => e.Id).Select(e => e.Outcome).ToListAsync());
    }

    [Fact]
    public async Task AnOpenConsultationIsNamedSoTheDoctorCanEndOrLeaveIt()
    {
        var meera = await ArrivedAsync(h.Patient, 10);
        var deepak = await ArrivedAsync(h.SecondPatient, 11);
        using var first = await Client(h.Doctor).PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = meera });
        var openId = (await Json(first)).GetProperty("id").GetGuid();

        using (var conflict = await Client(h.Doctor).PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = deepak }))
        {
            var body = await Json(conflict);
            Assert.Equal((HttpStatusCode.Conflict, "open_encounter", openId), (conflict.StatusCode, body.GetProperty("code").GetString(), body.GetProperty("encounterId").GetGuid()));
        }
        using (var leave = await Client(h.Doctor).PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = deepak, leaveOpen = true }))
            Assert.Equal(HttpStatusCode.OK, leave.StatusCode);

        using var booked = await Client(h.Doctor).PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = (await h.BookAsync(11, 20)).Id, leaveOpen = true });
        Assert.Equal((HttpStatusCode.Conflict, "not_arrived"), (booked.StatusCode, (await Json(booked)).GetProperty("code").GetString()));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-004 on real PostgreSQL, over HTTP: the three allergy states stay apart, every fact carries its source, trends are the last readings
/// oldest first, another tenant sees nothing (EF filter and row-level security), and every request leaves one audit row with the patient:
/// the clinician's allowed read, the receptionist's refusal, a colleague without a care relationship.
/// </summary>
public sealed class PatientChartTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;
    static readonly DateTimeOffset T0 = new(2026, 10, 1, 4, 30, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await h.BookAsync(10, patient: h.Patient); // the doctor's care relationship with Meera
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

    static Uri Chart(Guid patient) => new($"/v1/patients/{patient}/chart", UriKind.Relative);

    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    async Task SeedAsync(Action<Medvya.Encounters.Infrastructure.Persistence.EncountersDbContext> add)
    {
        await using var db = sql.Encounters(h.Tenant);
        add(db);
        await db.SaveChangesAsync();
    }

    Allergy Allergy(Guid patient, AllergyKind kind, string? substance, AllergyStatus status = AllergyStatus.Confirmed, ChartSource source = ChartSource.ClinicianVerified) =>
        new() { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = patient, Kind = kind, Substance = substance, Status = status, Source = source, RecordedAt = T0 };

    [Fact]
    public async Task AllergyStatesAreThreeDifferentAnswers()
    {
        using var none = await Client(h.Doctor).GetAsync(Chart(h.Patient));
        Assert.Equal(HttpStatusCode.OK, none.StatusCode);
        var empty = (await Json(none)).GetProperty("chart");
        Assert.Equal(("NotRecorded", 0), (empty.GetProperty("allergyState").GetString(), empty.GetProperty("allergies").GetArrayLength())); // unknown is never "none"

        await SeedAsync(db => db.Allergies.Add(Allergy(h.Patient, AllergyKind.NoKnownAllergies, null)));
        Assert.Equal("NoKnownAllergies", await StateAsync());

        await SeedAsync(db => db.Allergies.Add(Allergy(h.Patient, AllergyKind.Substance, "Penicillin")));
        using var listed = await Client(h.Doctor).GetAsync(Chart(h.Patient));
        var chart = (await Json(listed)).GetProperty("chart");
        // A listed allergy beats a "no known allergies" entry, so the banner never hides one.
        Assert.Equal("Listed", chart.GetProperty("allergyState").GetString());
        var allergy = chart.GetProperty("allergies")[0];
        Assert.Equal(("Penicillin", "Confirmed", "ClinicianVerified"), (allergy.GetProperty("substance").GetString(), allergy.GetProperty("status").GetString(), allergy.GetProperty("source").GetString()));
    }

    async Task<string?> StateAsync()
    {
        using var r = await Client(h.Doctor).GetAsync(Chart(h.Patient));
        return (await Json(r)).GetProperty("chart").GetProperty("allergyState").GetString();
    }

    [Fact]
    public async Task EveryFactCarriesItsSourceAndTrendsAreTheLastReadingsOldestFirst()
    {
        await SeedAsync(db =>
        {
            foreach (var (src, i) in new[] { ChartSource.ClinicianVerified, ChartSource.PatientReported, ChartSource.Imported, ChartSource.SyncedFromHis }.Select((s, i) => (s, i)))
                db.Problems.Add(new() { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Description = $"Problem {i}", Verification = ProblemVerification.Verified, Source = src, RecordedAt = T0.AddMinutes(i) });
            db.MedicationStatements.Add(new() { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Name = "Metformin", Dose = "500 mg", State = MedicationState.Active, Source = ChartSource.Imported, RecordedAt = T0 });
            for (var i = 0; i < 15; i++) // 15 weights: only the last 12 are the trend
                db.Observations.Add(new() { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Code = ObservationCode.Weight, ValueHundredths = (70 + i) * 100, TakenAt = T0.AddDays(i), Source = ChartSource.ClinicianVerified });
            db.Observations.Add(new() { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Code = ObservationCode.HbA1c, ValueHundredths = 740, TakenAt = T0, Source = ChartSource.SyncedFromHis });
        });
        using var r = await Client(h.Doctor).GetAsync(Chart(h.Patient));
        var chart = (await Json(r)).GetProperty("chart");

        Assert.Equal(["ClinicianVerified", "PatientReported", "Imported", "SyncedFromHis"], chart.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("source").GetString()));
        Assert.Equal("Imported", chart.GetProperty("medicines")[0].GetProperty("source").GetString());
        var weight = chart.GetProperty("trends").EnumerateArray().Single(t => t.GetProperty("code").GetString() == "Weight");
        Assert.Equal(Enumerable.Range(3, 12).Select(i => 70m + i), weight.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("value").GetDecimal()));
        Assert.All(weight.GetProperty("points").EnumerateArray(), p => Assert.Equal("ClinicianVerified", p.GetProperty("source").GetString()));
        var hba1c = chart.GetProperty("trends").EnumerateArray().Single(t => t.GetProperty("code").GetString() == "HbA1c");
        Assert.Equal(("%", "SyncedFromHis"), (hba1c.GetProperty("unit").GetString(), hba1c.GetProperty("points")[0].GetProperty("source").GetString()));
        var lastWeight = chart.GetProperty("lastVitals").EnumerateArray().Single();
        Assert.Equal((84m, "kg"), (lastWeight.GetProperty("value").GetDecimal(), lastWeight.GetProperty("unit").GetString())); // the latest reading; HbA1c is a lab, not a vital
    }

    [Fact]
    public async Task OnlyTheBannerIdentityComesWithTheChart()
    {
        using var r = await Client(h.Doctor).GetAsync(Chart(h.Patient));
        var body = await Json(r);
        Assert.Equal(["chart", "patient", "timeZone", "treatmentConsent"], body.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(["ageYears", "dateOfBirth", "fullName", "id", "mrn", "sexAtBirth"], body.GetProperty("patient").EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task AnotherTenantSeesNoFactsEvenWithTheFilterOff()
    {
        await SeedAsync(db => db.Allergies.Add(Allergy(h.Patient, AllergyKind.Substance, "Penicillin")));
        await using var other = sql.Encounters(Guid.NewGuid());
        Assert.Empty(await other.Allergies.ToListAsync());
        Assert.Empty(await other.Allergies.IgnoreQueryFilters().ToListAsync()); // layer 2: row-level security on its own
    }

    [Fact]
    public async Task EveryRequestIsAuditedWithThePatientAndPurpose()
    {
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using (var ok = await Client(h.Doctor).GetAsync(Chart(h.Patient))) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (var desk = await Client(h.Reception).GetAsync(Chart(h.Patient))) Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode);
        using (var colleague = await Client(h.SecondDoctor).GetAsync(Chart(h.Patient)))
        {
            var body = await Json(colleague);
            Assert.Equal((HttpStatusCode.Forbidden, "no_care_relationship"), (colleague.StatusCode, body.GetProperty("code").GetString()));
            Assert.DoesNotContain("Meera", body.ToString(), StringComparison.Ordinal);
        }
        using (var missing = await Client(h.Doctor).GetAsync(Chart(Guid.NewGuid()))) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.Action == "patient.chart.read").OrderBy(e => e.Id).Select(e => new { e.Outcome, e.PatientId, e.Purpose, e.ActorUserId }).ToListAsync();
        Assert.Equal([AuditOutcome.Allowed, AuditOutcome.Denied, AuditOutcome.Denied, AuditOutcome.NotFound], rows.Select(r => r.Outcome));
        Assert.Equal((h.Patient, AuditPurpose.Treatment, h.Doctor), (rows[0].PatientId, rows[0].Purpose, rows[0].ActorUserId));
        Assert.Equal(h.Patient, rows[1].PatientId); // the receptionist's attempt names the patient too
    }

    Task<HttpResponseMessage> Flags(Guid user, params Guid[] patients) => Client(user).PostAsJsonAsync("/v1/chart/allergy-flags", new { patientIds = patients });

    /// <summary>SAFETY, mutation check: let the flags skip the care-relationship check and the colleague's list shows the patient's allergy state.</summary>
    [Fact]
    public async Task TodaysAllergyFlagsAreOnlyForPatientsTheCliniciansMayTreatAndAreAudited()
    {
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        await SeedAsync(db => db.Allergies.Add(Allergy(h.Patient, AllergyKind.Substance, "Penicillin")));

        using var mine = await Flags(h.Doctor, h.Patient, h.Patient, Guid.NewGuid());
        var items = (await Json(mine)).GetProperty("items").EnumerateArray().ToList();
        // One answer for the patient (a repeated id is one), none for an id nobody knows; the state only, never a substance.
        Assert.Equal((HttpStatusCode.OK, 1), (mine.StatusCode, items.Count));
        Assert.Equal(["patientId", "state"], items[0].EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal("Listed", items[0].GetProperty("state").GetString());

        using var colleague = await Flags(h.SecondDoctor, h.Patient);
        Assert.Equal((HttpStatusCode.OK, 0), (colleague.StatusCode, (await Json(colleague)).GetProperty("items").GetArrayLength()));
        using var desk = await Flags(h.Reception, h.Patient);
        Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode);
        using var empty = await Flags(h.Doctor);
        using var many = await Flags(h.Doctor, [.. Enumerable.Range(0, 101).Select(_ => Guid.NewGuid())]);
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (empty.StatusCode, many.StatusCode));

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.Action == "patient.chart.allergy-flag").Select(e => new { e.PatientId, e.Purpose, e.ActorUserId }).ToListAsync();
        Assert.Equal([(h.Patient, AuditPurpose.Treatment, (Guid?)h.Doctor)], rows.Select(r => (r.PatientId, r.Purpose, r.ActorUserId)));
    }
}

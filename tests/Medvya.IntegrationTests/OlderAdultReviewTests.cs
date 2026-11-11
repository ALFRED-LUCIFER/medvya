using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
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
/// MDV-P2-037 on real PostgreSQL, over HTTP: a nurse or a doctor records the older adult review as a new row each time (never an edit), a score
/// outside its scale is refused and nothing is stored, the drop is worked out and flagged, another tenant sees nothing (EF filter and row-level
/// security), roles without the capability and colleagues without a care relationship are refused, and every request is audited with the
/// patient. The medicine review suggests from the chart's medicines for a doctor only, and never changes a medicine.
/// </summary>
public sealed class OlderAdultReviewTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await h.BookAsync(10, patient: h.Patient); // the doctor's care relationship with the patient
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

    static Uri Assessments(Guid patient) => new($"/v1/patients/{patient}/geriatric-assessments", UriKind.Relative);
    static Uri Review(Guid patient) => new($"/v1/patients/{patient}/medicine-review", UriKind.Relative);
    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    static readonly object Full = new
    {
        fallsLast12Months = 1, timedUpAndGoSeconds = 14.2m, lyingSystolic = 146, lyingDiastolic = 80, standingSystolic = 122, standingDiastolic = 72,
        clinicalFrailtyScale = 4, miniCog = 4, adl = 6, iadl = 5, phq2 = 1, nutritionNote = "Lost 3.5 kg in a year (synthetic)",
    };

    async Task<int> RowsAsync()
    {
        await using var db = sql.Encounters(h.Tenant);
        return await db.GeriatricAssessments.CountAsync();
    }

    [Fact]
    public async Task ANewAssessmentIsANewRowWithTheDropWorkedOutAndTheLatestComesFirst()
    {
        using (var first = await Client(h.Nurse).PostAsJsonAsync(Assessments(h.Patient), Full))
        {
            var body = await Json(first);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal((24, 8, true, 14.2m, true), (body.GetProperty("systolicDrop").GetInt32(), body.GetProperty("diastolicDrop").GetInt32(),
                body.GetProperty("orthostatic").GetBoolean(), body.GetProperty("timedUpAndGoSeconds").GetDecimal(), body.GetProperty("tugAtRisk").GetBoolean()));
        }
        h.Clock.Advance(TimeSpan.FromDays(30));
        using (var second = await Client(h.Doctor).PostAsJsonAsync(Assessments(h.Patient), new { clinicalFrailtyScale = 5 })) Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, await RowsAsync());

        using var list = await Client(h.Doctor).GetAsync(Assessments(h.Patient));
        var view = await Json(list);
        var items = view.GetProperty("assessments").EnumerateArray().ToList();
        Assert.Equal([5, 4], items.Select(i => i.GetProperty("clinicalFrailtyScale").GetInt32()));
        Assert.Equal("Dr. Arjun Rao (synthetic)", items[0].GetProperty("author").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("orthostatic").ValueKind); // not measured: no flag, never "normal"
        Assert.False(view.GetProperty("rulesSignedOff").GetBoolean());

        // No edit: there is no route that changes a recorded review.
        using var put = await Client(h.Doctor).PutAsJsonAsync(new Uri($"/v1/patients/{h.Patient}/geriatric-assessments/{items[0].GetProperty("id").GetGuid()}", UriKind.Relative), Full);
        Assert.True(put.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    /// <summary>SAFETY (mutation check: skip GeriatricRules.Validate in the service, and this fails).</summary>
    [Fact]
    public async Task AScoreOutsideItsScaleIsRefusedAndNothingIsStored()
    {
        using var r = await Client(h.Nurse).PostAsJsonAsync(Assessments(h.Patient), new { clinicalFrailtyScale = 10, miniCog = 6, timedUpAndGoSeconds = 1e9m, lyingSystolic = 140 });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var errors = (await Json(r)).GetProperty("errors");
        Assert.Equal(["bloodPressure", "clinicalFrailtyScale", "miniCog", "timedUpAndGoSeconds"], errors.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(0, await RowsAsync());
    }

    [Fact]
    public async Task AnotherTenantSeesNoAssessmentEvenWithTheFilterOff()
    {
        using (var r = await Client(h.Nurse).PostAsJsonAsync(Assessments(h.Patient), Full)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        await using var other = sql.Encounters(Guid.NewGuid());
        Assert.Empty(await other.GeriatricAssessments.ToListAsync());
        Assert.Empty(await other.GeriatricAssessments.IgnoreQueryFilters().ToListAsync()); // layer 2: row-level security on its own
    }

    /// <summary>SAFETY (mutation check: drop the clinical access check from either use case, or the capability from either route, and this fails).</summary>
    [Fact]
    public async Task OnlyCliniciansWithACareRelationshipReadOrRecordAndEveryRequestIsAudited()
    {
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using (var ok = await Client(h.Doctor).PostAsJsonAsync(Assessments(h.Patient), Full)) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (var desk = await Client(h.Reception).GetAsync(Assessments(h.Patient))) Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode);
        using (var coordinator = await Client(h.CareCoordinator).PostAsJsonAsync(Assessments(h.Patient), Full)) Assert.Equal(HttpStatusCode.Forbidden, coordinator.StatusCode);
        using (var colleague = await Client(h.SecondDoctor).PostAsJsonAsync(Assessments(h.Patient), Full))
            Assert.Equal((HttpStatusCode.Forbidden, "no_care_relationship"), (colleague.StatusCode, (await Json(colleague)).GetProperty("code").GetString()));
        using (var missing = await Client(h.Doctor).GetAsync(Assessments(Guid.NewGuid()))) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(1, await RowsAsync());

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.Action.StartsWith("patient.geriatric.")).OrderBy(e => e.Id).Select(e => new { e.Action, e.Outcome, e.PatientId, e.Purpose }).ToListAsync();
        Assert.Equal([("patient.geriatric.record", AuditOutcome.Allowed), ("patient.geriatric.read", AuditOutcome.Denied), ("patient.geriatric.record", AuditOutcome.Denied),
            ("patient.geriatric.record", AuditOutcome.Denied), ("patient.geriatric.read", AuditOutcome.NotFound)], rows.Select(r => (r.Action, r.Outcome)));
        Assert.All(rows.Take(4), r => Assert.Equal((h.Patient, AuditPurpose.Treatment), (r.PatientId!.Value, r.Purpose)));
    }

    async Task ChartAsync(int ageYears, params (string Name, int DaysAgo)[] medicines)
    {
        await using (var patients = sql.Patients(h.Tenant))
        {
            var row = await patients.Patients.SingleAsync(p => p.Id == h.Patient);
            row.DateOfBirth = DateOnly.FromDateTime(h.Clock.GetUtcNow().UtcDateTime).AddYears(-ageYears).AddDays(-10);
            await patients.SaveChangesAsync();
        }
        await using var db = sql.Encounters(h.Tenant);
        db.MedicationStatements.AddRange(medicines.Select(m => new MedicationStatement
        {
            Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Name = m.Name, State = MedicationState.Active, Source = ChartSource.ClinicianVerified,
            RecordedAt = h.Clock.GetUtcNow().AddDays(-m.DaysAgo),
        }));
        db.Observations.Add(new Observation { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Code = ObservationCode.Egfr, ValueHundredths = 5200, TakenAt = h.Clock.GetUtcNow().AddDays(-3), Source = ChartSource.ClinicianVerified });
        db.Observations.Add(new Observation { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Code = ObservationCode.Potassium, ValueHundredths = 540, TakenAt = h.Clock.GetUtcNow().AddDays(-3), Source = ChartSource.ClinicianVerified });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// SAFETY (mutation check: pass no potassium or no medicine start from PrescribingChart, or match chart medicines by anything but the formulary's
    /// ATC code, and this fails). The review reads; nothing on the chart or the prescription changes.
    /// </summary>
    [Fact]
    public async Task ADoctorGetsSuggestionsFromTheChartAndNoMedicineChanges()
    {
        await ChartAsync(76, ("Alprazolam 0.25 mg", 400), ("Pantoprazole 40 mg", 90), ("Ibuprofen 400 mg", 5), ("Losartan 50 mg", 400), ("Synthetic herbal tonic", 1));
        using var r = await Client(h.Doctor).GetAsync(Review(h.Patient));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await Json(r);
        Assert.Equal(["Benzodiazepine", "LongTermPpi", "RaasLowEgfrHighPotassium", "SystemicNsaid"],
            body.GetProperty("suggestions").EnumerateArray().Select(s => s.GetProperty("rule").GetString()).Order());
        Assert.Equal("NotOnFormulary", Assert.Single(body.GetProperty("notChecked").EnumerateArray()).GetProperty("reason").GetString());
        Assert.Equal((5, true, false), (body.GetProperty("medicines").GetInt32(), body.GetProperty("olderAdult").GetBoolean(), body.GetProperty("rulesSignedOff").GetBoolean()));

        await using var db = sql.Encounters(h.Tenant);
        Assert.All(await db.MedicationStatements.ToListAsync(), m => Assert.Equal((MedicationState.Active, (Guid?)null, (DateTimeOffset?)null), (m.State, m.ReviewedBy, m.StoppedAt)));
        Assert.Empty(await db.MedicationDecisions.ToListAsync());
        await using var rx = sql.Prescribing(h.Tenant);
        Assert.Empty(await rx.PrescriptionItems.ToListAsync());

        await using var audit = sql.Audit(h.Tenant);
        var row = await audit.Events.SingleAsync(e => e.Action == "patient.medicine-review.read");
        Assert.Equal((AuditOutcome.Allowed, (Guid?)h.Patient, "suggestions:4;not_checked:1"), (row.Outcome, row.PatientId, row.Detail));
    }

    /// <summary>SAFETY (mutation check: drop the clinical access check from MedicineReviewService, and this fails).</summary>
    [Fact]
    public async Task AYoungerPatientGetsNoReviewAndANurseTheDeskOrAColleagueWithoutCareIsRefused()
    {
        await ChartAsync(54, ("Alprazolam 0.25 mg", 400));
        using (var young = await Client(h.Doctor).GetAsync(Review(h.Patient)))
        {
            var body = await Json(young);
            Assert.Equal((false, 0), (body.GetProperty("olderAdult").GetBoolean(), body.GetProperty("suggestions").GetArrayLength()));
        }
        using (var nurse = await Client(h.Nurse).GetAsync(Review(h.Patient))) Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode); // Prescriptions & orders: X
        using (var desk = await Client(h.Reception).GetAsync(Review(h.Patient))) Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode);
        using (var missing = await Client(h.Doctor).GetAsync(Review(Guid.NewGuid()))) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using var colleague = await Client(h.SecondDoctor).GetAsync(Review(h.Patient));
        Assert.Equal((HttpStatusCode.Forbidden, "no_care_relationship"), (colleague.StatusCode, (await Json(colleague)).GetProperty("code").GetString()));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Encounters.Domain;
using Medvya.Prescribing.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-038 on real PostgreSQL, over HTTP: the care gaps come from this module's records, the child schedule and Prescribing's placed orders
/// and close only on a record; the doctor sets targets, records eye and foot checks and keeps goals; input outside its range is refused and
/// nothing is stored; another tenant sees nothing (EF filter and row-level security); roles without the capability and colleagues without a
/// care relationship are refused; every request is audited with the patient.
/// </summary>
public sealed class ChronicCareTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
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

    Uri Care(Guid? patient = null) => new($"/v1/patients/{patient ?? h.Patient}/chronic-care", UriKind.Relative);
    Uri Targets => new($"/v1/patients/{h.Patient}/care-targets", UriKind.Relative);
    Uri Checks => new($"/v1/patients/{h.Patient}/care-checks", UriKind.Relative);
    Uri Goals => new($"/v1/patients/{h.Patient}/care-goals", UriKind.Relative);
    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();
    DateTimeOffset Now => h.Clock.GetUtcNow();

    async Task<JsonElement> GetAsync(Guid user)
    {
        using var r = await Client(user).GetAsync(Care());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await Json(r);
    }

    static string[] Gaps(JsonElement care) => [.. care.GetProperty("gaps").EnumerateArray().Select(g => g.GetProperty("rule").GetString()!)];

    async Task DiabeticAsync()
    {
        await using var db = sql.Encounters(h.Tenant);
        db.Problems.Add(new Problem { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Icd10Code = "E11.9", Description = "Type 2 diabetes (synthetic)", Status = ProblemStatus.Active, Verification = ProblemVerification.Verified, Source = ChartSource.ClinicianVerified, RecordedAt = Now.AddYears(-2) });
        db.Observations.Add(new Observation { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Code = ObservationCode.HbA1c, ValueHundredths = 790, TakenAt = Now.AddDays(-40), Source = ChartSource.ClinicianVerified });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// SAFETY (mutation check: drop the IPlacedOrders read, the care check read or the observation read from ChronicCareService, and a gap here
    /// never opens or never closes).
    /// </summary>
    [Fact]
    public async Task GapsOpenFromTheChartAndCloseOnlyOnARecord()
    {
        var none = await GetAsync(h.Doctor);
        Assert.Equal((false, 0), (none.GetProperty("chronic").GetBoolean(), none.GetProperty("gaps").GetArrayLength())); // not due: no diabetes, no order, no fall

        await DiabeticAsync();
        await using (var rx = sql.Prescribing(h.Tenant))
        {
            rx.Orders.Add(new ClinicalOrder { Id = Guid.NewGuid(), TenantId = h.Tenant, FacilityId = h.Facility, PatientId = h.Patient, EncounterId = Guid.NewGuid(), Kind = OrderKind.Lab, Name = "Lipid profile", State = OrderState.Ordered, CreatedBy = h.Doctor, CreatedAt = Now.AddDays(-20), OrderedBy = h.Doctor, OrderedAt = Now.AddDays(-20) });
            rx.Orders.Add(new ClinicalOrder { Id = Guid.NewGuid(), TenantId = h.Tenant, FacilityId = h.Facility, PatientId = h.Patient, EncounterId = Guid.NewGuid(), Kind = OrderKind.Lab, Name = "TSH", State = OrderState.Draft, CreatedBy = h.Doctor, CreatedAt = Now.AddDays(-20) });
            await rx.SaveChangesAsync();
        }
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.GeriatricAssessments.Add(new GeriatricAssessment { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, FacilityId = h.Facility, FallsLast12Months = 1, RecordedBy = h.Nurse, AssessedAt = Now.AddDays(-10) });
            await db.SaveChangesAsync();
        }
        var care = await GetAsync(h.Doctor);
        Assert.True(care.GetProperty("chronic").GetBoolean());
        Assert.Equal(["DiabeticEyeExam", "DiabeticFootExam", "UrineAlbumin", "OrderNotResulted", "FallsReview"], Gaps(care)); // a draft order is not a placed one
        var order = care.GetProperty("gaps")[3];
        Assert.Equal(("Order", "Lipid profile"), (order.GetProperty("action").GetString(), order.GetProperty("item").GetString()));
        Assert.False(care.GetProperty("rulesSignedOff").GetBoolean());

        using (var eye = await Client(h.Doctor).PostAsJsonAsync(Checks, new { kind = "DiabeticEyeExam", doneOn = "2026-03-02" })) Assert.Equal(HttpStatusCode.NoContent, eye.StatusCode);
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.Observations.Add(new Observation { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Code = ObservationCode.UrineAlbuminCreatinineRatio, ValueHundredths = 4500, TakenAt = Now.AddDays(-1), Source = ChartSource.ClinicianVerified });
            db.Observations.Add(new Observation { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Code = ObservationCode.LdlCholesterol, ValueHundredths = 12200, TakenAt = Now.AddDays(-1), Source = ChartSource.ClinicianVerified });
            db.GeriatricAssessments.Add(new GeriatricAssessment { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, FacilityId = h.Facility, TimedUpAndGoTenths = 140, LyingSystolic = 140, LyingDiastolic = 80, StandingSystolic = 130, StandingDiastolic = 78, RecordedBy = h.Nurse, AssessedAt = Now.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        Assert.Equal(["DiabeticFootExam"], Gaps(await GetAsync(h.Doctor)));
    }

    [Fact]
    public async Task AChildsVaccinesDueAreGaps()
    {
        await using (var patients = sql.Patients(h.Tenant))
        {
            var row = await patients.Patients.SingleAsync(p => p.Id == h.Patient);
            row.DateOfBirth = DateOnly.FromDateTime(Now.UtcDateTime).AddYears(-5).AddDays(-10);
            await patients.SaveChangesAsync();
        }
        var gaps = (await GetAsync(h.Doctor)).GetProperty("gaps").EnumerateArray().ToList();
        Assert.NotEmpty(gaps);
        Assert.All(gaps, g => Assert.Equal(("VaccineDue", "AddToVisit"), (g.GetProperty("rule").GetString(), g.GetProperty("action").GetString())));
        Assert.All(gaps, g => Assert.False(string.IsNullOrEmpty(g.GetProperty("vaccine").GetString())));
    }

    [Fact]
    public async Task TheDoctorSetsATargetAndKeepsGoalsWithAuthorAndTime()
    {
        await DiabeticAsync();
        var hba1c = (await GetAsync(h.Doctor)).GetProperty("targets").EnumerateArray().Single(t => t.GetProperty("code").GetString() == "HbA1c");
        Assert.Equal((7.0m, true, 7.9m, false), (hba1c.GetProperty("limit").GetDecimal(), hba1c.GetProperty("isDefault").GetBoolean(), hba1c.GetProperty("latest").GetDecimal(), hba1c.GetProperty("met").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, (await GetAsync(h.Doctor)).GetProperty("targets").EnumerateArray().Single(t => t.GetProperty("code").GetString() == "Egfr").GetProperty("met").ValueKind); // no reading: no verdict

        using (var set = await Client(h.Doctor).PutAsJsonAsync(Targets, new { code = "HbA1c", limit = 8.0m }))
        {
            var body = await Json(set);
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
            Assert.Equal((8.0m, false, true, "Dr. Arjun Rao (synthetic)"), (body.GetProperty("limit").GetDecimal(), body.GetProperty("isDefault").GetBoolean(), body.GetProperty("met").GetBoolean(), body.GetProperty("setBy").GetString()));
        }
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        using (var again = await Client(h.Doctor).PutAsJsonAsync(Targets, new { code = "HbA1c", limit = 7.5m })) Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        await using (var db = sql.Encounters(h.Tenant)) Assert.Equal(2, await db.CareTargets.CountAsync()); // a change is a new row, the newest wins
        Assert.Equal(7.5m, (await GetAsync(h.Doctor)).GetProperty("targets").EnumerateArray().Single(t => t.GetProperty("code").GetString() == "HbA1c").GetProperty("limit").GetDecimal());

        Guid id;
        using (var add = await Client(h.Doctor).PostAsJsonAsync(Goals, new { text = "HbA1c below 7 % (synthetic)", target = "Review in 3 months", progressPercent = 35 }))
        {
            var body = await Json(add);
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
            id = body.GetProperty("id").GetGuid();
        }
        h.Clock.Advance(TimeSpan.FromDays(7));
        using (var move = await Client(h.SecondDoctor).PutAsJsonAsync(new Uri($"/v1/patients/{h.Patient}/care-goals/{id}", UriKind.Relative), new { progressPercent = 55 }))
            Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        var goal = Assert.Single((await GetAsync(h.Nurse)).GetProperty("goals").EnumerateArray()); // a nurse reads
        Assert.Equal((55, "Dr. Arjun Rao (synthetic)", "Review in 3 months"), (goal.GetProperty("progressPercent").GetInt32(), goal.GetProperty("createdBy").GetString(), goal.GetProperty("target").GetString()));
        Assert.Equal(Now, goal.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.NotEqual(goal.GetProperty("createdBy").GetString(), goal.GetProperty("updatedBy").GetString());

        using var missing = await Client(h.Doctor).PutAsJsonAsync(new Uri($"/v1/patients/{h.Patient}/care-goals/{Guid.NewGuid()}", UriKind.Relative), new { progressPercent = 10 });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var other = await Client(h.Doctor).PutAsJsonAsync(new Uri($"/v1/patients/{h.SecondPatient}/care-goals/{id}", UriKind.Relative), new { progressPercent = 90 });
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode); // a goal is found only on its own patient's route
    }

    /// <summary>SAFETY (mutation check: skip any validation in ChronicCareService, and this fails).</summary>
    [Fact]
    public async Task InputOutsideItsRangeIsRefusedAndNothingIsStored()
    {
        async Task Refused(HttpResponseMessage r, string field)
        {
            using (r)
            {
                Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                Assert.True((await Json(r)).GetProperty("errors").TryGetProperty(field, out _), field);
            }
        }
        await Refused(await Client(h.Doctor).PutAsJsonAsync(Targets, new { code = "HbA1c", limit = 30m }), "limit");
        await Refused(await Client(h.Doctor).PutAsJsonAsync(Targets, new { code = "HbA1c", limit = 1e12m }), "limit");
        await Refused(await Client(h.Doctor).PutAsJsonAsync(Targets, new { code = "Pulse", limit = 60m }), "code");
        await Refused(await Client(h.Doctor).PostAsJsonAsync(Checks, new { kind = "DiabeticFootExam", doneOn = "2026-12-01" }), "doneOn");
        await Refused(await Client(h.Doctor).PostAsJsonAsync(Checks, new { doneOn = "2026-10-01" }), "kind");
        await Refused(await Client(h.Doctor).PostAsJsonAsync(Goals, new { text = " ", progressPercent = 10 }), "text");
        await Refused(await Client(h.Doctor).PostAsJsonAsync(Goals, new { text = "Walk daily", progressPercent = 101 }), "progressPercent");
        await Refused(await Client(h.Doctor).PostAsJsonAsync(Goals, new { text = new string('x', CareGoal.MaxText + 1) }), "text");
        await using var db = sql.Encounters(h.Tenant);
        Assert.Equal((0, 0, 0), (await db.CareTargets.CountAsync(), await db.CareChecks.CountAsync(), await db.CareGoals.CountAsync()));
    }

    [Fact]
    public async Task AnotherTenantSeesNoTargetCheckOrGoalEvenWithTheFilterOff()
    {
        using (var r = await Client(h.Doctor).PutAsJsonAsync(Targets, new { code = "Egfr", limit = 45m })) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var r = await Client(h.Doctor).PostAsJsonAsync(Checks, new { kind = "DiabeticFootExam", doneOn = "2026-10-01" })) Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        using (var r = await Client(h.Doctor).PostAsJsonAsync(Goals, new { text = "Walk 150 min a week" })) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        await using var other = sql.Encounters(Guid.NewGuid());
        Assert.Equal((0, 0, 0), (await other.CareTargets.CountAsync(), await other.CareChecks.CountAsync(), await other.CareGoals.CountAsync()));
        // layer 2: row-level security on its own
        Assert.Equal((0, 0, 0), (await other.CareTargets.IgnoreQueryFilters().CountAsync(), await other.CareChecks.IgnoreQueryFilters().CountAsync(), await other.CareGoals.IgnoreQueryFilters().CountAsync()));
    }

    /// <summary>SAFETY (mutation check: drop the clinical access check from any use case, or the capability from any route, and this fails).</summary>
    [Fact]
    public async Task OnlyDoctorsWithACareRelationshipWriteCliniciansReadAndEveryRequestIsAudited()
    {
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        Guid goal;
        using (var ok = await Client(h.Doctor).PostAsJsonAsync(Goals, new { text = "No falls in 6 months" }))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            goal = (await Json(ok)).GetProperty("id").GetGuid();
        }
        using (var desk = await Client(h.Reception).GetAsync(Care())) Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode);
        using (var nurse = await Client(h.Nurse).PostAsJsonAsync(Goals, new { text = "Synthetic" })) Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode);
        using (var nurse = await Client(h.Nurse).PutAsJsonAsync(Targets, new { code = "HbA1c", limit = 8m })) Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode);
        using (var nurse = await Client(h.Nurse).PostAsJsonAsync(Checks, new { kind = "DiabeticEyeExam", doneOn = "2026-10-01" })) Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode);
        using (var nurse = await Client(h.Nurse).PutAsJsonAsync(new Uri($"/v1/patients/{h.Patient}/care-goals/{goal}", UriKind.Relative), new { progressPercent = 90 })) Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode);
        using (var colleague = await Client(h.SecondDoctor).PostAsJsonAsync(Goals, new { text = "Synthetic" }))
            Assert.Equal((HttpStatusCode.Forbidden, "no_care_relationship"), (colleague.StatusCode, (await Json(colleague)).GetProperty("code").GetString()));
        using (var colleague = await Client(h.SecondDoctor).GetAsync(Care())) Assert.Equal(HttpStatusCode.Forbidden, colleague.StatusCode);
        using (var colleague = await Client(h.SecondDoctor).PutAsJsonAsync(Targets, new { code = "HbA1c", limit = 8m })) Assert.Equal(HttpStatusCode.Forbidden, colleague.StatusCode);
        using (var colleague = await Client(h.SecondDoctor).PostAsJsonAsync(Checks, new { kind = "DiabeticEyeExam", doneOn = "2026-10-01" })) Assert.Equal(HttpStatusCode.Forbidden, colleague.StatusCode);
        using (var colleague = await Client(h.SecondDoctor).PutAsJsonAsync(new Uri($"/v1/patients/{h.Patient}/care-goals/{goal}", UriKind.Relative), new { progressPercent = 90 }))
            Assert.Equal(HttpStatusCode.Forbidden, colleague.StatusCode);
        using (var missing = await Client(h.Doctor).GetAsync(Care(Guid.NewGuid()))) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await using (var db = sql.Encounters(h.Tenant)) Assert.Equal((1, 0, 0), (await db.CareGoals.CountAsync(), await db.CareTargets.CountAsync(), await db.CareChecks.CountAsync()));

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.Action.StartsWith("patient.care")).OrderBy(e => e.Id).Select(e => new { e.Action, e.Outcome, e.PatientId, e.Purpose }).ToListAsync();
        Assert.Equal([("patient.care-goal.save", AuditOutcome.Allowed), ("patient.care.read", AuditOutcome.Denied), ("patient.care-goal.save", AuditOutcome.Denied),
            ("patient.care-target.set", AuditOutcome.Denied), ("patient.care-check.record", AuditOutcome.Denied), ("patient.care-goal.save", AuditOutcome.Denied),
            ("patient.care-goal.save", AuditOutcome.Denied), ("patient.care.read", AuditOutcome.Denied), ("patient.care-target.set", AuditOutcome.Denied),
            ("patient.care-check.record", AuditOutcome.Denied), ("patient.care-goal.save", AuditOutcome.Denied), ("patient.care.read", AuditOutcome.NotFound)],
            rows.Select(r => (r.Action, r.Outcome)));
        Assert.All(rows.SkipLast(1), r => Assert.Equal((h.Patient, AuditPurpose.Treatment), (r.PatientId!.Value, r.Purpose)));
    }
}

using System.Globalization;
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
/// MDV-P2-034 on real PostgreSQL, over HTTP: a doctor starts, re-dates and closes a pregnancy; the chart then says Pregnancy with the gestation,
/// due date and contacts, and Adult again once closed. Only one open pregnancy per patient. A nurse counsels the danger signs, records "not
/// pregnant" and the antenatal readings, but cannot start or close one. Another tenant sees nothing (EF filter and row-level security); the
/// desk, a care coordinator and a colleague without a care relationship are refused; the audit never holds a date or a count.
/// </summary>
public sealed class PregnancyTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;
    static readonly DateOnly Today = SchedulingHarness.Monday; // the harness clock, on the facility's (IST) date

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

    static Uri At(Guid patient, string tail = "") => new($"/v1/patients/{patient}/pregnancy{tail}", UriKind.Relative);

    Task<HttpResponseMessage> Start(Guid user, object body, Guid? patient = null) => Client(user).PostAsJsonAsync(At(patient ?? h.Patient), body);

    static object Lmp(DateOnly lmp) => new { lmp, gravida = 2, para = 1, abortions = 0, living = 1 };

    async Task<JsonElement> ChartAsync()
    {
        using var r = await Client(h.Doctor).GetAsync(new Uri($"/v1/patients/{h.Patient}/chart", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    static async Task<string?> Code(HttpResponseMessage r, HttpStatusCode status)
    {
        Assert.Equal(status, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        return status == HttpStatusCode.BadRequest ? body.GetProperty("errors").EnumerateObject().Select(e => $"{e.Name}:{e.Value[0].GetString()}").First() : body.GetProperty("code").GetString();
    }

    [Fact]
    public async Task ADoctorStartsAPregnancyAndTheChartShowsItUntilAnOutcomeClosesIt()
    {
        Assert.Equal("Adult", (await ChartAsync()).GetProperty("lifeStage").GetString());
        using (var r = await Start(h.Doctor, Lmp(Today.AddDays(-171))))
        {
            var open = (await Ok(r)).GetProperty("open");
            Assert.Equal((Today.AddDays(109).ToString("O", CultureInfo.InvariantCulture), "Lmp", "LmpOnly", 171), (open.GetProperty("dueDate").GetString(), open.GetProperty("datedBy").GetString(),
                open.GetProperty("datingReason").GetString(), open.GetProperty("gestationDays").GetInt32()));
            Assert.Equal((2, 1, 0, 1), (open.GetProperty("gravida").GetInt32(), open.GetProperty("para").GetInt32(), open.GetProperty("abortions").GetInt32(), open.GetProperty("living").GetInt32()));
            Assert.Equal("Today", open.GetProperty("contacts").EnumerateArray().Single(c => c.GetProperty("week").GetInt32() == 24).GetProperty("state").GetString());
            Assert.Equal((6, false), (open.GetProperty("dangerSigns").GetArrayLength(), open.GetProperty("rulesSignedOff").GetBoolean()));
        }
        var chart = await ChartAsync();
        Assert.Equal(("Pregnancy", "Obstetric"), (chart.GetProperty("lifeStage").GetString(), chart.GetProperty("historySections")[0].GetString()));

        using (var again = await Start(h.Doctor, Lmp(Today.AddDays(-30)))) Assert.Equal("already_open", await Code(again, HttpStatusCode.Conflict));
        using (var none = await Client(h.Doctor).PostAsJsonAsync(At(h.Patient, "/close"), new { })) Assert.Equal("outcome:required", await Code(none, HttpStatusCode.BadRequest));
        using (var closed = await Client(h.Doctor).PostAsJsonAsync(At(h.Patient, "/close"), new { outcome = "LiveBirth" }))
            Assert.Equal(JsonValueKind.Null, (await Ok(closed)).GetProperty("open").ValueKind);
        Assert.Equal("Adult", (await ChartAsync()).GetProperty("lifeStage").GetString());

        await using var db = sql.Encounters(h.Tenant);
        var row = await db.PregnancyEpisodes.SingleAsync();
        Assert.Equal((PregnancyOutcome.LiveBirth, h.Doctor), (row.Outcome, row.ClosedBy));
        using (var next = await Start(h.Doctor, Lmp(Today.AddDays(-20)))) await Ok(next); // a closed pregnancy does not block the next
    }

    [Fact]
    public async Task AScanThatMovesTheDueDateSaysWhyAndKeepsTheEarlierDate()
    {
        var lmp = Today.AddDays(-100);
        using (var r = await Start(h.Doctor, Lmp(lmp))) await Ok(r);
        // A scan 30 days ago showing 12 w 0 d (84 days): the pregnancy is 114 days on, 14 more than the LMP says; the line at 12 weeks is 7.
        using var dated = await Client(h.Doctor).PutAsJsonAsync(At(h.Patient, "/dating"), new { lmp, scanDate = Today.AddDays(-30), scanWeeks = 12, scanDays = 0 });
        var open = (await Ok(dated)).GetProperty("open");
        Assert.Equal((Today.AddDays(280 - 114).ToString("O", CultureInfo.InvariantCulture), "Scan", "ScanDiffers", lmp.AddDays(280).ToString("O", CultureInfo.InvariantCulture), 114),
            (open.GetProperty("dueDate").GetString(), open.GetProperty("datedBy").GetString(), open.GetProperty("datingReason").GetString(),
                open.GetProperty("previousDueDate").GetString(), open.GetProperty("gestationDays").GetInt32()));
    }

    [Theory]
    [InlineData("""{}""", "lmp:required")]
    [InlineData("""{"lmp":"2026-10-20"}""", "lmp:in_future")]
    [InlineData("""{"lmp":"2025-12-01"}""", "lmp:too_early")]
    [InlineData("""{"scanDate":"2026-10-01"}""", "scanWeeks:required")]
    [InlineData("""{"scanDate":"2026-10-01","scanWeeks":10,"scanDays":7}""", "scanDays:invalid")]
    [InlineData("""{"lmp":"2026-08-01","scanDate":"2026-07-01","scanWeeks":8}""", "scanDate:before_lmp")]
    [InlineData("""{"lmp":"2026-08-01","gravida":0}""", "gravida:invalid")]
    [InlineData("""{"lmp":"2026-08-01","gravida":2,"para":1,"abortions":1}""", "para:more_than_gravida")]
    public async Task ARefusedStartSaysWhyAndSavesNothing(string json, string expected)
    {
        using var r = await Client(h.Doctor).PostAsync(At(h.Patient), new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(expected, await Code(r, HttpStatusCode.BadRequest));
        await using var db = sql.Encounters(h.Tenant);
        Assert.Equal(0, await db.PregnancyEpisodes.CountAsync());
    }

    [Fact]
    public async Task ANurseCounselsRecordsNotPregnantAndReadingsButCannotStartOrClose()
    {
        using (var start = await Start(h.Nurse, Lmp(Today.AddDays(-60)))) Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
        using (var ruledOut = await Client(h.Nurse).PostAsync(At(h.Patient, "/ruled-out"), null))
            Assert.Equal(JsonValueKind.String, (await Ok(ruledOut)).GetProperty("ruledOut").GetProperty("recordedAt").ValueKind);

        using (var r = await Start(h.Doctor, Lmp(Today.AddDays(-150)))) await Ok(r);
        Assert.Equal(JsonValueKind.Null, (await ChartAsync()).GetProperty("pregnancy").GetProperty("ruledOut").ValueKind); // an open pregnancy wins
        using (var ruled = await Client(h.Nurse).PostAsync(At(h.Patient, "/ruled-out"), null)) Assert.Equal("pregnancy_open", await Code(ruled, HttpStatusCode.Conflict));
        using (var counselled = await Client(h.Nurse).PostAsync(At(h.Patient, "/danger-signs"), null))
            Assert.NotEqual(JsonValueKind.Null, (await Ok(counselled)).GetProperty("open").GetProperty("dangerSignsCounselledAt").ValueKind);
        using (var close = await Client(h.Nurse).PostAsJsonAsync(At(h.Patient, "/close"), new { outcome = "Miscarriage" })) Assert.Equal(HttpStatusCode.Forbidden, close.StatusCode);

        using (var noVisit = await Client(h.Nurse).PostAsJsonAsync(At(h.Patient, "/readings"), new { fundalHeightCm = 22 })) Assert.Equal("no_open_visit", await Code(noVisit, HttpStatusCode.Conflict));
        using (var blank = await Client(h.Nurse).PostAsJsonAsync(At(h.Patient, "/readings"), new { })) Assert.Equal("readings:required", await Code(blank, HttpStatusCode.BadRequest));
        using (var wild = await Client(h.Nurse).PostAsJsonAsync(At(h.Patient, "/readings"), new { fetalHeartRate = 400 })) Assert.Equal("fetalHeartRate:implausible", await Code(wild, HttpStatusCode.BadRequest));

        var visit = await h.BookAsync(11, patient: h.Patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new Medvya.Scheduling.Application.CheckIn());
        await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id));
        // 21 w 3 d today: the visit is linked to the 20-week contact.
        using (var ok = await Client(h.Nurse).PostAsJsonAsync(At(h.Patient, "/readings"), new { fundalHeightCm = 22, fetalHeartRate = 146 }))
            Assert.Equal("Done", (await Ok(ok)).GetProperty("open").GetProperty("contacts").EnumerateArray().Single(c => c.GetProperty("week").GetInt32() == 20).GetProperty("state").GetString());
        await using var db = sql.Encounters(h.Tenant);
        var readings = await db.Observations.Where(o => o.AppointmentId == visit.Id).OrderBy(o => o.Code).ToListAsync();
        Assert.Equal([(ObservationCode.FetalHeartRate, 14600), (ObservationCode.FundalHeight, 2200)], readings.Select(o => (o.Code, o.ValueHundredths))); // codes are stored by name
        Assert.All(readings, o => Assert.Equal((h.Nurse, ChartSource.ClinicianVerified, h.Facility), (o.RecordedBy!.Value, o.Source, o.FacilityId!.Value)));
    }

    [Fact]
    public async Task ANotPregnantStatementCountsOnlyOnTheDayItWasMade()
    {
        Assert.Equal(JsonValueKind.Null, (await ChartAsync()).GetProperty("pregnancy").GetProperty("ruledOut").ValueKind); // not recorded, never "not pregnant"
        using (var r = await Client(h.Doctor).PostAsync(At(h.Patient, "/ruled-out"), null)) await Ok(r);
        Assert.Equal("Dr. Arjun Rao (synthetic)", (await ChartAsync()).GetProperty("pregnancy").GetProperty("ruledOut").GetProperty("recordedBy").GetString());
        h.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(JsonValueKind.Null, (await ChartAsync()).GetProperty("pregnancy").GetProperty("ruledOut").ValueKind);
    }

    /// <summary>SAFETY, mutation check: drop the clinical access check in PregnancyService.StartAsync and the colleague's start goes through.</summary>
    [Fact]
    public async Task OnlyAClinicianWhoTreatsThePatientMayWriteAndEveryWriteIsAuditedWithoutItsValues()
    {
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using (var desk = await Client(h.Reception).PostAsync(At(h.Patient, "/ruled-out"), null)) Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode);
        using (var coordinator = await Start(h.CareCoordinator, Lmp(Today.AddDays(-60)))) Assert.Equal(HttpStatusCode.Forbidden, coordinator.StatusCode);
        using (var colleague = await Start(h.SecondDoctor, Lmp(Today.AddDays(-60))))
            Assert.Equal((HttpStatusCode.Forbidden, "no_care_relationship"), (colleague.StatusCode, (await colleague.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()));
        using (var missing = await Start(h.Doctor, Lmp(Today.AddDays(-60)), Guid.NewGuid())) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var ok = await Start(h.Doctor, Lmp(Today.AddDays(-60)))) await Ok(ok);

        await using (var db = sql.Encounters(h.Tenant)) Assert.Equal(1, await db.PregnancyEpisodes.CountAsync());
        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.Action == "pregnancy.start").ToListAsync();
        Assert.Contains(rows, r => r.Outcome == AuditOutcome.Allowed && r.PatientId == h.Patient && r.ActorUserId == h.Doctor && r.Purpose == AuditPurpose.Treatment);
        Assert.Contains(rows, r => r.Outcome == AuditOutcome.Denied && r.ActorUserId == h.SecondDoctor);
        var lmp = Today.AddDays(-60).ToString("O", CultureInfo.InvariantCulture);
        Assert.DoesNotContain(rows, r => JsonSerializer.Serialize(r).Contains(lmp, StringComparison.Ordinal)); // never a date
    }

    [Fact]
    public async Task AnotherTenantSeesNoPregnancyEvenWithTheFilterOff()
    {
        using (var r = await Start(h.Doctor, Lmp(Today.AddDays(-60)))) await Ok(r);
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.PregnancyRuledOuts.Add(new() { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.SecondPatient, FacilityId = h.Facility, RecordedBy = h.Nurse, RecordedAt = h.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        await using var other = sql.Encounters(Guid.NewGuid());
        Assert.Empty(await other.PregnancyEpisodes.ToListAsync());
        Assert.Empty(await other.PregnancyEpisodes.IgnoreQueryFilters().ToListAsync()); // layer 2: row-level security on its own
        Assert.Empty(await other.PregnancyRuledOuts.IgnoreQueryFilters().ToListAsync());
    }
}

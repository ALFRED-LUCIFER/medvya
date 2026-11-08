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
/// MDV-P2-005 on real PostgreSQL over HTTP: one newest-first list across encounters, results and medicines with source and author, filters that
/// combine, "Load earlier" that never repeats or skips an entry (even entries at the same instant), the trend series, and the same gate and audit
/// as the chart (reception refused, a colleague without a care relationship refused, every request audited with the patient).
/// </summary>
public sealed class PatientTimelineTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;
    static readonly DateTimeOffset T0 = new(2026, 9, 1, 4, 30, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await h.BookAsync(10, patient: h.Patient);
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

    Uri Timeline(string query = "") => new($"/v1/patients/{h.Patient}/timeline{query}", UriKind.Relative);

    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    async Task SeedAsync(Action<Medvya.Encounters.Infrastructure.Persistence.EncountersDbContext> add)
    {
        await using var db = sql.Encounters(h.Tenant);
        add(db);
        await db.SaveChangesAsync();
    }

    Encounter Visit(DateTimeOffset at, EncounterStatus status = EncounterStatus.Ended) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = h.Tenant, FacilityId = h.Facility, PatientId = h.Patient, AppointmentId = Guid.NewGuid(), LeadClinicianId = h.Doctor,
        Status = status, AuthorityMode = AuthorityMode.Medvya, StartedAt = at, EndedAt = status == EncounterStatus.Ended ? at.AddMinutes(20) : null,
    };

    Observation Lab(DateTimeOffset at, int hundredths, ChartSource source = ChartSource.SyncedFromHis) =>
        new() { Id = Guid.CreateVersion7(), TenantId = h.Tenant, PatientId = h.Patient, Code = ObservationCode.HbA1c, ValueHundredths = hundredths, TakenAt = at, Source = source };

    MedicationStatement Medicine(DateTimeOffset at, string name, ChartSource source = ChartSource.Imported) =>
        new() { Id = Guid.CreateVersion7(), TenantId = h.Tenant, PatientId = h.Patient, Name = name, Dose = "500 mg", State = MedicationState.Active, Source = source, RecordedAt = at };

    [Fact]
    public async Task OneNewestFirstListWithSourceAndAuthorAndFiltersThatCombine()
    {
        await SeedAsync(db =>
        {
            db.Encounters.Add(Visit(T0.AddDays(3)));
            db.Observations.Add(Lab(T0.AddDays(2), 740));
            db.MedicationStatements.Add(Medicine(T0.AddDays(1), "Metformin"));
        });
        using var all = await Client(h.Doctor).GetAsync(Timeline());
        var body = await Json(all);
        var entries = body.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal([("Encounter", "ClinicianVerified"), ("Lab", "SyncedFromHis"), ("Medicine", "Imported")], entries.Select(e => (e.GetProperty("kind").GetString()!, e.GetProperty("source").GetString()!)));
        Assert.Equal(("Consultation", "Dr. Arjun Rao (synthetic)"), (entries[0].GetProperty("code").GetString(), entries[0].GetProperty("author").GetString()));
        Assert.Equal(("HbA1c", "7.4 %"), (entries[1].GetProperty("code").GetString(), entries[1].GetProperty("detail").GetString()));
        Assert.Equal(("Metformin", "500 mg"), (entries[2].GetProperty("title").GetString(), entries[2].GetProperty("detail").GetString()));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("next").ValueKind);

        async Task<string[]> Kinds(string query) => [.. (await Json(await Client(h.Doctor).GetAsync(Timeline(query)))).GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("kind").GetString()!)];
        Assert.Equal(["Lab", "Medicine"], await Kinds("?kinds=Lab,Medicine"));
        Assert.Equal(["Medicine"], await Kinds("?kinds=Lab,Medicine&source=Imported")); // filters combine
        Assert.Empty(await Kinds("?kinds=Document")); // nothing yet: an empty answer, not an error
        Assert.Equal(["Encounter"], await Kinds("?source=ClinicianVerified"));
        foreach (var bad in new[] { "?kinds=Nope", "?source=Nope", "?before=junk" })
            using (var r = await Client(h.Doctor).GetAsync(Timeline(bad))) Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task LoadEarlierNeverRepeatsOrSkipsAnEntryEvenAtTheSameInstant()
    {
        var expected = new List<Guid>();
        await SeedAsync(db =>
        {
            for (var i = 0; i < 17; i++) { var e = Visit(T0.AddDays(i)); db.Encounters.Add(e); expected.Add(e.Id); }
            for (var i = 0; i < 17; i++) { var m = Medicine(T0.AddDays(i).AddHours(1), $"Medicine {i}"); db.MedicationStatements.Add(m); expected.Add(m.Id); }
            // Six results at one instant across the page boundary: ties are ordered by id.
            for (var i = 0; i < 6; i++) { var l = Lab(T0.AddDays(13).AddHours(1), 700 + i); db.Observations.Add(l); expected.Add(l.Id); }
        });
        var seen = new List<Guid>();
        string? next = null;
        var pages = 0;
        do
        {
            using var r = await Client(h.Doctor).GetAsync(Timeline(next is null ? "?limit=10" : $"?limit=10&before={next}"));
            var body = await Json(r);
            seen.AddRange(body.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
            next = body.GetProperty("next").GetString();
            pages++;
        } while (next is not null && pages < 20);

        Assert.Equal(expected.Count, seen.Count);
        Assert.Equal(expected.Order(), seen.Order()); // every entry exactly once
        Assert.Equal(4, pages); // 40 entries
    }

    [Fact]
    public async Task TrendsReturnTheLastReadingsOldestFirst()
    {
        await SeedAsync(db =>
        {
            for (var i = 0; i < 30; i++)
                db.Observations.Add(new() { Id = Guid.CreateVersion7(), TenantId = h.Tenant, PatientId = h.Patient, Code = ObservationCode.Weight, ValueHundredths = (7000 + i) , TakenAt = T0.AddDays(i), Source = ChartSource.ClinicianVerified });
        });
        using var r = await Client(h.Doctor).GetAsync(new Uri($"/v1/patients/{h.Patient}/trends", UriKind.Relative));
        var weight = (await Json(r)).GetProperty("trends").EnumerateArray().Single();
        var values = weight.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("value").GetDecimal()).ToList();
        Assert.Equal((24, 70.06m, 70.29m, "kg"), (values.Count, values[0], values[^1], weight.GetProperty("unit").GetString()));
    }

    [Fact]
    public async Task SameGateAndAuditAsTheChart()
    {
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using (var ok = await Client(h.Doctor).GetAsync(Timeline())) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (var desk = await Client(h.Reception).GetAsync(Timeline())) Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode);
        using (var colleague = await Client(h.SecondDoctor).GetAsync(Timeline()))
            Assert.Equal((HttpStatusCode.Forbidden, "no_care_relationship"), (colleague.StatusCode, (await Json(colleague)).GetProperty("code").GetString()));
        using (var trends = await Client(h.SecondDoctor).GetAsync(new Uri($"/v1/patients/{h.Patient}/trends", UriKind.Relative))) Assert.Equal(HttpStatusCode.Forbidden, trends.StatusCode);
        using (var missing = await Client(h.Doctor).GetAsync(new Uri($"/v1/patients/{Guid.NewGuid()}/timeline", UriKind.Relative))) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.Action == "patient.timeline.read" || e.Action == "patient.trends.read").OrderBy(e => e.Id).Select(e => new { e.Action, e.Outcome, e.PatientId, e.Purpose }).ToListAsync();
        Assert.Equal([AuditOutcome.Allowed, AuditOutcome.Denied, AuditOutcome.Denied, AuditOutcome.Denied, AuditOutcome.NotFound], rows.Select(r => r.Outcome));
        Assert.Equal(("patient.timeline.read", h.Patient, AuditPurpose.Treatment), (rows[0].Action, rows[0].PatientId, rows[0].Purpose));
    }
}

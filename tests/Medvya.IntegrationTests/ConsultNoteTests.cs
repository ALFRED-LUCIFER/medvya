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
/// MDV-P2-009 (backend) over HTTP on real PostgreSQL: autosave with a version so a stale save is refused, two saves racing, the template pin,
/// who may write, and audit rows that hold the patient and never the note text. Cross-tenant ids are in AuthorisationMatrixTests.
/// </summary>
public sealed class ConsultNoteTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    const string Text = "Synthetic cough for three days";
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;
    Guid encounter;

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
        var visit = await h.BookAsync(10, patient: h.Patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        using var started = await Client(h.Doctor).PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = visit.Id });
        encounter = (await Json(started)).GetProperty("id").GetGuid();
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

    Task<HttpResponseMessage> SaveAsync(Guid user, int baseVersion, object sections, string? templateKey = null, bool manual = false, Guid? id = null) =>
        Client(user).PutAsJsonAsync(U($"/v1/encounters/{id ?? encounter}/note"), new { templateKey, manual, baseVersion, sections });

    [Fact]
    public async Task ANewNoteIsEmptyThenAutosavesRaiseTheVersionAndReadBack()
    {
        using (var empty = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note")))
        {
            var v = await Json(empty);
            Assert.Equal(("general-opd", 1, "Draft", 0), (v.GetProperty("templateKey").GetString(), v.GetProperty("templateVersion").GetInt32(), v.GetProperty("status").GetString(), v.GetProperty("version").GetInt32()));
            Assert.Empty(v.GetProperty("sections").EnumerateObject());
        }
        using (var first = await SaveAsync(h.Doctor, 0, new { chiefComplaint = Text }, manual: true))
        {
            Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
            Assert.Equal(1, (await Json(first)).GetProperty("version").GetInt32());
        }
        using (var second = await SaveAsync(h.Doctor, 1, new { chiefComplaint = Text, plan = "Rest", subjective = "  " }))
            Assert.Equal(2, (await Json(second)).GetProperty("version").GetInt32());

        using var read = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note"));
        var note = await Json(read);
        Assert.Equal(2, note.GetProperty("version").GetInt32());
        Assert.False(note.GetProperty("manual").GetBoolean());
        Assert.Equal(["chiefComplaint", "plan"], note.GetProperty("sections").EnumerateObject().Select(p => p.Name).Order()); // a blank section is not stored
    }

    // SAFETY mutation check: remove the base-version comparison in ConsultNoteService.SaveAsync and this fails (the stale save would overwrite).
    [Fact]
    public async Task AStaleSaveIsRefusedAndTheNewerTextSurvives()
    {
        using (await SaveAsync(h.Doctor, 0, new { chiefComplaint = "Newer from tab one" })) { }
        using var stale = await SaveAsync(h.Doctor, 0, new { chiefComplaint = "Older from tab two" });
        var body = await Json(stale);
        Assert.Equal((HttpStatusCode.Conflict, "stale_note", 1), (stale.StatusCode, body.GetProperty("code").GetString(), body.GetProperty("currentVersion").GetInt32()));
        Assert.DoesNotContain("Older", body.ToString(), StringComparison.Ordinal); // the refusal never echoes text

        await using var db = sql.Encounters(h.Tenant);
        var saved = await db.ConsultNotes.SingleAsync();
        Assert.Equal(("Newer from tab one", 1), (saved.Sections["chiefComplaint"], saved.Version));
    }

    [Fact]
    public async Task TwoSavesOnTheSameVersionAtOnceLeaveExactlyOneWinner()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            using var r = await SaveAsync(h.Doctor, 0, new { chiefComplaint = $"Synthetic {i}" });
            return r.StatusCode;
        }));
        Assert.Equal(1, results.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(5, results.Count(s => s == HttpStatusCode.Conflict));
        await using var db = sql.Encounters(h.Tenant);
        Assert.Equal(1, (await db.ConsultNotes.SingleAsync()).Version);
    }

    [Fact]
    public async Task ANoteKeepsItsTemplateVersionAndRefusesSectionsOfAnotherTemplate()
    {
        // A new note starts on the latest version (2 since MDV-P2-042); StructuredNoteTests pins an older note to version 1.
        using (var diabetes = await SaveAsync(h.Doctor, 0, new { chiefComplaint = Text, selfMonitoring = "Synthetic log" }, "diabetes-review"))
        {
            var v = await Json(diabetes);
            Assert.Equal(("diabetes-review", 2), (v.GetProperty("templateKey").GetString(), v.GetProperty("templateVersion").GetInt32()));
        }
        using (var wrong = await SaveAsync(h.Doctor, 1, new { chiefComplaint = Text, homeBloodPressure = "x" })) // a hypertension section on a diabetes note
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Equal("unknown_section", (await Json(wrong)).GetProperty("errors").GetProperty("sections.homeBloodPressure")[0].GetString());
        }
        using (var switched = await SaveAsync(h.Doctor, 1, new { chiefComplaint = Text, homeBloodPressure = "Synthetic 130/80" }, "hypertension"))
        {
            var v = await Json(switched);
            Assert.Equal(("hypertension", 2), (v.GetProperty("templateKey").GetString(), v.GetProperty("version").GetInt32()));
        }
        using var bad = await SaveAsync(h.Doctor, 2, new { chiefComplaint = Text }, "nope");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using var tooLong = await SaveAsync(h.Doctor, 2, new { chiefComplaint = new string('a', ConsultNote.MaxSectionLength + 1) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task OnlyTheTreatingDoctorWritesAndAnUnknownEncounterIsNotFound()
    {
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using (var desk = await SaveAsync(h.Reception, 0, new { chiefComplaint = Text })) Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode);
        using (var nurse = await SaveAsync(h.Nurse, 0, new { chiefComplaint = Text })) Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode);
        using (var colleague = await SaveAsync(h.SecondDoctor, 0, new { chiefComplaint = Text }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, colleague.StatusCode); // no care relationship
            Assert.Equal("no_care_relationship", (await Json(colleague)).GetProperty("code").GetString());
        }
        using (var read = await Client(h.SecondDoctor).GetAsync(U($"/v1/encounters/{encounter}/note"))) Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        using (var unknown = await SaveAsync(h.Doctor, 0, new { chiefComplaint = Text }, id: Guid.NewGuid())) Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        await using var db = sql.Encounters(h.Tenant);
        Assert.False(await db.ConsultNotes.AnyAsync()); // nothing was written by a refused caller
    }

    [Fact]
    public async Task EveryCallIsAuditedWithThePatientAndNeverTheNoteText()
    {
        using (await SaveAsync(h.Doctor, 0, new { chiefComplaint = Text })) { }
        using (await SaveAsync(h.Doctor, 0, new { chiefComplaint = Text })) { } // stale
        using (await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note"))) { }
        using (await SaveAsync(h.Nurse, 1, new { chiefComplaint = Text })) { } // denied

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.ResourceType == "note").OrderBy(e => e.Id).ToListAsync();
        Assert.Equal([("note.save", AuditOutcome.Allowed), ("note.save", AuditOutcome.Rejected), ("note.read", AuditOutcome.Allowed), ("note.save", AuditOutcome.Denied)],
            rows.Select(r => (r.Action, r.Outcome)));
        Assert.All(rows.Where(r => r.Outcome == AuditOutcome.Allowed), r => Assert.Equal(h.Patient, r.PatientId));
        Assert.DoesNotContain(rows, r => JsonSerializer.Serialize(r).Contains("cough", StringComparison.Ordinal));
    }
}

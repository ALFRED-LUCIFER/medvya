using System.Globalization;
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
/// MDV-P2-009 over HTTP on real PostgreSQL: the Objective offer holds only verified nurse-intake readings of this visit, each with its source; earlier
/// signed notes of the same patient (and no one else's) are offered as snippets; both reads need the note's access and write one audit row without values.
/// </summary>
public sealed class ConsultNoteAssistTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    static readonly DateTimeOffset T0 = new(2026, 10, 19, 4, 35, 0, TimeSpan.Zero);
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;
    Guid encounter, appointment;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.NoKnownAllergiesAsync(h.Patient);
        await h.DoctorTemplateAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s =>
            {
                PatientEndpointTests.SignInFromHeaders(s, h.Tenant);
                s.AddSingleton<TimeProvider>(h.Clock);
            }));
        (encounter, appointment) = await StartAsync(10, h.Patient);
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    HttpClient Client(Guid user, bool reauth = false)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        if (reauth) client.DefaultRequestHeaders.Add("X-Test-Reauth", h.Clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return client;
    }

    static Uri U(string path) => new(path, UriKind.Relative);

    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    async Task<(Guid Encounter, Guid Appointment)> StartAsync(int hour, Guid patient)
    {
        var visit = await h.BookAsync(hour, patient: patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        using var started = await Client(h.Doctor).PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = visit.Id });
        Assert.True(started.IsSuccessStatusCode, await started.Content.ReadAsStringAsync());
        return ((await Json(started)).GetProperty("id").GetGuid(), visit.Id);
    }

    async Task SignAsync(Guid id, string complaint)
    {
        using var save = await Client(h.Doctor).PutAsJsonAsync(U($"/v1/encounters/{id}/note"), new { baseVersion = 0, sections = new { chiefComplaint = complaint, assessment = "Synthetic assessment", plan = "Synthetic plan" }, recordingConsent = "Declined" });
        Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
        using var signed = await Client(h.Doctor, reauth: true).PostAsJsonAsync(U($"/v1/encounters/{id}/note/sign"), new { baseVersion = 1 });
        Assert.True(signed.IsSuccessStatusCode, await signed.Content.ReadAsStringAsync());
    }

    Observation Reading(ObservationCode code, int value, VitalAlert? flag = null, bool kept = false, ChartSource source = ChartSource.ClinicianVerified, bool verified = true, Guid? visit = null, bool noVisit = false, DateTimeOffset? at = null) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = h.Tenant, PatientId = h.Patient, FacilityId = h.Facility, AppointmentId = noVisit ? null : visit ?? appointment, Code = code, ValueHundredths = value * 100,
        TakenAt = at ?? T0, Source = source, RecordedBy = h.SecondDoctor, VerifiedBy = verified ? h.SecondDoctor : null, Flag = flag, FlagKept = kept,
    };

    [Fact]
    public async Task OnlyVerifiedIntakeReadingsOfThisVisitAreOfferedEachWithItsSourceAndTheLatestOfAKind()
    {
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.Observations.AddRange(
                Reading(ObservationCode.Pulse, 70), Reading(ObservationCode.Pulse, 88, at: T0.AddMinutes(5)),    // a re-measure: the later one only
                Reading(ObservationCode.BloodPressureSystolic, 150, VitalAlert.High, kept: true),                // flagged, kept on purpose: final
                Reading(ObservationCode.Temperature, 39, VitalAlert.High),                                       // flagged, not decided: not final
                Reading(ObservationCode.Weight, 70, source: ChartSource.PatientReported),                        // not clinician-verified
                Reading(ObservationCode.OxygenSaturation, 97, verified: false),                                  // nobody verified it
                Reading(ObservationCode.BloodPressureDiastolic, 90, visit: Guid.NewGuid()),                      // another visit
                Reading(ObservationCode.HbA1c, 7, source: ChartSource.SyncedFromHis, verified: false, noVisit: true));
            await db.SaveChangesAsync();
        }

        using var r = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note/prefill"));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        var vitals = (await Json(r)).GetProperty("vitals").EnumerateArray().ToList();
        Assert.Equal(["BloodPressureSystolic", "Pulse"], vitals.Select(v => v.GetProperty("code").GetString()));
        Assert.Equal([150m, 88m], vitals.Select(v => v.GetProperty("value").GetDecimal()));
        Assert.Equal([T0, T0.AddMinutes(5)], vitals.Select(v => v.GetProperty("takenAt").GetDateTimeOffset()));
        Assert.All(vitals, v => Assert.Equal("ClinicianVerified", v.GetProperty("source").GetString()));
        Assert.Equal(["High", null], vitals.Select(v => v.GetProperty("flag").GetString())); // MDV-1090: the kept abnormal reading still says it was flagged
        Assert.All(vitals, v => Assert.Equal("Dr. S. Menon (synthetic)", v.GetProperty("recordedByName").GetString())); // who recorded it, named
    }

    [Fact]
    public async Task WithNothingVerifiedTheOfferIsEmptyNeverGuessed()
    {
        using var r = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note/prefill"));
        Assert.Empty((await Json(r)).GetProperty("vitals").EnumerateArray());
    }

    [Fact]
    public async Task SignedNotesOfTheSamePatientAreOfferedAndNeverTheCurrentDraftOrAnotherPatients()
    {
        using (var draft = await Client(h.Doctor).PutAsJsonAsync(U($"/v1/encounters/{encounter}/note"), new { baseVersion = 0, sections = new { chiefComplaint = "Synthetic unsigned draft" } })) Assert.True(draft.IsSuccessStatusCode);
        using (await Client(h.Doctor).PostAsync(U($"/v1/encounters/{encounter}/end"), null)) { } // one open consultation at a time
        var (earlier, _) = await StartAsync(9, h.Patient);
        await SignAsync(earlier, "Synthetic earlier cough");
        using (await Client(h.Doctor).PostAsync(U($"/v1/encounters/{earlier}/end"), null)) { }
        await h.NoKnownAllergiesAsync(h.SecondPatient); // signing needs the allergy state recorded (MDV audit P0-2)
        var (other, _) = await StartAsync(11, h.SecondPatient);
        await SignAsync(other, "Synthetic other patient's complaint");

        using var r = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note/previous"));
        var body = await r.Content.ReadAsStringAsync();
        var only = Assert.Single(JsonDocument.Parse(body).RootElement.GetProperty("notes").EnumerateArray());
        Assert.Equal(earlier, only.GetProperty("encounterId").GetGuid());
        Assert.Equal("Synthetic earlier cough", only.GetProperty("sections").GetProperty("chiefComplaint").GetString());
        Assert.DoesNotContain("unsigned", body);
        Assert.DoesNotContain("other patient", body);
    }

    [Fact]
    public async Task OnlyTheTreatingDoctorReadsEitherOfferAndEachReadIsAuditedWithThePatientAndNoValues()
    {
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.Observations.Add(Reading(ObservationCode.Pulse, 88));
            db.Intakes.Add(new Intake { Id = Guid.CreateVersion7(), TenantId = h.Tenant, FacilityId = h.Facility, PatientId = h.Patient, AppointmentId = appointment, ChiefComplaint = Complaint, ChiefComplaintBy = h.Nurse, ChiefComplaintAt = T0, UpdatedAt = T0 });
            await db.SaveChangesAsync();
        }
        foreach (var path in new[] { "prefill", "previous" })
        {
            // MDV-P1-104: the nurse's complaint reaches only someone with clinical access to the encounter (its treating doctor here).
            using (var colleague = await Client(h.SecondDoctor).GetAsync(U($"/v1/encounters/{encounter}/note/{path}")))
                Assert.Equal((HttpStatusCode.Forbidden, false), (colleague.StatusCode, (await colleague.Content.ReadAsStringAsync()).Contains("sore throat", StringComparison.Ordinal)));
            using (var nurse = await Client(h.Nurse).GetAsync(U($"/v1/encounters/{encounter}/note/{path}"))) Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode);
            using (var unknown = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{Guid.NewGuid()}/note/{path}"))) Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            using (var own = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note/{path}"))) Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        }

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.Action == "note.prefill" || e.Action == "note.previous").ToListAsync();
        var allowed = rows.Where(r => r.Outcome == AuditOutcome.Allowed).ToList();
        Assert.Equal(["note.prefill", "note.previous"], allowed.Select(r => r.Action).Order());
        Assert.All(allowed, r => Assert.Equal(h.Patient, r.PatientId));
        Assert.Equal(4, rows.Count(r => r.Outcome == AuditOutcome.Denied));
        Assert.DoesNotContain(allowed, r => JsonSerializer.Serialize(r).Contains("Pulse", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => JsonSerializer.Serialize(r).Contains("sore throat", StringComparison.Ordinal));
    }

    // MDV-P1-104: the nurse's chief complaint and the note's record of where its own came from.
    const string Complaint = "Fever and sore throat for three days (synthetic)";

    async Task NurseSavesComplaintAsync(string text)
    {
        using var saved = await Client(h.Nurse).PutAsJsonAsync(U($"/v1/intake/{appointment}"), new { chiefComplaint = text, vitals = Array.Empty<object>() });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
    }

    async Task<JsonElement> SaveNoteAsync(int baseVersion, string complaint)
    {
        using var r = await Client(h.Doctor).PutAsJsonAsync(U($"/v1/encounters/{encounter}/note"), new { baseVersion, sections = new { chiefComplaint = complaint, assessment = "Synthetic assessment", plan = "Synthetic plan" }, recordingConsent = "Declined" });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return await Json(r);
    }

    static string? Source(JsonElement view) => view.GetProperty("chiefComplaintSource") is { ValueKind: JsonValueKind.Object } s ? s.GetProperty("source").GetString() : null;

    [Fact]
    public async Task TheNursesComplaintIsOfferedWithWhoAndWhenAndTheSignedNoteRecordsThatTheDoctorEditedIt()
    {
        await using (var identity = sql.Identity())
        {
            identity.Users.Add(new Medvya.Identity.Domain.StaffUser { Id = h.Nurse, Issuer = "test-idp", Subject = h.Nurse.ToString(), DisplayName = "Nurse Kavya (synthetic)" });
            await identity.SaveChangesAsync();
        }
        await NurseSavesComplaintAsync(Complaint);

        using (var r = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note/prefill")))
        {
            var offered = (await Json(r)).GetProperty("chiefComplaint");
            Assert.Equal(Complaint, offered.GetProperty("text").GetString());
            Assert.Equal("Nurse Kavya (synthetic)", offered.GetProperty("byName").GetString());
            Assert.Equal(h.Clock.GetUtcNow(), offered.GetProperty("at").GetDateTimeOffset());
        }

        var first = await SaveNoteAsync(0, Complaint);
        Assert.Equal("NurseIntake", Source(first));
        Assert.Equal("Nurse Kavya (synthetic)", first.GetProperty("chiefComplaintSource").GetProperty("byName").GetString());
        Assert.Equal("EditedByDoctor", Source(await SaveNoteAsync(1, Complaint + ", worse at night")));

        using var signed = await Client(h.Doctor, reauth: true).PostAsJsonAsync(U($"/v1/encounters/{encounter}/note/sign"), new { baseVersion = 2 });
        Assert.True(signed.IsSuccessStatusCode, await signed.Content.ReadAsStringAsync());
        Assert.Equal("EditedByDoctor", Source(await Json(signed)));
        await using (var db = sql.Encounters(h.Tenant))
        {
            var row = await db.SignedNoteVersions.SingleAsync(v => v.EncounterId == encounter);
            Assert.Equal((ComplaintSource.EditedByDoctor, h.Nurse, h.Clock.GetUtcNow()), (row.ChiefComplaintSource, row.ChiefComplaintBy, row.ChiefComplaintAt));
            Assert.Equal(SignedNoteVersion.Hash(row.TemplateKey, row.TemplateVersion, row.Sections, complaintSource: (row.ChiefComplaintSource!.Value, row.ChiefComplaintBy, row.ChiefComplaintAt), atSigning: row.AtSigning), row.ContentHash);
        }
        // Tenant isolation: the complaint and the source stay inside this clinic, even with the EF filter off (row-level security).
        await using var elsewhere = sql.Encounters(Guid.NewGuid());
        Assert.Empty(await elsewhere.Intakes.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await elsewhere.ConsultNotes.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task AComplaintTheDoctorTypedIsNeverReplacedOrMarkedWhenTheNurseSavesLater()
    {
        Assert.Null(Source(await SaveNoteAsync(0, "Cough since Monday (doctor, synthetic)")));
        await NurseSavesComplaintAsync(Complaint);

        using var r = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note"));
        var view = await Json(r);
        Assert.Equal("Cough since Monday (doctor, synthetic)", view.GetProperty("sections").GetProperty("chiefComplaint").GetString());
        Assert.Null(Source(view));
        // Even the nurse's exact words over the doctor's own text are the doctor's choice, never marked as the nurse's.
        Assert.Null(Source(await SaveNoteAsync(1, Complaint)));
    }

    [Fact]
    public async Task WithNurseIntakeOffThereIsNoComplaintToOfferAndNothingIsMarked()
    {
        // Nurse intake is off by default: no intake row, so nothing is offered and the doctor's text is the doctor's own.
        using var r = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note/prefill"));
        Assert.Equal(JsonValueKind.Null, (await Json(r)).GetProperty("chiefComplaint").ValueKind);
        using var note = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note"));
        Assert.Null(Source(await Json(note)));
        Assert.Null(Source(await SaveNoteAsync(0, Complaint)));
    }
}

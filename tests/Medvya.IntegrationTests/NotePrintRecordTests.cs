using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Encounters.Domain;
using Medvya.Scheduling.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-049 over HTTP on real PostgreSQL: a signed note's print and PDF show the visit record kept at signing (verified vitals with who and when,
/// the allergy state in words, the diagnoses coded in the visit, the prescription lines authorised before signing), never a later value; a note
/// signed before the record was kept says so instead of reading today's chart. Synthetic data only.
/// </summary>
public sealed class NotePrintRecordTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
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
        var visit = await h.BookAsync(10, patient: h.Patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        using var started = await Client(h.Doctor).PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = visit.Id });
        Assert.True(started.IsSuccessStatusCode, await started.Content.ReadAsStringAsync());
        (encounter, appointment) = ((await Json(started)).GetProperty("id").GetGuid(), visit.Id);
        // Signing needs identity confirmed with the patient (MDV-P1-109).
        using var confirmed = await Client(h.Doctor).PostAsync(U($"/v1/encounters/{encounter}/identity-confirmation"), null);
        Assert.True(confirmed.IsSuccessStatusCode);
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

    async Task SignAsync()
    {
        using var save = await Client(h.Doctor).PutAsJsonAsync(U($"/v1/encounters/{encounter}/note"),
            new { baseVersion = 0, sections = new { chiefComplaint = "Synthetic cough", assessment = "Synthetic assessment", plan = "Synthetic plan" }, recordingConsent = "Declined" });
        Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
        using var signed = await Client(h.Doctor, reauth: true).PostAsJsonAsync(U($"/v1/encounters/{encounter}/note/sign"), new { baseVersion = 1 });
        Assert.True(signed.IsSuccessStatusCode, await signed.Content.ReadAsStringAsync());
    }

    async Task<JsonElement> AtSigningAsync()
    {
        using var r = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await Json(r)).GetProperty("atSigning");
    }

    async Task<string> PdfTextAsync()
    {
        using var r = await Client(h.Doctor).PostAsync(U($"/v1/encounters/{encounter}/note/pdf"), null);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return string.Join(' ', PdfPages.Read(await r.Content.ReadAsByteArrayAsync(), PlainPdf.A4.Margin).Select(p => p.Text));
    }

    Observation Pulse(int value, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = h.Tenant, PatientId = h.Patient, FacilityId = h.Facility, AppointmentId = appointment, Code = ObservationCode.Pulse,
        ValueHundredths = value * 100, TakenAt = at, Source = ChartSource.ClinicianVerified, RecordedBy = h.SecondDoctor, VerifiedBy = h.SecondDoctor, // the harness names doctors, not the nurse
    };

    Allergy Substance(string name) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = h.Tenant, PatientId = h.Patient, FacilityId = h.Facility, Kind = AllergyKind.Substance, Substance = name, Reaction = "Rash",
        Status = AllergyStatus.Confirmed, Source = ChartSource.ClinicianVerified, RecordedAt = h.Clock.GetUtcNow(), RecordedBy = h.Nurse,
    };

    Problem Diagnosis(string code, string description) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = h.Tenant, PatientId = h.Patient, Icd10Code = code, CodeSystem = "ICD-10", CodeSystemVersion = "2019-medvya-starter-1",
        Description = description, Verification = ProblemVerification.Verified, Status = ProblemStatus.Active, Source = ChartSource.ClinicianVerified,
        RecordedAt = h.Clock.GetUtcNow(), EncounterId = encounter, RecordedBy = h.Doctor,
    };

    // AC1 and AC3. SAFETY mutation check: make ConsultNoteService.ViewAsync or SignedNotePdfDocument read today's chart instead of AtSigning and this fails.
    [Fact]
    public async Task ThePrintShowsVitalsAllergyAndDiagnosisAsAtSigningWithTheirSourcesNeverALaterValue()
    {
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.Observations.Add(Pulse(72, T0));
            db.Allergies.Add(Substance("Penicillin (synthetic)"));
            db.Problems.Add(Diagnosis("J06.9", "Upper respiratory infection (synthetic)"));
            db.MedicationStatements.Add(new MedicationStatement
            {
                Id = Guid.CreateVersion7(), TenantId = h.Tenant, PatientId = h.Patient, Name = "Paracetamol (synthetic)", Dose = "500 mg", State = MedicationState.Active,
                Source = ChartSource.ClinicianVerified, RecordedAt = h.Clock.GetUtcNow(), EncounterId = encounter, PrescriptionId = Guid.NewGuid(),
            });
            // A replaced or cancelled prescription version's line is stopped, never printed as prescribed.
            db.MedicationStatements.Add(new MedicationStatement
            {
                Id = Guid.CreateVersion7(), TenantId = h.Tenant, PatientId = h.Patient, Name = "Ibuprofen (synthetic)", Dose = "400 mg", State = MedicationState.Stopped,
                Source = ChartSource.ClinicianVerified, RecordedAt = h.Clock.GetUtcNow(), EncounterId = encounter, PrescriptionId = Guid.NewGuid(), StoppedAt = h.Clock.GetUtcNow(),
            });
            await db.SaveChangesAsync();
        }
        await SignAsync();

        // After signing: the pulse is corrected (a re-measure is a new row), a second allergy and a second diagnosis are recorded.
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.Observations.Add(Pulse(88, T0.AddMinutes(10)));
            db.Allergies.Add(Substance("Sulfonamide (synthetic)"));
            db.Problems.Add(Diagnosis("I10", "Hypertension (synthetic)"));
            await db.SaveChangesAsync();
        }

        var record = await AtSigningAsync();
        var vital = Assert.Single(record.GetProperty("vitals").EnumerateArray());
        Assert.Equal(("Pulse", 72m, "/min", T0), (vital.GetProperty("code").GetString(), vital.GetProperty("value").GetDecimal(), vital.GetProperty("unit").GetString(), vital.GetProperty("takenAt").GetDateTimeOffset()));
        Assert.Equal("ClinicianVerified", vital.GetProperty("source").GetString());
        Assert.False(string.IsNullOrEmpty(vital.GetProperty("recordedByName").GetString())); // who took it
        Assert.Equal("Listed", record.GetProperty("allergies").GetProperty("state").GetString());
        Assert.Equal("Penicillin (synthetic)", Assert.Single(record.GetProperty("allergies").GetProperty("allergies").EnumerateArray()).GetProperty("substance").GetString());
        var diagnosis = Assert.Single(record.GetProperty("diagnoses").EnumerateArray());
        Assert.Equal(("J06.9", "Verified", "ClinicianVerified"), (diagnosis.GetProperty("icd10Code").GetString(), diagnosis.GetProperty("verification").GetString(), diagnosis.GetProperty("source").GetString()));
        Assert.Equal("Paracetamol (synthetic)", Assert.Single(record.GetProperty("medicines").EnumerateArray()).GetProperty("name").GetString());

        var pdf = await PdfTextAsync();
        foreach (var expected in new[] { "Recorded at signing", "Pulse 72 /min", "nurse intake, verified", "Penicillin (synthetic) (Rash) · Verified · Clinician verified", "J06.9", "Paracetamol (synthetic) 500 mg · prescribed at this visit" })
            Assert.Contains(expected, pdf, StringComparison.Ordinal);
        foreach (var later in new[] { "Pulse 88", "Sulfonamide", "I10", "Ibuprofen" })
            Assert.DoesNotContain(later, pdf, StringComparison.Ordinal);

        // The record is covered by the signature's hash and, as the rest of the ledger row, cannot be rewritten.
        await using (var db = sql.Encounters(h.Tenant))
        {
            var row = await db.SignedNoteVersions.AsNoTracking().SingleAsync();
            Assert.Equal(SignedNoteVersion.Hash(row.TemplateKey, row.TemplateVersion, row.Sections, atSigning: row.AtSigning), row.ContentHash);
            Assert.NotEqual(SignedNoteVersion.Hash(row.TemplateKey, row.TemplateVersion, row.Sections), row.ContentHash);
        }
        await using var conn = await sql.OpenAs("app_user", h.Tenant);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""UPDATE encounters."SignedNoteVersions" SET "AtSigning" = NULL""", conn).ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.RestrictViolation, ex.SqlState);
    }

    // AC2: "no known allergies" at signing stays that, in words; an allergy recorded later does not appear. No vitals: said so, never blank.
    [Fact]
    public async Task NoKnownAllergiesAtSigningIsSaidInWordsAndALaterAllergyDoesNotReplaceIt()
    {
        await SignAsync();
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.Allergies.Add(Substance("Penicillin (synthetic)"));
            await db.SaveChangesAsync();
        }

        var record = await AtSigningAsync();
        Assert.Equal("NoKnownAllergies", record.GetProperty("allergies").GetProperty("state").GetString());
        Assert.Empty(record.GetProperty("allergies").GetProperty("allergies").EnumerateArray());
        Assert.Empty(record.GetProperty("vitals").EnumerateArray());

        var pdf = await PdfTextAsync();
        foreach (var expected in new[] { "No known allergies", "No verified vitals for this visit.", "No problems recorded." })
            Assert.Contains(expected, pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("Penicillin", pdf, StringComparison.Ordinal);
    }

    // AC2: a note signed before the record was kept prints that, never today's chart and never "none" or "not recorded" for what was simply not kept.
    [Fact]
    public async Task ANoteSignedBeforeTheRecordWasKeptSaysSoAndDoesNotReadTodaysChart()
    {
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.Observations.Add(Pulse(72, T0));
            var sections = new Dictionary<string, string> { ["chiefComplaint"] = "Synthetic cough", ["assessment"] = "Synthetic", ["plan"] = "Synthetic" };
            var note = new ConsultNote
            {
                Id = Guid.CreateVersion7(), TenantId = h.Tenant, EncounterId = encounter, TemplateKey = "general-opd", TemplateVersion = 1, Status = NoteStatus.Signed,
                Sections = sections, Version = 2, UpdatedBy = h.Doctor, UpdatedAt = T0,
            };
            db.ConsultNotes.Add(note);
            db.SignedNoteVersions.Add(new SignedNoteVersion
            {
                Id = Guid.CreateVersion7(), TenantId = h.Tenant, EncounterId = encounter, NoteId = note.Id, VersionNo = 1, TemplateKey = "general-opd", TemplateVersion = 1,
                Sections = sections, ContentHash = SignedNoteVersion.Hash("general-opd", 1, sections), SignedBy = h.Doctor, SignedAt = T0,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(JsonValueKind.Null, (await AtSigningAsync()).ValueKind);
        var pdf = await PdfTextAsync();
        Assert.Contains("This note was signed before vitals, allergies and diagnoses were kept with the signature", pdf, StringComparison.Ordinal);
        foreach (var live in new[] { "Pulse 72", "No known allergies", "Allergies not recorded" })
            Assert.DoesNotContain(live, pdf, StringComparison.Ordinal);
    }
}

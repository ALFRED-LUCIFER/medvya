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
using Xunit;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-042 over HTTP on real PostgreSQL: the structured review of systems and examination by system (stored canonical in the note's
/// sections, an abnormal finding refused without its note, a blank kept blank through signing and the PDF), and the template a new note starts on.
/// </summary>
public sealed class StructuredNoteTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    const string Diabetes = "diabetes-review";
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
        using var started = await Client().PostAsJsonAsync(U("/v1/encounters"), new { appointmentId = visit.Id });
        encounter = (await Json(started)).GetProperty("id").GetGuid();
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    HttpClient Client(bool reauth = false)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", h.Doctor.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        if (reauth) client.DefaultRequestHeaders.Add("X-Test-Reauth", h.Clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return client;
    }

    static Uri U(string path) => new(path, UriKind.Relative);

    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    Task<HttpResponseMessage> SaveAsync(int baseVersion, Dictionary<string, string> sections, string templateKey = Diabetes) =>
        Client().PutAsJsonAsync(U($"/v1/encounters/{encounter}/note"), new { templateKey, manual = true, baseVersion, sections, recordingConsent = "Declined" });

    static Dictionary<string, string> Note(string ros, string exam) => new()
    {
        ["chiefComplaint"] = "Synthetic sugar review", ["assessment"] = "Synthetic assessment", ["plan"] = "Synthetic plan",
        [NoteTemplates.ReviewOfSystems] = ros, [NoteTemplates.Examination] = exam,
    };

    static async Task<IReadOnlyDictionary<string, string[]>> ErrorsAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        return (await Json(r)).GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }

    [Fact]
    public async Task AnswersAreStoredInTemplateOrderABlankStaysBlankAndTheSignedNoteAndPdfSayThemInWords()
    {
        // Out of order, a padded note, and most items not answered.
        using var saved = await SaveAsync(0, Note(
            """{"tinglingFeet":{"answer":"yes"},"chestPain":{"answer":"no"}}""",
            """{"nervousSystem":{"answer":"notExamined"},"feet":{"answer":"abnormal","note":"  Reduced monofilament both soles  "},"heart":{"answer":"normal","note":" "}}"""));
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var view = await Json(saved);
        Assert.Equal((Diabetes, 2), (view.GetProperty("templateKey").GetString(), view.GetProperty("templateVersion").GetInt32()));

        using var signed = await Client(reauth: true).PostAsJsonAsync(U($"/v1/encounters/{encounter}/note/sign"), new { baseVersion = 1 });
        Assert.True(signed.IsSuccessStatusCode, await signed.Content.ReadAsStringAsync());
        await using (var db = sql.Encounters(h.Tenant))
        {
            var row = await db.SignedNoteVersions.SingleAsync();
            // SAFETY mutation check: default an unanswered item to normal (or "no") in StructuredAnswers.Normalise and these fail.
            Assert.Equal("""{"chestPain":{"answer":"no"},"tinglingFeet":{"answer":"yes"}}""", row.Sections[NoteTemplates.ReviewOfSystems]);
            Assert.Equal("""{"heart":{"answer":"normal"},"feet":{"answer":"abnormal","note":"Reduced monofilament both soles"},"nervousSystem":{"answer":"notExamined"}}""",
                row.Sections[NoteTemplates.Examination]);
            Assert.Equal(SignedNoteVersion.Hash(Diabetes, 2, row.Sections), row.ContentHash);
        }

        using var pdf = await Client().PostAsync(U($"/v1/encounters/{encounter}/note/pdf"), null);
        Assert.True(pdf.IsSuccessStatusCode, await pdf.Content.ReadAsStringAsync());
        var text = string.Join('\n', PdfPages.Read(await pdf.Content.ReadAsByteArrayAsync(), PlainPdf.A4.Margin).Select(p => p.Text));
        foreach (var line in new[] { "Review of systems", "Chest pain: No", "Tingling feet: Yes", "Examination by system", "Heart: Normal", "Feet: Abnormal. Reduced monofilament both soles", "Nervous system: Not examined" })
            Assert.Contains(line, text, StringComparison.Ordinal);
        foreach (var blank in new[] { "Tiredness", "Lungs", "Abdomen", "{\"" })
            Assert.DoesNotContain(blank, text, StringComparison.Ordinal);
    }

    // SAFETY mutation check: remove the note_required rule in StructuredAnswers.Normalise and this fails.
    [Fact]
    public async Task AnAbnormalFindingWithoutItsNoteIsRefusedAndNothingIsSaved()
    {
        foreach (var note in new[] { "", ",\"note\":\"   \"" })
        {
            using var r = await SaveAsync(0, Note("{}", "{\"feet\":{\"answer\":\"abnormal\"" + note + "}}"));
            Assert.Equal(["note_required"], (await ErrorsAsync(r))["sections.examination.feet"]);
        }
        await using var db = sql.Encounters(h.Tenant);
        Assert.False(await db.ConsultNotes.AnyAsync());
    }

    [Fact]
    public async Task AnUnknownItemAWrongAnswerOrBrokenTextIsRefused()
    {
        using (var r = await SaveAsync(0, Note("""{"fever":{"answer":"yes"},"chestPain":{"answer":"normal"}}""", """{"heart":{"answer":"yes"},"feet":{"answer":7}}""")))
        {
            var errors = await ErrorsAsync(r);
            Assert.Equal(["unknown_item"], errors["sections.reviewOfSystems.fever"]); // fever is asked in antenatal and paediatrics, not here
            Assert.Equal(["invalid"], errors["sections.reviewOfSystems.chestPain"]);
            Assert.Equal(["invalid"], errors["sections.examination.heart"]);
            Assert.Equal(["invalid"], errors["sections.examination.feet"]);
        }
        foreach (var broken in new[] { "not json", "[1]", "\"yes\"" })
        {
            using var r = await SaveAsync(0, Note(broken, "{}"));
            Assert.Equal(["invalid"], (await ErrorsAsync(r))["sections.reviewOfSystems"]);
        }
        // Nothing answered is no section at all; a released version without structured sections refuses them as unknown.
        using (var empty = await SaveAsync(0, Note("{}", "{}")))
            Assert.False((await Json(empty)).GetProperty("sections").TryGetProperty(NoteTemplates.ReviewOfSystems, out _));
        using var old = await SaveAsync(1, Note("""{"chestPain":{"answer":"no"}}""", "{}"), "general-opd");
        Assert.Equal(["unknown_section"], (await ErrorsAsync(old))["sections.reviewOfSystems"]);
    }

    [Fact]
    public async Task ANewNoteStartsOnTheSuggestedTemplateAndTheDoctorsChoiceWins()
    {
        using (var plain = await Client().GetAsync(U($"/v1/encounters/{encounter}/note")))
        {
            var v = await Json(plain);
            Assert.Equal(("general-opd", JsonValueKind.Null), (v.GetProperty("templateKey").GetString(), v.GetProperty("suggestedTemplateKey").ValueKind));
        }
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.Problems.Add(new Problem { Id = Guid.NewGuid(), TenantId = h.Tenant, PatientId = h.Patient, Description = "Type 2 diabetes (synthetic)", Icd10Code = "E11.9", Verification = ProblemVerification.Verified, Source = ChartSource.ClinicianVerified, RecordedAt = h.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        using (var suggested = await Client().GetAsync(U($"/v1/encounters/{encounter}/note")))
        {
            var v = await Json(suggested);
            Assert.Equal((Diabetes, 2, Diabetes), (v.GetProperty("templateKey").GetString(), v.GetProperty("templateVersion").GetInt32(), v.GetProperty("suggestedTemplateKey").GetString()));
        }
        using (var chosen = await SaveAsync(0, new() { ["chiefComplaint"] = "Synthetic cough" }, "general-opd"))
            Assert.True(chosen.IsSuccessStatusCode, await chosen.Content.ReadAsStringAsync());
        using var after = await Client().GetAsync(U($"/v1/encounters/{encounter}/note"));
        var a = await Json(after);
        Assert.Equal(("general-opd", JsonValueKind.Null), (a.GetProperty("templateKey").GetString(), a.GetProperty("suggestedTemplateKey").ValueKind));
    }

    [Fact]
    public async Task ANoteWrittenOnAnOlderVersionKeepsItAndItsTextSections()
    {
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.ConsultNotes.Add(new ConsultNote
            {
                Id = Guid.NewGuid(), TenantId = h.Tenant, EncounterId = encounter, TemplateKey = Diabetes, TemplateVersion = 1, Status = NoteStatus.Draft, Version = 1,
                Sections = new() { ["chiefComplaint"] = "Synthetic sugar review" }, UpdatedBy = h.Doctor, UpdatedAt = h.Clock.GetUtcNow(),
            });
            await db.SaveChangesAsync();
        }
        using (var ok = await SaveAsync(1, new() { ["chiefComplaint"] = "Synthetic sugar review", ["selfMonitoring"] = "Synthetic log" }))
        {
            var v = await Json(ok);
            Assert.Equal((Diabetes, 1), (v.GetProperty("templateKey").GetString(), v.GetProperty("templateVersion").GetInt32()));
        }
        using var structured = await SaveAsync(2, Note("""{"chestPain":{"answer":"no"}}""", "{}"));
        Assert.Equal(["unknown_section"], (await ErrorsAsync(structured))["sections.reviewOfSystems"]);
    }

    [Theory]
    [InlineData("2016-11-12", new string[0], "paediatrics")] // 10 years old
    [InlineData("2008-11-13", new string[0], "paediatrics")] // 18 tomorrow
    [InlineData("2008-11-12", new string[0], null)] // 18 today: an adult
    [InlineData("1990-01-01", new[] { "E11.9" }, "diabetes-review")]
    [InlineData("1990-01-01", new[] { "e10.65" }, "diabetes-review")]
    [InlineData("1990-01-01", new[] { "E16.2", "I10" }, null)] // not diabetes
    [InlineData("1990-01-01", new[] { "Z34.9", "E11.9" }, "antenatal")] // a pregnancy wins over diabetes
    [InlineData("2010-05-01", new[] { "O24.4" }, "antenatal")] // and over a child's age
    [InlineData("1961-11-12", new string[0], "older-adult-review")] // 65 today
    [InlineData("1961-11-13", new[] { "E11.9" }, "diabetes-review")] // 64
    [InlineData("1950-01-01", new[] { "E11.9" }, "older-adult-review")]
    public void TheSuggestionFollowsTheLifeStageThenTheProblems(string dob, string[] codes, string? expected) =>
        Assert.Equal(expected, NoteTemplates.Suggest(DateOnly.Parse(dob, CultureInfo.InvariantCulture), new DateOnly(2026, 11, 12), [.. codes, null]));
}

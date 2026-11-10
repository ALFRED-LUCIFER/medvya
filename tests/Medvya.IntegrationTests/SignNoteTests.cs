using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-012 (backend) over HTTP on real PostgreSQL: a verified doctor with a care relationship and a re-authentication in the last 5 minutes
/// signs a complete note into an immutable version 1; afterwards nothing edits it, at the API or at the database. Synthetic data only.
/// </summary>
public sealed class SignNoteTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    const string Text = "Synthetic cough for three days";
    static readonly object Complete = new { chiefComplaint = Text, assessment = "Synthetic viral cold", plan = "Rest and fluids" };
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

    /// <summary>Re-authenticated <paramref name="minutesAgo"/> minutes before the test clock; null: never.</summary>
    HttpClient Client(Guid user, int? minutesAgo = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        if (minutesAgo is { } m) client.DefaultRequestHeaders.Add("X-Test-Reauth", h.Clock.GetUtcNow().AddMinutes(-m).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return client;
    }

    static Uri U(string path) => new(path, UriKind.Relative);

    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    async Task<int> SaveDraftAsync(object sections, int baseVersion = 0)
    {
        using var r = await Client(h.Doctor).PutAsJsonAsync(U($"/v1/encounters/{encounter}/note"), new { baseVersion, sections });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await Json(r)).GetProperty("version").GetInt32();
    }

    Task<HttpResponseMessage> SignAsync(Guid user, int baseVersion, int? reauthMinutesAgo = 0, Guid? id = null) =>
        Client(user, reauthMinutesAgo).PostAsJsonAsync(U($"/v1/encounters/{id ?? encounter}/note/sign"), new { baseVersion });

    async Task<ConsultNote> NoteAsync()
    {
        await using var db = sql.Encounters(h.Tenant);
        return await db.ConsultNotes.AsNoTracking().SingleAsync();
    }

    async Task<int> SignedRowsAsync()
    {
        await using var db = sql.Encounters(h.Tenant);
        return await db.SignedNoteVersions.CountAsync();
    }

    async Task<Guid?> BrokenChainRowAsync()
    {
        await using var conn = await sql.OpenAs("app_user", h.Tenant);
        return await new NpgsqlCommand($"""SELECT ledger.verify_chain('encounters.SignedNoteVersions', '{h.Tenant}')""", conn).ExecuteScalarAsync() is Guid g ? g : null;
    }

    [Fact]
    public async Task ASignedNoteFreezesVersionOneWithItsHashSignerAndTimeAndIsAudited()
    {
        var version = await SaveDraftAsync(Complete);
        using var signed = await SignAsync(h.Doctor, version);
        Assert.True(signed.IsSuccessStatusCode, await signed.Content.ReadAsStringAsync());
        var view = await Json(signed);
        Assert.Equal(("Signed", 1), (view.GetProperty("status").GetString(), view.GetProperty("signature").GetProperty("versionNo").GetInt32()));
        Assert.Equal(h.Doctor, view.GetProperty("signature").GetProperty("signedBy").GetGuid());

        await using var db = sql.Encounters(h.Tenant);
        var row = await db.SignedNoteVersions.SingleAsync();
        Assert.Equal(("general-opd", 1, h.Doctor, Text), (row.TemplateKey, row.TemplateVersion, row.SignedBy, row.Sections["chiefComplaint"]));
        Assert.Equal(SignedNoteVersion.Hash("general-opd", 1, row.Sections), row.ContentHash); // the stored hash verifies against the stored content
        Assert.Equal(row.ContentHash, view.GetProperty("signature").GetProperty("contentHash").GetString());
        Assert.Null(await BrokenChainRowAsync()); // the ledger chain is intact

        using var read = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note"));
        var again = await Json(read);
        Assert.Equal(("Signed", row.SignedAt), (again.GetProperty("status").GetString(), again.GetProperty("signature").GetProperty("signedAt").GetDateTimeOffset()));

        await using var audit = sql.Audit(h.Tenant);
        var sign = await audit.Events.SingleAsync(e => e.Action == "note.sign");
        Assert.Equal((h.Patient, AuditOutcome.Allowed), (sign.PatientId, sign.Outcome));
        Assert.DoesNotContain("cough", JsonSerializer.Serialize(sign), StringComparison.Ordinal);
    }

    // SAFETY mutation check: remove the ReauthenticatedAt test in ConsultNoteService.SignAsync and the first two cases fail.
    [Fact]
    public async Task SigningNeedsAReauthenticationInTheLastFiveMinutes()
    {
        var version = await SaveDraftAsync(Complete);
        foreach (var minutes in new int?[] { null, 6 })
        {
            using var refused = await SignAsync(h.Doctor, version, minutes);
            Assert.Equal((HttpStatusCode.Forbidden, "reauth_required"), (refused.StatusCode, (await Json(refused)).GetProperty("code").GetString()));
        }
        Assert.Equal((NoteStatus.Draft, 0), ((await NoteAsync()).Status, await SignedRowsAsync()));
        using var ok = await SignAsync(h.Doctor, version, 4);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task ABlankRequiredSectionBlocksSigningWithItsCodeAndNothingIsWritten()
    {
        var version = await SaveDraftAsync(new { chiefComplaint = Text, plan = "  " }); // assessment never entered, plan blank
        using var blocked = await SignAsync(h.Doctor, version);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        var errors = (await Json(blocked)).GetProperty("errors");
        Assert.Equal("required", errors.GetProperty("sections.assessment")[0].GetString());
        Assert.Equal("required", errors.GetProperty("sections.plan")[0].GetString());
        Assert.Equal((NoteStatus.Draft, 0), ((await NoteAsync()).Status, await SignedRowsAsync()));
    }

    [Fact]
    public async Task OnlyADoctorWithACareRelationshipSigns()
    {
        var version = await SaveDraftAsync(Complete);
        foreach (var user in new[] { h.Reception, h.Nurse })
        {
            using var r = await SignAsync(user, version);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using (var colleague = await SignAsync(h.SecondDoctor, version))
        {
            Assert.Equal(HttpStatusCode.Forbidden, colleague.StatusCode);
            Assert.Equal("no_care_relationship", (await Json(colleague)).GetProperty("code").GetString());
        }
        using (var unknown = await SignAsync(h.Doctor, version, id: Guid.NewGuid())) Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal((NoteStatus.Draft, 0), ((await NoteAsync()).Status, await SignedRowsAsync()));
    }

    // SAFETY mutation check: remove the Status == Signed guard in ConsultNoteService.SaveAsync and the 409 case fails (the trigger would answer 500);
    // drop the TR_ConsultNotes_SignedImmutable trigger (migration SignedNotes) and the direct-database cases fail.
    [Fact]
    public async Task ASignedNoteCannotBeEditedByTheApiOrTheDatabase()
    {
        var version = await SaveDraftAsync(Complete);
        using (await SignAsync(h.Doctor, version)) { }
        var signed = await NoteAsync();

        using (var edit = await Client(h.Doctor).PutAsJsonAsync(U($"/v1/encounters/{encounter}/note"), new { baseVersion = signed.Version, sections = new { chiefComplaint = "Changed" } }))
            Assert.Equal(("note_signed", HttpStatusCode.Conflict), ((await Json(edit)).GetProperty("code").GetString(), edit.StatusCode));

        await using (var db = sql.Encounters(h.Tenant)) // the repository path: even code that skips the service is refused
        {
            var tracked = await db.ConsultNotes.SingleAsync();
            tracked.Sections = new Dictionary<string, string> { ["chiefComplaint"] = "Changed" };
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = sql.Encounters(h.Tenant))
        {
            db.ConsultNotes.Remove(await db.ConsultNotes.SingleAsync());
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var conn = await sql.OpenAs("app_user", h.Tenant)) // raw SQL on the signed version: the ledger refuses
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""UPDATE encounters."SignedNoteVersions" SET "Sections" = '{}'""", conn).ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.RestrictViolation, ex.SqlState);
        }
        var after = await NoteAsync();
        Assert.Equal((Text, NoteStatus.Signed), (after.Sections["chiefComplaint"], after.Status));
    }

    [Fact]
    public async Task SigningTwiceOrAtOnceWritesOneSignatureAndTheRetryGetsTheSignedNote()
    {
        var version = await SaveDraftAsync(Complete);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var r = await SignAsync(h.Doctor, version);
            return (r.StatusCode, (await Json(r)).GetProperty("status").GetString());
        }));
        Assert.All(results, r => Assert.Equal((HttpStatusCode.OK, "Signed"), r));
        using (var again = await SignAsync(h.Doctor, version)) Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, await SignedRowsAsync());
        await using var audit = sql.Audit(h.Tenant);
        Assert.Equal(1, await audit.Events.CountAsync(e => e.Action == "note.sign" && e.Outcome == AuditOutcome.Allowed));
    }

    [Fact]
    public async Task ASaveSinceTheEditorReadTheNoteIsRefusedAsStaleNotSigned()
    {
        var version = await SaveDraftAsync(Complete);
        await SaveDraftAsync(Complete, version);
        using var stale = await SignAsync(h.Doctor, version);
        Assert.Equal(("stale_note", HttpStatusCode.Conflict), ((await Json(stale)).GetProperty("code").GetString(), stale.StatusCode));
        Assert.Equal(0, await SignedRowsAsync());
    }

    async Task<JsonElement[]> PendingAsync(Guid user)
    {
        using var r = await Client(user).GetAsync(U("/v1/notes/pending"));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        var body = await Json(r);
        Assert.False(body.GetProperty("more").GetBoolean()); // well under the cap
        return [.. body.GetProperty("items").EnumerateArray()];
    }

    // MDV-P2-002 SAFETY mutation checks: drop the lead-clinician filter in ConsultNoteService.PendingAsync and the colleague sees it; drop the
    // signed filter and the signed note stays listed.
    [Fact]
    public async Task AnUnsignedNoteStaysOnItsDoctorsPendingListAcrossDaysUntilSigned()
    {
        var started = Assert.Single(await PendingAsync(h.Doctor)); // started, nothing typed yet: still a consultation without a signed note
        Assert.Equal((encounter, false), (started.GetProperty("encounterId").GetGuid(), started.GetProperty("drafted").GetBoolean()));
        Assert.False(string.IsNullOrEmpty(started.GetProperty("patientName").GetString()));
        Assert.Empty(await PendingAsync(h.SecondDoctor)); // a colleague who may treat the patient still sees only their own

        var version = await SaveDraftAsync(Complete);
        h.Clock.Advance(TimeSpan.FromDays(2));
        var later = Assert.Single(await PendingAsync(h.Doctor));
        Assert.True(later.GetProperty("drafted").GetBoolean());
        Assert.DoesNotContain("cough", later.ToString(), StringComparison.Ordinal); // no note text in the list

        using (var signed = await SignAsync(h.Doctor, version)) Assert.True(signed.IsSuccessStatusCode, await signed.Content.ReadAsStringAsync());
        Assert.Empty(await PendingAsync(h.Doctor));

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.Action == "note.pending").ToListAsync();
        Assert.Equal(2, rows.Count); // one per note answered, none for the empty answers
        Assert.All(rows, r => Assert.Equal((h.Patient, h.Doctor, AuditOutcome.Allowed), (r.PatientId!.Value, r.ActorUserId!.Value, r.Outcome)));
    }

    [Fact]
    public async Task OnlyARoleThatWritesNotesHasAPendingList()
    {
        foreach (var user in new[] { h.Reception, h.Nurse })
        {
            using var r = await Client(user).GetAsync(U("/v1/notes/pending"));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
    }

    // ---- MDV-P2-013: addenda and the PDF ----

    async Task SignedNoteAsync()
    {
        using var signed = await SignAsync(h.Doctor, await SaveDraftAsync(Complete));
        Assert.True(signed.IsSuccessStatusCode, await signed.Content.ReadAsStringAsync());
    }

    Task<HttpResponseMessage> AddendumAsync(Guid user, string? text, string? reason, int? reauthMinutesAgo = 0) =>
        Client(user, reauthMinutesAgo).PostAsJsonAsync(U($"/v1/encounters/{encounter}/note/addenda"), new { text, reason });

    Task<HttpResponseMessage> PdfAsync(Guid user) => Client(user).PostAsync(U($"/v1/encounters/{encounter}/note/pdf"), null);

    async Task<Guid?> BrokenChainAsync(string table)
    {
        await using var conn = await sql.OpenAs("app_user", h.Tenant);
        return await new NpgsqlCommand($"""SELECT ledger.verify_chain('encounters.{table}', '{h.Tenant}')""", conn).ExecuteScalarAsync() is Guid g ? g : null;
    }

    [Fact]
    public async Task AnAddendumIsANewSignedVersionAndNeverAltersVersionOne()
    {
        await SignedNoteAsync();
        SignedNoteVersion v1;
        await using (var db = sql.Encounters(h.Tenant)) v1 = await db.SignedNoteVersions.AsNoTracking().SingleAsync();
        var note = await NoteAsync();

        foreach (var (text, reason) in new[] { ("Synthetic: dose corrected to 500 mg", "Dose typed wrongly"), ("Synthetic: lab result reviewed", "New result") })
        {
            using var r = await AddendumAsync(h.Doctor, text, reason);
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
            h.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        using var read = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/note"));
        var view = await Json(read);
        Assert.Equal(Text, view.GetProperty("sections").GetProperty("chiefComplaint").GetString()); // the original, as signed
        Assert.Equal(1, view.GetProperty("signature").GetProperty("versionNo").GetInt32());
        var addenda = view.GetProperty("addenda").EnumerateArray().ToList();
        Assert.Equal([(2, 1, "Dose typed wrongly"), (3, 2, "New result")],
            addenda.Select(a => (a.GetProperty("versionNo").GetInt32(), a.GetProperty("amendsVersionNo").GetInt32(), a.GetProperty("reason").GetString())));
        Assert.Equal("Synthetic: dose corrected to 500 mg", addenda[0].GetProperty("text").GetString());

        await using (var db = sql.Encounters(h.Tenant))
        {
            var rows = await db.SignedNoteVersions.AsNoTracking().OrderBy(v => v.VersionNo).ToListAsync();
            Assert.Equal((v1.ContentHash, v1.SignedAt, Text), (rows[0].ContentHash, rows[0].SignedAt, rows[0].Sections["chiefComplaint"])); // v1 untouched
            Assert.Equal((rows[0].Id, rows[1].Id), (rows[1].AmendsVersionId!.Value, rows[2].AmendsVersionId!.Value));
            foreach (var a in rows.Skip(1)) // each stored hash verifies against its text, link and reason
                Assert.Equal(SignedNoteVersion.Hash(a.TemplateKey, a.TemplateVersion, a.Sections, a.AmendsVersionId, a.Reason), a.ContentHash);
            Assert.NotEqual(SignedNoteVersion.Hash(rows[1].TemplateKey, rows[1].TemplateVersion, rows[1].Sections, rows[1].AmendsVersionId, "Another reason"), rows[1].ContentHash);
        }
        var after = await NoteAsync();
        Assert.Equal((note.Version, note.UpdatedAt, NoteStatus.Signed), (after.Version, after.UpdatedAt, after.Status));
        Assert.Null(await BrokenChainAsync("SignedNoteVersions"));

        await using var audit = sql.Audit(h.Tenant);
        var events = await audit.Events.Where(e => e.Action == "note.addendum").ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal((h.Patient, AuditOutcome.Allowed), (e.PatientId, e.Outcome)));
        Assert.DoesNotContain("500 mg", JsonSerializer.Serialize(events), StringComparison.Ordinal);
    }

    // SAFETY mutation checks: remove RequireReauth() from ConsultNoteService.AddAddendumAsync and the re-authentication cases fail; remove the
    // reason check and the blank-reason cases fail; remove its clinical.RequireAsync call and the colleague case fails.
    [Fact]
    public async Task AnAddendumNeedsASignedNoteATextAReasonAndTheSameGateAsSigning()
    {
        using (var draft = await AddendumAsync(h.Doctor, "Synthetic", "Synthetic"))
            Assert.Equal((HttpStatusCode.Conflict, "note_not_signed"), (draft.StatusCode, (await Json(draft)).GetProperty("code").GetString()));
        await SignedNoteAsync();

        foreach (var minutes in new int?[] { null, 6 })
        {
            using var refused = await AddendumAsync(h.Doctor, "Synthetic correction", "Synthetic reason", minutes);
            Assert.Equal((HttpStatusCode.Forbidden, "reauth_required"), (refused.StatusCode, (await Json(refused)).GetProperty("code").GetString()));
        }
        foreach (var (text, reason, field, code) in new (string?, string?, string, string)[] { ("Synthetic correction", " ", "reason", "required"),
                     ("Synthetic correction", null, "reason", "required"), ("", "Synthetic reason", "text", "required"), ("Synthetic correction", new string('r', 501), "reason", "too_long") })
        {
            using var invalid = await AddendumAsync(h.Doctor, text, reason);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(code, (await Json(invalid)).GetProperty("errors").GetProperty(field)[0].GetString());
        }
        foreach (var user in new[] { h.Reception, h.Nurse })
        {
            using var r = await AddendumAsync(user, "Synthetic correction", "Synthetic reason");
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using (var colleague = await AddendumAsync(h.SecondDoctor, "Synthetic correction", "Synthetic reason"))
            Assert.Equal((HttpStatusCode.Forbidden, "no_care_relationship"), (colleague.StatusCode, (await Json(colleague)).GetProperty("code").GetString()));
        Assert.Equal(1, await SignedRowsAsync()); // only version 1

        using var ok = await AddendumAsync(h.Doctor, "Synthetic correction", "Synthetic reason", 4);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    // SAFETY mutation check: drop CK_SignedNoteVersions_Addendum (migration NoteAddenda) and the unlinked addendum is stored.
    [Fact]
    public async Task TheDatabaseRefusesAnAddendumWithoutItsLinkAndReason()
    {
        await SignedNoteAsync();
        await using var db = sql.Encounters(h.Tenant);
        var v1 = await db.SignedNoteVersions.AsNoTracking().SingleAsync();
        db.SignedNoteVersions.Add(new SignedNoteVersion
        {
            Id = Guid.NewGuid(), TenantId = h.Tenant, EncounterId = encounter, NoteId = v1.NoteId, VersionNo = 2, TemplateKey = v1.TemplateKey, TemplateVersion = 1,
            Sections = new() { [SignedNoteVersion.AddendumSection] = "Synthetic" }, ContentHash = v1.ContentHash, SignedBy = h.Doctor, SignedAt = v1.SignedAt,
        });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, (ex.InnerException as PostgresException)?.SqlState);
    }

    // SAFETY mutation check: drop the header lines from SignedNotePdfDocument.Write's page loop and the per-page identifier counts fail.
    [Fact]
    public async Task ThePdfIsRenderedServerSideWithIdentifiersOnEveryPageAndStoredWithItsHash()
    {
        var plan = string.Join('\n', Enumerable.Range(1, 70).Select(i => $"Synthetic plan line {i}"));
        using (var signed = await SignAsync(h.Doctor, await SaveDraftAsync(new { chiefComplaint = Text, assessment = "Synthetic viral cold", plan }))) Assert.True(signed.IsSuccessStatusCode);
        using (var added = await AddendumAsync(h.Doctor, "Synthetic: follow-up moved to Friday", "Patient asked")) Assert.True(added.IsSuccessStatusCode);
        await using (var patients = sql.Patients(h.Tenant)) // a long name must not push the MRN or date of birth off the page
            await patients.Patients.Where(x => x.Id == h.Patient).ExecuteUpdateAsync(x => x.SetProperty(y => y.FullName, "Meera Lakshmi Venkataraman Iyer (synthetic)"));

        using var r = await PdfAsync(h.Doctor);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        var bytes = await r.Content.ReadAsByteArrayAsync();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.Equal(hash, r.Headers.GetValues("X-Content-SHA256").Single());

        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        var pages = text.Split("/Type /Page ").Length - 1;
        Assert.True(pages >= 2, $"{pages} page(s)");
        foreach (var marker in new[] { "MRN 000001", "Date of birth 12 Mar 1972", @"Meera Lakshmi Venkataraman Iyer \(synthetic\)", "Corrections are added as signed addenda." })
            Assert.Equal(pages, text.Split(marker).Length - 1);
        // Every shown line fits between the margins: 80 Courier columns (an escape such as \267 or \( is one character).
        var shown = System.Text.RegularExpressions.Regex.Matches(text, @"\(((?:[^\\)]|\\.)*)\) Tj").Select(m => System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, @"\\(\d{3}|.)", "x"));
        Assert.All(shown, line => Assert.True(line.Length <= 80, line));
        foreach (var expected in new[] { @"Signed by Dr. Arjun Rao \(synthetic\)", @"Addendum \267 version 2", "Reason: Patient asked", "Synthetic: follow-up moved to Friday", "Asia/Kolkata" })
            Assert.Contains(expected, text, StringComparison.Ordinal);

        await using (var db = sql.Encounters(h.Tenant))
        {
            var stored = await db.SignedNotePdfs.AsNoTracking().SingleAsync();
            Assert.Equal((hash, 2, h.Doctor), (stored.Sha256, stored.VersionNo, stored.RenderedBy));
            Assert.Equal(bytes, stored.Content);
        }
        Assert.Null(await BrokenChainAsync("SignedNotePdfs"));
        await using (var conn = await sql.OpenAs("app_user", h.Tenant)) // the stored copy cannot be replaced
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""UPDATE encounters."SignedNotePdfs" SET "Sha256" = 'x'""", conn).ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.RestrictViolation, ex.SqlState);
        }
        await using var audit = sql.Audit(h.Tenant);
        var pdf = await audit.Events.SingleAsync(e => e.Action == "note.pdf");
        Assert.Equal((h.Patient, AuditOutcome.Allowed), (pdf.PatientId, pdf.Outcome));
    }

    [Fact]
    public async Task ThePdfNeedsASignedNoteReadAccessAndTextItCanShow()
    {
        var version = await SaveDraftAsync(new { chiefComplaint = "खांसी (synthetic)", assessment = "Synthetic viral cold", plan = "Rest" });
        using (var draft = await PdfAsync(h.Doctor))
            Assert.Equal((HttpStatusCode.Conflict, "note_not_signed"), (draft.StatusCode, (await Json(draft)).GetProperty("code").GetString()));
        using (var signed = await SignAsync(h.Doctor, version)) Assert.True(signed.IsSuccessStatusCode);
        using (var nurse = await PdfAsync(h.Nurse)) Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode);
        using (var script = await PdfAsync(h.Doctor))
            Assert.Equal((HttpStatusCode.UnprocessableEntity, "pdf_unsupported_text"), (script.StatusCode, (await Json(script)).GetProperty("code").GetString()));
        await using var db = sql.Encounters(h.Tenant);
        Assert.Equal(0, await db.SignedNotePdfs.CountAsync());
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Encounters.Application;
using Medvya.Encounters.Infrastructure;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-018/019 on real PostgreSQL and a real Azurite: the investigation slip and the referral letter as server PDFs, each copy a stored,
/// hash-chained paper with its own QR verify code, and a public check that says what the paper is but never whose it is or what it lists.
/// </summary>
public sealed partial class PaperPdfTests(PostgresFixture sql, AzuriteFixture blobs) : IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;
    Guid encounter;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        var visit = await h.BookAsync(10, patient: h.Patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new Medvya.Scheduling.Application.CheckIn());
        encounter = (await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id)))!.Id;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Invitations:LinkBaseUrl", "https://clinic.test/app")
            .UseSetting("RateLimiting:SignInPerMinute", "1000")
            .UseSetting("Storage:ConnectionString", blobs.ConnectionString)
            .ConfigureTestServices(s => PatientEndpointTests.SignInFromHeaders(s, h.Tenant)));
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    HttpClient Client(Guid? user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (user is null) return client;
        client.DefaultRequestHeaders.Add("X-Test-User", user.Value.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        return client;
    }

    static Uri U(string path) => new(path, UriKind.Relative);

    Task<HttpResponseMessage> VerifyAsync(string token) => Client(null).PostAsJsonAsync(U("/v1/papers/verify"), new { token });

    [GeneratedRegex(@"/verify/paper/([A-Za-z0-9_-]{43})")]
    private static partial Regex VerifyLink();

    /// <summary>The PDF's pages and its verify token (the link may wrap, so it is read with the spaces taken out).</summary>
    static async Task<(IReadOnlyList<PdfPages.Page> Pages, string Text, string Token, string Sha)> ReadAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        var bytes = await r.Content.ReadAsByteArrayAsync();
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.Equal(sha, r.Headers.GetValues("X-Content-SHA256").Single());
        var pages = PdfPages.Read(bytes, PlainPdf.A4.Margin);
        Assert.All(pages, p => Assert.True(p.InsideMargins));
        var text = string.Join(' ', pages.Select(p => p.Text));
        return (pages, text, VerifyLink().Match(text.Replace(" ", "", StringComparison.Ordinal)).Groups[1].Value, sha);
    }

    async Task<PrintedPaper> PaperAsync(string sha)
    {
        await using var db = sql.Prescribing(h.Tenant);
        var paper = await db.PrintedPapers.AsNoTracking().SingleAsync(p => p.Sha256 == sha);
        var stored = (await new BlobContainerClient(blobs.ConnectionString, BlobDocumentStore.Live).GetBlobClient(paper.BlobKey).DownloadContentAsync()).Value.Content.ToArray();
        Assert.Equal((paper.Sha256, $"{h.Tenant}/paper/{paper.Id}"), (Convert.ToHexStringLower(SHA256.HashData(stored)), paper.BlobKey)); // the copy, byte for byte, under an id-only key
        return paper;
    }

    async Task<List<AuditEvent>> AuditAsync(string action)
    {
        await using var audit = sql.Audit(h.Tenant);
        return await audit.Events.Where(e => e.Action == action).OrderBy(e => e.Id).ToListAsync();
    }

    /// <summary>What the public check may never say: the patient's name, record number or birth year, or what the paper lists.</summary>
    static readonly string[] Phi = ["Meera", "Iyer", "000001", "1972", "HbA1c", "Lipid", "Menon", "Uncontrolled"];

    // SAFETY mutation check: let PaperVerification take any paper instead of the one whose code matches and the unknown code verifies (200, not 404).
    [Fact]
    public async Task TheSlipIsAStoredHashChainedPaperWhoseQrCheckSaysWhatItIsButNeverWhoseItIs()
    {
        var doctor = Client(h.Doctor);
        using (var none = await doctor.PostAsync(U($"/v1/encounters/{encounter}/orders/slip/pdf"), null)) Assert.Equal(HttpStatusCode.NotFound, none.StatusCode); // nothing placed
        using (var add = await doctor.PostAsJsonAsync(U($"/v1/encounters/{encounter}/orders"), new { kind = "Lab", name = "HbA1c", note = "उपवास 10 घंटे (synthetic)" })) Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        using (var add = await doctor.PostAsJsonAsync(U($"/v1/encounters/{encounter}/orders"), new { kind = "Lab", name = "Lipid profile" })) Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        using (var place = await doctor.PostAsJsonAsync(U($"/v1/encounters/{encounter}/orders/place"), new { destination = "Metro Diagnostics (synthetic)" })) Assert.Equal(HttpStatusCode.OK, place.StatusCode);

        string token, sha;
        using (var r = await doctor.PostAsync(U($"/v1/encounters/{encounter}/orders/slip/pdf"), null))
        {
            var pdf = await ReadAsync(r);
            (token, sha) = (pdf.Token, pdf.Sha);
            foreach (var expected in new[] { "Adyar (synthetic)", "Investigation slip", "MRN 000001", "Send to: Metro Diagnostics (synthetic)", "1. HbA1c (Lab)", "2. Lipid profile (Lab)",
                         "Bring this slip to the lab.", "Ordered by Dr. Arjun Rao (synthetic)", "SHA-256 of this paper's content:" })
                Assert.Contains(expected, pdf.Text, StringComparison.Ordinal);
            Assert.Contains("उपवास", pdf.Text, StringComparison.Ordinal); // the Hindi note prints shaped
            Assert.All(pdf.Pages, p => Assert.Contains("MRN 000001", p.Text, StringComparison.Ordinal)); // the patient on every page
            Assert.True(pdf.Pages[^1].Shapes > 200, "the QR code is drawn");
            Assert.NotEmpty(token);
        }
        var paper = await PaperAsync(sha);
        Assert.Equal((PaperKind.InvestigationSlip, encounter, h.Doctor, 2, h.Doctor), (paper.Kind, paper.SourceId, paper.DoctorId, paper.Items, paper.RenderedBy));
        Assert.Equal([(AuditOutcome.NotFound, null), (AuditOutcome.Allowed, "tests:2")], (await AuditAsync(OrderService.SlipPdfAction)).Select(e => (e.Outcome, e.Detail))); // the empty one too

        // A second copy is a second paper with its own code.
        using (var again = await doctor.PostAsync(U($"/v1/encounters/{encounter}/orders/slip/pdf"), null)) Assert.NotEqual(token, (await ReadAsync(again)).Token);

        using (var check = await VerifyAsync(token))
        {
            Assert.Equal(HttpStatusCode.OK, check.StatusCode);
            var raw = await check.Content.ReadAsStringAsync();
            var v = JsonDocument.Parse(raw).RootElement;
            Assert.Equal(("InvestigationSlip", 2, "Adyar (synthetic)", "Dr. Arjun Rao (synthetic)"),
                (v.GetProperty("kind").GetString(), v.GetProperty("items").GetInt32(), v.GetProperty("clinic").GetString(), v.GetProperty("doctor").GetString()));
            foreach (var phi in Phi) Assert.DoesNotContain(phi, raw, StringComparison.OrdinalIgnoreCase);
        }
        var verified = Assert.Single(await AuditAsync(PaperVerification.VerifyAction));
        Assert.Equal(((Guid?)null, (Guid?)h.Patient, "kind:InvestigationSlip"), (verified.ActorUserId, verified.PatientId, verified.Detail)); // in the patient's access log

        // The same code inside another tenant, an unknown code and a malformed token are all "not found".
        var (_, code) = RxVerifyToken.Read(token)!.Value;
        using (var foreign = await VerifyAsync(RxVerifyToken.Token(Guid.NewGuid(), code))) Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using (var unknown = await VerifyAsync(RxVerifyToken.Token(h.Tenant, RxVerifyToken.NewCode()))) Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using (var malformed = await VerifyAsync("not-a-token")) Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);

        await using var conn = await sql.OpenAs("app_user", h.Tenant);
        Assert.Null(await new NpgsqlCommand($"""SELECT ledger.verify_chain('prescribing.PrintedPapers', '{h.Tenant}')""", conn).ExecuteScalarAsync() is Guid g ? g : null);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""UPDATE prescribing."PrintedPapers" SET "Items" = 9""", conn).ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.RestrictViolation, ex.SqlState);
    }

    [Fact]
    public async Task TheLetterPrintsOnlyTheChosenSectionsAndTextNoFontHasIsRefused()
    {
        var referral = (await h.Get<IReferrals>(h.Doctor).CreateAsync(encounter,
            new CreateReferral("Dr. S. Menon, Endocrinology (synthetic)", ReferralUrgency.Urgent, "Uncontrolled HbA1c (synthetic)", [ReferralSection.Allergies])))!.Id;
        string token, sha;
        using (var r = await Client(h.Doctor).PostAsync(U($"/v1/referrals/{referral}/letter/pdf"), null))
        {
            var pdf = await ReadAsync(r);
            (token, sha) = (pdf.Token, pdf.Sha);
            foreach (var expected in new[] { "Referral letter", "To: Dr. S. Menon, Endocrinology (synthetic) · Urgent", "Reason for the referral", "Uncontrolled HbA1c (synthetic)",
                         "Allergies", "This letter holds only what the doctor chose to share.", "From Dr. Arjun Rao (synthetic)" })
                Assert.Contains(expected, pdf.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Current medicines", pdf.Text, StringComparison.Ordinal); // a section not chosen is not read at all
        }
        var paper = await PaperAsync(sha);
        Assert.Equal((PaperKind.ReferralLetter, referral, 1), (paper.Kind, paper.SourceId, paper.Items));
        Assert.Equal(["sections:1"], (await AuditAsync("referral.letter.pdf")).Select(e => e.Detail));
        using (var check = await VerifyAsync(token))
        {
            var v = await check.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(("ReferralLetter", 1), (v.GetProperty("kind").GetString(), v.GetProperty("items").GetInt32()));
        }
        Assert.Equal((Guid?)h.Patient, Assert.Single(await AuditAsync(PaperVerification.VerifyAction)).PatientId);

        var emoji = (await h.Get<IReferrals>(h.Doctor).CreateAsync(encounter,
            new CreateReferral("Dr. S. Menon (synthetic)", ReferralUrgency.Routine, "Synthetic 🍯", [])))!.Id;
        using (var refused = await Client(h.Doctor).PostAsync(U($"/v1/referrals/{emoji}/letter/pdf"), null))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Equal("pdf_unsupported_text", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        await using var db = sql.Prescribing(h.Tenant);
        Assert.Equal(1, await db.PrintedPapers.CountAsync()); // nothing stored for the refused one
        using (var nurse = await Client(h.Nurse).PostAsync(U($"/v1/referrals/{referral}/letter/pdf"), null)) Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Azure.Storage.Blobs;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-006 on real PostgreSQL and a real Azurite over HTTP: uploads are checked by their bytes, stored under random keys, and not viewable
/// until the scan passes; an infected file is quarantined; a view link is read-only, expires within five minutes and fetches the exact original;
/// the front desk uploads and lists but never opens; the original survives a metadata edit, which is audited; every call is audited with the patient.
/// </summary>
public sealed class DocumentTests(PostgresFixture sql, AzuriteFixture blobs) : IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly FakeScan scan = new();
    WebApplicationFactory<Program> factory = null!;
    static readonly byte[] Pdf = [.. "%PDF-1.4\n% synthetic report\n"u8, .. new byte[200]];

    /// <summary>The real local scanner, until a test says the scan is still running.</summary>
    sealed class FakeScan : IMalwareScan
    {
        public ScanVerdict? Fixed { get; set; }
        public IMalwareScan? Real { get; set; }
        public Task<ScanVerdict> CheckAsync(string key, CancellationToken cancellationToken = default) =>
            Fixed is { } v ? Task.FromResult(v) : Real!.CheckAsync(key, cancellationToken);
    }

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await h.BookAsync(10, patient: h.Patient); // the doctor's care relationship with Meera
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Storage:ConnectionString", blobs.ConnectionString)
            .ConfigureTestServices(s =>
            {
                PatientEndpointTests.SignInFromHeaders(s, h.Tenant);
                s.AddSingleton<TimeProvider>(h.Clock);
                // Wrap the registered (local) scanner so a test can hold a scan "pending".
                var real = s.Single(d => d.ServiceType == typeof(IMalwareScan)).ImplementationInstance as IMalwareScan;
                scan.Real = real;
                s.AddSingleton<IMalwareScan>(scan);
            }));
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    HttpClient Client(Guid user, Guid? facility = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", (facility ?? h.Facility).ToString());
        return client;
    }

    Uri Docs(Guid? patient = null) => new($"/v1/patients/{patient ?? h.Patient}/documents", UriKind.Relative);

    async Task<HttpResponseMessage> UploadAsync(Guid user, byte[] bytes, string type = "LabReport", string date = "2026-10-01", string source = "Imported", Guid? patient = null)
    {
        using var body = new ByteArrayContent(bytes);
        return await Client(user).PostAsync(new Uri($"/v1/patients/{patient ?? h.Patient}/documents?type={type}&date={date}&source={source}", UriKind.Relative), body);
    }

    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    async Task<Guid> UploadedAsync(Guid user, byte[]? bytes = null)
    {
        using var r = await UploadAsync(user, bytes ?? Pdf);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await Json(r)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task TheFrontDeskUploadsAndListsButNeverOpensAndTheDoctorOpensTheExactOriginal()
    {
        using var up = await UploadAsync(h.Reception, Pdf);
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        var doc = await Json(up);
        var id = doc.GetProperty("id").GetGuid();
        Assert.Equal(("Clean", "application/pdf", "Imported", "LabReport"), (doc.GetProperty("scanStatus").GetString(), doc.GetProperty("contentType").GetString(), doc.GetProperty("source").GetString(), doc.GetProperty("type").GetString()));

        using (var list = await Client(h.Reception).GetAsync(Docs()))
        {
            var body = await Json(list);
            Assert.False(body.GetProperty("canOpen").GetBoolean()); // the desk sees the list, not the files
            Assert.Single(body.GetProperty("documents").EnumerateArray());
        }
        using (var desk = await Client(h.Reception).PostAsync(new Uri($"/v1/documents/{id}/view-link", UriKind.Relative), null))
            Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode);

        using var opened = await Client(h.Doctor).PostAsync(new Uri($"/v1/documents/{id}/view-link", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var link = await Json(opened);
        Assert.Equal("Dr. Arjun Rao (synthetic)", link.GetProperty("viewer").GetString()); // printed over the document as the watermark
        var url = new Uri(link.GetProperty("url").GetString()!);
        var query = HttpUtility.ParseQueryString(url.Query);
        var expires = DateTimeOffset.Parse(query["se"]!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.Zero, TimeSpan.FromMinutes(5.5)); // at most five minutes
        Assert.Equal("r", query["sp"]); // read only
        var name = Path.GetFileName(url.AbsolutePath);
        Assert.Matches("^[0-9a-f]{32}$", name); // random: nothing of the patient, the file or the tenant in a blob name
        Assert.DoesNotContain(h.Patient.ToString("N"), url.ToString(), StringComparison.OrdinalIgnoreCase);

        using var http = new HttpClient();
        using var fetched = await http.GetAsync(url);
        Assert.Equal("application/pdf", fetched.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SHA256.HashData(Pdf), SHA256.HashData(await fetched.Content.ReadAsByteArrayAsync())); // the exact original
        using var tampered = await http.GetAsync(new Uri(url.ToString().Replace("sig=", "sig=x", StringComparison.Ordinal)));
        Assert.Equal(HttpStatusCode.Forbidden, tampered.StatusCode);
    }

    [Fact]
    public async Task NotViewableUntilTheScanPassesAndAnInfectedFileIsQuarantined()
    {
        scan.Fixed = ScanVerdict.Pending;
        var pending = await UploadedAsync(h.Doctor);
        using (var r = await Client(h.Doctor).PostAsync(new Uri($"/v1/documents/{pending}/view-link", UriKind.Relative), null))
            Assert.Equal(("scan_pending", HttpStatusCode.Conflict), ((await Json(r)).GetProperty("code").GetString(), r.StatusCode));
        using (var timeline = await Client(h.Doctor).GetAsync(new Uri($"/v1/patients/{h.Patient}/timeline?kinds=Document", UriKind.Relative)))
            Assert.Empty((await Json(timeline)).GetProperty("entries").EnumerateArray()); // not in the record yet

        scan.Fixed = ScanVerdict.Clean;
        using (var r = await Client(h.Doctor).PostAsync(new Uri($"/v1/documents/{pending}/view-link", UriKind.Relative), null))
            Assert.Equal(HttpStatusCode.OK, r.StatusCode); // looked at again when asked
        using (var timeline = await Client(h.Doctor).GetAsync(new Uri($"/v1/patients/{h.Patient}/timeline?kinds=Document", UriKind.Relative)))
        {
            var e = (await Json(timeline)).GetProperty("entries")[0];
            Assert.Equal(("Document", "DocumentLabReport", "Imported"), (e.GetProperty("kind").GetString(), e.GetProperty("code").GetString(), e.GetProperty("source").GetString()));
        }

        scan.Fixed = null; // the real local scanner finds the EICAR test string
        var eicar = Encoding.ASCII.GetBytes("%PDF-1.4\nX5O!P%@AP[4\\PZX54(P^)7CC)7}$" + "EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        using var infected = await UploadAsync(h.Nurse, eicar);
        var bad = await Json(infected);
        Assert.Equal("Infected", bad.GetProperty("scanStatus").GetString()); // the uploader is told in the answer
        using (var r = await Client(h.Doctor).PostAsync(new Uri($"/v1/documents/{bad.GetProperty("id").GetGuid()}/view-link", UriKind.Relative), null))
            Assert.Equal(("quarantined", HttpStatusCode.Conflict), ((await Json(r)).GetProperty("code").GetString(), r.StatusCode));
        var service = new BlobServiceClient(blobs.ConnectionString);
        var key = await KeyOfAsync(bad.GetProperty("id").GetGuid());
        Assert.False(await service.GetBlobContainerClient(BlobDocumentStore.Live).GetBlobClient(key).ExistsAsync()); // out of the live container
        Assert.True(await service.GetBlobContainerClient(BlobDocumentStore.Quarantine).GetBlobClient(key).ExistsAsync()); // kept for the security team
    }

    async Task<string> KeyOfAsync(Guid id)
    {
        await using var db = sql.Encounters(h.Tenant);
        return await db.Documents.Where(d => d.Id == id).Select(d => d.BlobKey).SingleAsync();
    }

    [Fact]
    public async Task EditingTheTypeOrDateLeavesTheOriginalUntouchedAndIsAudited()
    {
        var id = await UploadedAsync(h.Doctor);
        var key = await KeyOfAsync(id);
        var blob = new BlobServiceClient(blobs.ConnectionString).GetBlobContainerClient(BlobDocumentStore.Live).GetBlobClient(key);
        var before = (await blob.GetPropertiesAsync()).Value;

        using var edit = await Client(h.Doctor).PatchAsJsonAsync(new Uri($"/v1/documents/{id}", UriKind.Relative), new { type = "Imaging", documentDate = "2026-09-15" });
        var changed = await Json(edit);
        Assert.Equal(("Imaging", "2026-09-15"), (changed.GetProperty("type").GetString(), changed.GetProperty("documentDate").GetString()));
        var after = (await blob.GetPropertiesAsync()).Value;
        Assert.Equal((before.ETag, before.LastModified, Convert.ToHexString(before.ContentHash)), (after.ETag, after.LastModified, Convert.ToHexString(after.ContentHash))); // the file is byte for byte the same
        Assert.Equal(Pdf, (await blob.DownloadContentAsync()).Value.Content.ToArray());

        // Nothing can write over it: a second write to the same key is refused.
        var store = new BlobDocumentStore(new BlobServiceClient(blobs.ConnectionString), userDelegation: false, createContainers: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PutAsync(key, new MemoryStream([1, 2, 3]), "application/pdf"));

        using (var desk = await Client(h.Reception).PatchAsJsonAsync(new Uri($"/v1/documents/{id}", UriKind.Relative), new { type = "Other", documentDate = "2026-09-15" }))
            Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode);
        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.ResourceId == id).OrderBy(e => e.Id).Select(e => new { e.Action, e.Outcome, e.PatientId, e.Purpose }).ToListAsync();
        Assert.Equal([("patient.document.upload", AuditOutcome.Allowed), ("patient.document.update", AuditOutcome.Allowed)], rows.Select(r => (r.Action, r.Outcome)));
        Assert.All(rows, r => Assert.Equal((h.Patient, AuditPurpose.Treatment), (r.PatientId, r.Purpose)));
    }

    [Fact]
    public async Task OnlyGenuinePdfJpegAndPngOfASaneSizeAndDetailsAreAccepted()
    {
        using (var text = await UploadAsync(h.Doctor, "just text, claimed to be a pdf"u8.ToArray())) Assert.Equal(((HttpStatusCode)415, "unsupported_type"), (text.StatusCode, (await Json(text)).GetProperty("code").GetString()));
        using (var empty = await UploadAsync(h.Doctor, [])) Assert.Equal("empty", (await Json(empty)).GetProperty("code").GetString());
        using (var big = await UploadAsync(h.Doctor, [.. "%PDF-"u8, .. new byte[DocumentPolicy.MaxBytes]])) Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        using (var type = await UploadAsync(h.Doctor, Pdf, type: "Nope")) Assert.Equal(HttpStatusCode.BadRequest, type.StatusCode);
        using (var future = await UploadAsync(h.Doctor, Pdf, date: "2999-01-01")) Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        using (var source = await UploadAsync(h.Doctor, Pdf, source: "ClinicianVerified")) Assert.Equal(HttpStatusCode.BadRequest, source.StatusCode); // an upload is never "verified"
        using (var jpeg = await UploadAsync(h.Doctor, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3])) Assert.Equal("image/jpeg", (await Json(jpeg)).GetProperty("contentType").GetString());
        using (var png = await UploadAsync(h.Doctor, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1])) Assert.Equal("image/png", (await Json(png)).GetProperty("contentType").GetString());
    }

    [Fact]
    public async Task OnlyTheRightRolesAndOnlyTheirOwnFacilitysPatientsAndEveryCallIsAudited()
    {
        using (var admin = await UploadAsync(h.Admin, Pdf)) Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode); // the practice admin has no business in documents
        using (var adminList = await Client(h.Admin).GetAsync(Docs())) Assert.Equal(HttpStatusCode.Forbidden, adminList.StatusCode);
        using (var elsewhere = await UploadAsync(h.Reception, Pdf, patient: h.OtherFacilityPatient)) Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode); // another facility's patient
        using (var missing = await Client(h.Doctor).GetAsync(Docs(Guid.NewGuid()))) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using (var colleague = await Client(h.SecondDoctor).GetAsync(Docs()))
            Assert.Equal(("no_care_relationship", HttpStatusCode.Forbidden), ((await Json(colleague)).GetProperty("code").GetString(), colleague.StatusCode));
        using (var ok = await Client(h.Doctor).GetAsync(Docs())) Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.ResourceType == "document").OrderBy(e => e.Id).Select(e => new { e.Action, e.Outcome }).ToListAsync();
        Assert.Equal(
            [("patient.document.upload", AuditOutcome.Denied), ("patient.document.list", AuditOutcome.Denied), ("patient.document.upload", AuditOutcome.Denied),
             ("patient.document.list", AuditOutcome.NotFound), ("patient.document.list", AuditOutcome.Denied), ("patient.document.list", AuditOutcome.Allowed)],
            rows.Select(r => (r.Action, r.Outcome)));
    }

    [Fact]
    public void RolesDecideWhoUploadsAndWhoOpens()
    {
        var open = new List<Role>();
        var upload = new List<Role>();
        foreach (var role in Enum.GetValues<Role>())
        {
            var chart = RoleMatrix.Of(role, Capability.Patient360Chart);
            if (DocumentRules.CanOpen(chart)) open.Add(role);
            if (DocumentRules.CanOpen(chart) || DocumentRules.CanUploadAtDesk(RoleMatrix.Of(role, Capability.RegisterBookPatients), RoleMatrix.Of(role, Capability.CheckInQueue))) upload.Add(role);
        }
        Assert.Equal([Role.SoloDoctor, Role.HospitalDoctor, Role.Nurse, Role.CareCoordinator], open);
        Assert.Equal([Role.SoloDoctor, Role.HospitalDoctor, Role.Receptionist, Role.Nurse, Role.CareCoordinator], upload); // not the practice admin, hospital IT or the patient
    }

    [Fact]
    public void TheTypeComesFromTheBytesNotTheName() =>
        Assert.Equal(new string?[] { "application/pdf", "image/jpeg", "image/png", null, null },
            new[] { DocumentRules.ContentTypeOf("%PDF-1"u8), DocumentRules.ContentTypeOf([0xFF, 0xD8, 0xFF]), DocumentRules.ContentTypeOf([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
             DocumentRules.ContentTypeOf("MZ\x90"u8), DocumentRules.ContentTypeOf([]) });
}

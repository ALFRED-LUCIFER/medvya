using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Azure.Storage.Blobs;
using Medvya.Encounters.Application;
using Medvya.Encounters.Infrastructure;
using Medvya.Notifications.Application;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-020 over HTTP on real PostgreSQL: only an authorised, unchanged prescription is shared; the link expires, opens only with a one-time code
/// sent to the patient's registered mobile, and each open is audited; message texts carry no clinical detail; a pharmacy dispatch records each
/// state with its actor; no regulator submission endpoint exists. Messages go to fakes, never a provider. SAFETY mutation checks are named on the tests.
/// </summary>
public sealed partial class RxShareTests(PostgresFixture sql, AzuriteFixture blobs) : IClassFixture<PostgresFixture>, IClassFixture<AzuriteFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly FakeEmail email = new();
    readonly FakeSms sms = new();
    WebApplicationFactory<Program> factory = null!;
    Guid encounter;

    sealed class FakeEmail : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    sealed class FakeSms : ISmsGateway
    {
        public List<SmsMessage> Sent { get; } = [];
        public bool Fail { get; set; }
        public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("provider down for +919000000101");
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await using (var db = sql.Context(h.Tenant))
        {
            var membership = await db.Memberships.SingleAsync(m => m.UserId == h.Doctor);
            db.DoctorLicences.Add(new DoctorLicence { MembershipId = membership.Id, TenantId = h.Tenant, Authority = "TNMC", Number = "84213", SubmittedAt = h.Clock.GetUtcNow() });
            db.DoctorProfiles.Add(new DoctorProfile { MembershipId = membership.Id, TenantId = h.Tenant, FullName = "Dr. Arjun Rao (synthetic)" });
            await db.SaveChangesAsync();
        }
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Invitations:LinkBaseUrl", "https://clinic.test/app")
            .UseSetting("RateLimiting:SignInPerMinute", "1000")
            .UseSetting("Storage:ConnectionString", blobs.ConnectionString) // the shared copy lives in Blob
            .UseSetting("Notifications:Sms:DefaultSender", "MEDVYA")
            .UseSetting("Notifications:Sms:DltTemplateIds:SecureLink.en", "1107000000000000001")
            .UseSetting("Notifications:Sms:DltTemplateIds:SecureCode.en", "1107000000000000002")
            .ConfigureTestServices(s =>
            {
                PatientEndpointTests.SignInFromHeaders(s, h.Tenant);
                s.AddSingleton<TimeProvider>(h.Clock);
                s.AddSingleton<IEmailSender>(email);
                s.AddSingleton<ISmsGateway>(sms);
            }));
        var visit = await h.BookAsync(10, patient: h.Patient);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new CheckIn());
        encounter = (await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id)))!.Id;
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    HttpClient Client(Guid? user, int? reauthMinutesAgo = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (user is null) return client;
        client.DefaultRequestHeaders.Add("X-Test-User", user.Value.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        if (reauthMinutesAgo is { } m) client.DefaultRequestHeaders.Add("X-Test-Reauth", h.Clock.GetUtcNow().AddMinutes(-m).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return client;
    }

    static Uri U(string path) => new(path, UriKind.Relative);

    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    static async Task<string?> Code(HttpResponseMessage r) => (await Json(r)).TryGetProperty("code", out var c) ? c.GetString() : null;

    static readonly UpdateRxLine Twice = new("1", "tablet", "oral", "BD", "after_food", 5, 10, null);

    /// <summary>Metformin with a complete instruction, authorised (a fresh step-up).</summary>
    async Task<Guid> AuthorisedAsync()
    {
        var rx = h.Get<IPrescriptions>(h.Doctor);
        var id = (await h.Get<IFormulary>(h.Doctor).SearchAsync("Metformin", 1)).Single().Id;
        var line = (await rx.AddAsync(encounter, new AddRxLine(id, null, null, null)))!.Last(l => l.FormularyItemId == id).Id!.Value;
        await rx.UpdateAsync(line, Twice);
        await AuthoriseAsync();
        return line;
    }

    async Task AuthoriseAsync()
    {
        using var r = await Client(h.Doctor, 0).PostAsync(U($"/v1/encounters/{encounter}/prescription/authorisation"), null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    Task<HttpResponseMessage> ShareAsync(object body, Guid? user = null) => Client(user ?? h.Doctor).PostAsJsonAsync(U($"/v1/encounters/{encounter}/prescription/shares"), body);

    Task<HttpResponseMessage> SendCodeAsync(string token) => Client(null).PostAsJsonAsync(U("/v1/prescriptions/shared/code"), new { token });

    Task<HttpResponseMessage> OpenAsync(string token, string? code) => Client(null).PostAsJsonAsync(U("/v1/prescriptions/shared/open"), new { token, code });

    [GeneratedRegex(@"/rx/([A-Za-z0-9_-]+)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\b(\d{6})\b")]
    private static partial Regex CodePattern();

    static string TokenIn(string text) => LinkPattern().Match(text).Groups[1].Value;

    string LastCode() => CodePattern().Match(sms.Sent[^1].Text).Groups[1].Value;

    async Task<List<AuditEvent>> AuditAsync(string action)
    {
        await using var audit = sql.Audit(h.Tenant);
        return await audit.Events.Where(e => e.Action == action).OrderBy(e => e.Id).ToListAsync();
    }

    async Task<int> SharesAsync()
    {
        await using var db = sql.Prescribing(h.Tenant);
        return await db.RxShares.CountAsync();
    }

    /// <summary>What no message may carry: the patient's name, record number, birth year or id, the medicine or its dose.</summary>
    string[] Phi => ["Meera", "Iyer", "000001", "1972", h.Patient.ToString(), "Metformin", "500 mg", "tablet", "twice"];

    // SAFETY mutation check: drop the hash check in RxShareLinks.OpenAsync and the swapped copy is handed out.
    [Fact]
    public async Task AStoredCopyThatNoLongerMatchesItsHashIsNeverHandedOut()
    {
        await AuthorisedAsync();
        using (var shared = await ShareAsync(new { recipient = "Patient", channel = "Sms" })) Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        var token = TokenIn(sms.Sent[^1].Text);
        string key;
        await using (var db = sql.Prescribing(h.Tenant)) key = (await db.PrescriptionPdfs.SingleAsync()).BlobKey!;
        // Someone with storage access swaps the file; the hash in the hash-chained row still says what was shared.
        await new BlobContainerClient(blobs.ConnectionString, BlobDocumentStore.Live).GetBlobClient(key).UploadAsync(new BinaryData("%PDF-1.7 synthetic swap"), overwrite: true);
        using (var sent = await SendCodeAsync(token)) Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);
        using (var open = await OpenAsync(token, LastCode()))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, open.StatusCode);
            Assert.DoesNotContain("synthetic swap", await open.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        Assert.DoesNotContain(await AuditAsync("rx.share.open"), e => e.Outcome == AuditOutcome.Allowed);
    }

    // SAFETY mutation check: drop the CodeMatches check in RxShareLinks.OpenAsync and the wrong-code open returns the PDF; drop the ExpiresAt filter in
    // LiveAsync and the expired link still sends a code; drop the UsedAt update and the same code opens twice; put the patient's name or a medicine
    // into SecureLinkCatalog and the PHI assertions fail.
    [Fact]
    public async Task AShareLinkNeedsACodeToTheRegisteredMobileExpiresCarriesNoClinicalDetailAndEveryOpenIsAudited()
    {
        await AuthorisedAsync();
        using (var shared = await ShareAsync(new { recipient = "Patient", channel = "Sms" }))
        {
            Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
            var view = (await Json(shared))[0];
            Assert.Equal(("Patient", "Sms", 1, false), (view.GetProperty("recipient").GetString(), view.GetProperty("channel").GetString(), view.GetProperty("versionNo").GetInt32(), view.GetProperty("replaced").GetBoolean()));
            Assert.Equal(h.Clock.GetUtcNow().AddDays(3), view.GetProperty("expiresAt").GetDateTimeOffset());
        }
        var link = Assert.Single(sms.Sent);
        Assert.Equal(("+919000000101", "MEDVYA", "1107000000000000001"), (link.To, link.Sender, link.TemplateId));
        Assert.StartsWith("Adyar (synthetic) has shared a document with you: https://clinic.test/app/rx/", link.Text, StringComparison.Ordinal);
        var token = TokenIn(link.Text);
        foreach (var phi in Phi) Assert.DoesNotContain(phi, link.Text, StringComparison.OrdinalIgnoreCase);

        await using (var db = sql.Prescribing(h.Tenant)) // only the token's hash is stored
            Assert.Equal(SHA256.HashData(Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + new string('=', (4 - token.Length % 4) % 4))),
                (await db.RxShares.SingleAsync()).LinkHash);

        using (var noCode = await OpenAsync(token, "000000")) Assert.Equal((HttpStatusCode.Forbidden, "no_code"), (noCode.StatusCode, await Code(noCode)));
        using (var sent = await SendCodeAsync(token)) Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);
        var codeSms = sms.Sent[^1];
        Assert.Equal(("+919000000101", "1107000000000000002"), (codeSms.To, codeSms.TemplateId));
        foreach (var phi in Phi) Assert.DoesNotContain(phi, codeSms.Text, StringComparison.OrdinalIgnoreCase);
        var code = LastCode();
        var wrong = code == "123456" ? "654321" : "123456";

        using (var bad = await OpenAsync(token, wrong)) Assert.Equal((HttpStatusCode.Forbidden, "wrong_code"), (bad.StatusCode, await Code(bad)));
        using (var ok = await OpenAsync(token, code))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("application/pdf", ok.Content.Headers.ContentType?.MediaType);
            var bytes = await ok.Content.ReadAsByteArrayAsync();
            await using var db = sql.Prescribing(h.Tenant);
            Assert.Equal((await db.PrescriptionPdfs.SingleAsync()).Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes))); // the stored copy, byte for byte
        }
        using (var again = await OpenAsync(token, code)) Assert.Equal((HttpStatusCode.Forbidden, "no_code"), (again.StatusCode, await Code(again))); // used up

        var shareRow = Assert.Single(await AuditAsync("rx.share"));
        Assert.Equal(((Guid?)h.Doctor, (Guid?)h.Patient, "recipient:Patient;channel:Sms;version:1"), (shareRow.ActorUserId, shareRow.PatientId, shareRow.Detail));
        var opens = await AuditAsync("rx.share.open");
        Assert.All(opens, e => Assert.Equal(((Guid?)null, (Guid?)h.Patient), (e.ActorUserId, e.PatientId)));
        Assert.Equal([(AuditOutcome.Denied, "no_code"), (AuditOutcome.Denied, "wrong_code"), (AuditOutcome.Allowed, "recipient:Patient"), (AuditOutcome.Denied, "no_code")],
            opens.Select(e => (e.Outcome, e.Detail)));
        var codeRow = Assert.Single(await AuditAsync("rx.share.code"));
        Assert.Equal((AuditOutcome.Allowed, (Guid?)null, (Guid?)h.Patient), (codeRow.Outcome, codeRow.ActorUserId, codeRow.PatientId));

        // Expired, malformed, unknown and another tenant's: the same 404, and no message.
        h.Clock.Advance(TimeSpan.FromDays(3));
        var count = sms.Sent.Count;
        foreach (var dead in new[] { token, "not-a-token", InvitationToken.New(h.Tenant).Token, InvitationToken.New(Guid.NewGuid()).Token })
        {
            using var r = await SendCodeAsync(dead);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            using var o = await OpenAsync(dead, code);
            Assert.Equal(HttpStatusCode.NotFound, o.StatusCode);
        }
        Assert.Equal(count, sms.Sent.Count);
    }

    // SAFETY mutation check: drop the Attempts < MaxAttempts condition in OpenAsync and the right code still opens after five wrong tries;
    // drop the MaxCodes or CodeInterval check in SendCodeAsync and the extra codes are sent.
    [Fact]
    public async Task FiveWrongTriesEndACodeAndCodesAreRationed()
    {
        await AuthorisedAsync();
        using (await ShareAsync(new { recipient = "Patient", channel = "Sms" })) { }
        var token = TokenIn(sms.Sent[^1].Text);
        using (await SendCodeAsync(token)) { }
        var code = LastCode();
        var wrong = code == "123456" ? "654321" : "123456";
        for (var i = 0; i < RxShareRules.MaxAttempts; i++)
            using (var bad = await OpenAsync(token, wrong)) Assert.Equal("wrong_code", await Code(bad));
        using (var locked = await OpenAsync(token, code)) Assert.Equal((HttpStatusCode.Forbidden, "no_code"), (locked.StatusCode, await Code(locked)));

        using (var soon = await SendCodeAsync(token)) Assert.Equal((HttpStatusCode.TooManyRequests, "too_soon"), (soon.StatusCode, await Code(soon)));
        for (var i = 1; i < RxShareRules.MaxCodes; i++)
        {
            h.Clock.Advance(RxShareRules.CodeInterval);
            using var more = await SendCodeAsync(token);
            Assert.Equal(HttpStatusCode.NoContent, more.StatusCode);
        }
        h.Clock.Advance(RxShareRules.CodeInterval);
        using (var limit = await SendCodeAsync(token)) Assert.Equal((HttpStatusCode.TooManyRequests, "code_limit"), (limit.StatusCode, await Code(limit)));
        using (var latest = await OpenAsync(token, LastCode())) Assert.Equal(HttpStatusCode.OK, latest.StatusCode); // the latest code still works
    }

    // SAFETY mutation check: drop PrintableAsync from PrescriptionService.ShareAsync and the unauthorised and changed prescriptions are shared;
    // drop RefuseIfReplacedAsync from RxShareLinks and the replaced version still opens.
    [Fact]
    public async Task OnlyAnAuthorisedUnchangedPrescriptionIsSharedAndAReplacedOneOpensNothing()
    {
        using (var none = await ShareAsync(new { recipient = "Patient", channel = "Sms" }))
            Assert.Equal((HttpStatusCode.Conflict, "not_authorised"), (none.StatusCode, await Code(none)));
        var line = await AuthorisedAsync();
        using (await ShareAsync(new { recipient = "Patient", channel = "Sms" })) { }
        var first = TokenIn(sms.Sent[^1].Text);

        await h.Get<IPrescriptions>(h.Doctor).UpdateAsync(line, Twice with { Dose = "2" });
        using (var changed = await ShareAsync(new { recipient = "Patient", channel = "Sms" }))
            Assert.Equal((HttpStatusCode.Conflict, "changed"), (changed.StatusCode, await Code(changed)));
        Assert.Equal(1, await SharesAsync());

        await AuthoriseAsync(); // version 2 replaces version 1
        using (var code = await SendCodeAsync(first)) Assert.Equal((HttpStatusCode.Gone, "replaced"), (code.StatusCode, await Code(code)));
        using (var open = await OpenAsync(first, "000000")) Assert.Equal((HttpStatusCode.Gone, "replaced"), (open.StatusCode, await Code(open)));
        using var list = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{encounter}/prescription/shares"));
        Assert.True((await Json(list))[0].GetProperty("replaced").GetBoolean());
        foreach (var action in new[] { "rx.share.code", "rx.share.open" })
        {
            var row = Assert.Single(await AuditAsync(action));
            Assert.Equal((AuditOutcome.Denied, "replaced"), (row.Outcome, row.Detail));
        }
    }

    // SAFETY mutation check: drop the cancellation check from RxShareLinks.RefuseIfReplacedAsync and the cancelled prescription still opens by its link.
    [Fact]
    public async Task ACancelledPrescriptionIsNotSharedAndItsLinkOpensNothing()
    {
        await AuthorisedAsync();
        using (await ShareAsync(new { recipient = "Patient", channel = "Sms" })) { }
        var token = TokenIn(sms.Sent[^1].Text);
        using (var code = await SendCodeAsync(token)) Assert.Equal(HttpStatusCode.NoContent, code.StatusCode);
        var issued = LastCode();

        using (var cancel = await Client(h.Doctor, 0).PostAsJsonAsync(U($"/v1/encounters/{encounter}/prescription/cancellation"), new { reason = "Wrong patient (synthetic)" }))
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        using (var open = await OpenAsync(token, issued)) Assert.Equal((HttpStatusCode.Gone, "cancelled"), (open.StatusCode, await Code(open))); // even with a live code
        using (var again = await SendCodeAsync(token)) Assert.Equal((HttpStatusCode.Gone, "cancelled"), (again.StatusCode, await Code(again)));
        using (var share = await ShareAsync(new { recipient = "Patient", channel = "Sms" }))
            Assert.Equal((HttpStatusCode.Conflict, "cancelled"), (share.StatusCode, await Code(share)));
        Assert.Equal(1, await SharesAsync());
        Assert.Equal((AuditOutcome.Denied, "cancelled"), ((await AuditAsync("rx.share.open"))[^1].Outcome, (await AuditAsync("rx.share.open"))[^1].Detail));
    }

    [Fact]
    public async Task NothingIsKeptWhenTheMessageCannotGoOrThePatientHasNoMobile()
    {
        await AuthorisedAsync();
        using (var noEmail = await ShareAsync(new { recipient = "Patient", channel = "Email" })) // the synthetic patient has no email
            Assert.Equal((HttpStatusCode.Conflict, "no_address"), (noEmail.StatusCode, await Code(noEmail)));
        sms.Fail = true;
        using (var down = await ShareAsync(new { recipient = "Patient", channel = "Sms" }))
            Assert.Equal((HttpStatusCode.Conflict, "send_failed"), (down.StatusCode, await Code(down)));
        Assert.Equal(0, await SharesAsync());
        await using (var db = sql.Prescribing(h.Tenant)) Assert.Equal(0, await db.PrescriptionPdfs.CountAsync());

        await using (var patients = sql.Patients(h.Tenant))
        {
            (await patients.Patients.SingleAsync(p => p.Id == h.Patient)).Mobile = "";
            await patients.SaveChangesAsync();
        }
        using (var noMobile = await ShareAsync(new { recipient = "Pharmacy", pharmacyName = "Apollo Pharmacy Adyar (synthetic)", pharmacyEmail = "orders@pharmacy.example.test" }))
            Assert.Equal((HttpStatusCode.Conflict, "no_mobile"), (noMobile.StatusCode, await Code(noMobile))); // the code could never reach the patient
        Assert.Empty(email.Sent);
        using (var invalid = await ShareAsync(new { recipient = "Pharmacy", pharmacyName = "", pharmacyEmail = "not an email" })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    // SAFETY mutation check: drop AddLedger for PharmacyDispatchEvents (migration RxShares) and the database UPDATE succeeds; drop the CanMove check in
    // DispatchAsync and a dispensed dispatch is cancelled.
    [Fact]
    public async Task APharmacyDispatchRecordsEachStateWithItsActorAndOnlyADoctorCaringForThePatientChangesIt()
    {
        await AuthorisedAsync();
        using (var shared = await ShareAsync(new { recipient = "Pharmacy", pharmacyName = "Apollo Pharmacy Adyar (synthetic)", pharmacyEmail = "orders@pharmacy.example.test" }))
        {
            Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
            var dispatch = (await Json(shared))[0].GetProperty("dispatch");
            Assert.Equal(("Sent", h.Doctor), (dispatch.GetProperty("state").GetString(), dispatch.GetProperty("changedBy").GetGuid()));
        }
        var mail = Assert.Single(email.Sent);
        Assert.Equal("orders@pharmacy.example.test", mail.To);
        Assert.Contains("ask the patient for the code", mail.Body, StringComparison.Ordinal);
        foreach (var phi in Phi) Assert.DoesNotContain(phi, mail.Subject + mail.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sms.Sent); // the pharmacy link is emailed; only the code goes to the patient's mobile

        Guid share;
        await using (var db = sql.Prescribing(h.Tenant)) share = (await db.RxShares.SingleAsync()).Id;
        foreach (var user in new[] { h.Reception, h.Nurse })
        {
            using var refused = await Client(user).PostAsJsonAsync(U($"/v1/prescription-shares/{share}/dispatch"), new { state = "Cancelled" });
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }
        await h.Settings(h.Admin).SaveClinicalAccessAsync(ClinicalAccessPolicy.AssignedOnly);
        using (var colleague = await Client(h.SecondDoctor).PostAsJsonAsync(U($"/v1/prescription-shares/{share}/dispatch"), new { state = "Cancelled" }))
            Assert.Equal(HttpStatusCode.Forbidden, colleague.StatusCode);

        using (var future = await Client(h.Doctor).PostAsJsonAsync(U($"/v1/prescription-shares/{share}/dispatch"), new { state = "Dispensed", dispensedAt = h.Clock.GetUtcNow().AddHours(1) }))
            Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        h.Clock.Advance(TimeSpan.FromHours(2));
        var at = h.Clock.GetUtcNow().AddMinutes(-30);
        using (var dispensed = await Client(h.Doctor).PostAsJsonAsync(U($"/v1/prescription-shares/{share}/dispatch"), new { state = "Dispensed", dispensedAt = at, pharmacyRef = "APL-2231" }))
        {
            Assert.Equal(HttpStatusCode.OK, dispensed.StatusCode);
            var d = (await Json(dispensed))[0].GetProperty("dispatch");
            Assert.Equal(("Dispensed", "APL-2231", at), (d.GetProperty("state").GetString(), d.GetProperty("pharmacyRef").GetString(), d.GetProperty("dispensedAt").GetDateTimeOffset()));
        }
        using (var final = await Client(h.Doctor).PostAsJsonAsync(U($"/v1/prescription-shares/{share}/dispatch"), new { state = "Cancelled" }))
            Assert.Equal((HttpStatusCode.Conflict, "dispatch_final"), (final.StatusCode, await Code(final)));

        await using (var db = sql.Prescribing(h.Tenant))
        {
            var events = await db.PharmacyDispatchEvents.AsNoTracking().OrderBy(e => e.ChangedAt).ToListAsync();
            Assert.Equal([(DispatchState.Sent, h.Doctor), (DispatchState.Dispensed, h.Doctor)], events.Select(e => (e.State, e.ChangedBy)));
        }
        var audited = Assert.Single(await AuditAsync("rx.dispatch"), e => e.Outcome == AuditOutcome.Allowed);
        Assert.Equal(((Guid?)h.Doctor, (Guid?)h.Patient, "state:Dispensed"), (audited.ActorUserId, audited.PatientId, audited.Detail));

        await using var conn = await sql.OpenAs("app_user", h.Tenant);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""UPDATE prescribing."PharmacyDispatchEvents" SET "State" = 'Cancelled'""", conn).ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.RestrictViolation, ex.SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""UPDATE prescribing."RxShares" SET "ExpiresAt" = now() + interval '1 year'""", conn).ExecuteNonQueryAsync());
    }

    /// <summary>Scope guard (MDV-P2-020, PRD P2 "not in this phase"): regulator e-prescription submission is P5-04, so no such endpoint exists.</summary>
    [Fact]
    public void NoRegulatorSubmissionEndpointExistsInP2()
    {
        var routes = factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText ?? "").ToList();
        Assert.Contains("/v1/encounters/{encounterId:guid}/prescription/shares", routes);
        Assert.DoesNotContain(routes, r => Regex.IsMatch(r, "regulat|e-?rx|eprescri|abdm|nhcx|nabidh|riayati|malaffi|(prescri|rx).*submi", RegexOptions.IgnoreCase));
    }
}

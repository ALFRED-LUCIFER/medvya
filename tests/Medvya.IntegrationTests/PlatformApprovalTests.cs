extern alias platform;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.BuildingBlocks.Domain;
using Medvya.Platform.Domain;
using Medvya.Platform.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ConsoleProgram = platform::Program;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-064 (ADR-016, ADR-014 note): the console's organisation approvals on real PostgreSQL, through the console host and its narrow medvya_review login.
/// Mutation checks: let every console role decide (PlatformPolicies.OrganisationDecide to Console) and the Support and Finance steps fail; drop the
/// organisation's own transition or DPA rule in OrganisationStates and the refusal steps fail; drop the platform audit call and the audit steps fail;
/// grant medvya_review a patients table in table-rights.sql and TheReviewLoginCannotReadClinicData fails.
/// </summary>
public sealed class PlatformApprovalTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IDisposable
{
    static readonly byte[] Pdf = "%PDF-1.4 synthetic"u8.ToArray();
    readonly InMemoryLogos files = new();
    readonly List<EmailMessage> sent = [];
    WebApplicationFactory<ConsoleProgram>? factory;

    sealed class Mail(List<EmailMessage> sent) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            lock (sent) sent.Add(message);
            return Task.CompletedTask;
        }
    }

    // The storage link is fetched by the console host; here it answers with the stored bytes instead of leaving the machine.
    sealed class StorageStub(InMemoryLogos files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.RequestUri!.AbsolutePath.TrimStart('/');
            if (!files.Stored.TryGetValue(key, out var file)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file.Content) };
            response.Content.Headers.ContentType = new("application/pdf");
            return Task.FromResult(response);
        }
    }

    public void Dispose() => factory?.Dispose();

    async Task<HttpClient> Console(PlatformRole role)
    {
        factory ??= PlatformConsoleTests.CreateFactory(sql, signInPerMinute: 1000, configure: b => b
            .UseSetting("ConnectionStrings:Review", sql.As("medvya_review"))
            .ConfigureServices(s =>
            {
                s.AddSingleton<ILogoStore>(files);
                s.AddSingleton<IEmailSender>(new Mail(sent));
                s.AddSingleton(new HttpClient(new StorageStub(files)));
            }));
        var (member, key) = await PlatformConsoleTests.Seed(sql, role);
        return await PlatformConsoleTests.SignedIn(factory, member, key);
    }

    /// <summary>An organisation under review with every document it needs, a profile email and a request of the given type.</summary>
    async Task<(Guid Id, Guid Document)> Org(OrganisationType type = OrganisationType.Clinic, OrganisationState state = OrganisationState.Submitted, bool withDpa = true)
    {
        var id = Guid.NewGuid();
        Guid doc = default;
        await using var db = sql.Context(id);
        db.Tenants.Add(new Tenant { Id = id, Name = $"Review Clinic {id.ToString("N")[..6]} (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia", State = state });
        db.Profiles.Add(new PracticeProfile { Id = Guid.NewGuid(), TenantId = id, Email = "desk@review-clinic.example", ChangedAt = DateTimeOffset.UtcNow });
        db.OrganisationRequests.Add(new OrganisationRequest { TenantId = id, Type = type, SubmittedAt = DateTimeOffset.UtcNow, ItContactName = type == OrganisationType.Hospital ? "IT lead (synthetic)" : null, ItContactEmail = type == OrganisationType.Hospital ? "it@review-clinic.example" : null });
        foreach (var kind in OrganisationDocumentRules.Required(type, ownerIsDoctor: true).Where(k => withDpa || k != OrganisationDocumentRules.Dpa))
        {
            var key = $"onboarding/{id}/{Guid.NewGuid()}";
            files.Stored[key] = (Pdf, "application/pdf");
            var d = new OrganisationDocument { Id = Guid.CreateVersion7(), TenantId = id, Kind = kind, ContentType = "application/pdf", SizeBytes = Pdf.Length, BlobKey = key, UploadedAt = DateTimeOffset.UtcNow };
            db.OrganisationDocuments.Add(d);
            doc = d.Id;
        }
        await db.SaveChangesAsync();
        return (id, doc);
    }

    async Task<OrganisationState> State(Guid id)
    {
        await using var db = sql.Context(id);
        return await db.Tenants.Select(t => t.State).SingleAsync();
    }

    static Task<HttpResponseMessage> Decide(HttpClient c, Guid id, string route, string? reason = "Registration certificate and GSTIN checked") =>
        PlatformConsoleTests.Post(c, $"/v1/organisations/{id}/{route}", new { reason });

    async Task<List<Medvya.Platform.Domain.PlatformAuditEntry>> PlatformAudit(Guid id)
    {
        await using var db = PlatformConsoleTests.Db(sql);
        return await db.AuditEntries.Where(a => a.SubjectTenantId == id).OrderBy(a => a.OccurredAt).ToListAsync();
    }

    [Theory]
    [InlineData(PlatformRole.Owner), InlineData(PlatformRole.Operations), InlineData(PlatformRole.Reviewer)]
    public async Task OwnerOperationsAndReviewerDecideAndTheDecisionIsAuditedEmailedAndShown(PlatformRole role)
    {
        var (id, _) = await Org();
        using var c = await Console(role);
        var approved = await Decide(c, id, "approve");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var preview = await approved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("is approved on Medvya", preview.GetProperty("subject").GetString(), StringComparison.Ordinal);
        Assert.Equal(OrganisationState.Approved, await State(id));

        // The organisation's own audit trail names the operator (the platform member); the platform log names the action and the organisation.
        await using (var audit = sql.Audit(id))
            Assert.Single(await audit.Events.Where(e => e.Action == "organisation.state.Approved").ToListAsync());
        var entry = Assert.Single(await PlatformAudit(id));
        Assert.Equal("organisation.approve", entry.Action);
        Assert.Equal("Registration certificate and GSTIN checked", entry.Reason);
        Assert.NotNull(entry.ActorMemberId);
        Assert.Single(sent, m => m.To == "desk@review-clinic.example" && m.Subject.Contains("is approved", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PlatformRole.Support), InlineData(PlatformRole.Finance)]
    public async Task SupportAndFinanceReadButEveryDecisionIs403(PlatformRole role)
    {
        var (id, doc) = await Org();
        using var c = await Console(role);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(new Uri("/v1/organisations/?filter=Waiting", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(new Uri($"/v1/organisations/{id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(new Uri($"/v1/organisations/{id}/documents/{doc}", UriKind.Relative))).StatusCode);
        foreach (var route in new[] { "approve", "request-changes", "reject", "suspend", "reinstate" })
            Assert.Equal(HttpStatusCode.Forbidden, (await Decide(c, id, route)).StatusCode);
        Assert.Equal(OrganisationState.Submitted, await State(id));
        Assert.DoesNotContain(sent, m => m.To == "desk@review-clinic.example");
    }

    [Fact]
    public async Task SigningOnWithoutTheKeyOpensNoApprovalEndpoint()
    {
        var (id, _) = await Org();
        await Console(PlatformRole.Owner); // builds the factory
        var (member, _) = await PlatformConsoleTests.Seed(sql, PlatformRole.Owner);
        using var browser = PlatformConsoleTests.Browser(factory!);
        await PlatformConsoleTests.SignOn(browser, member.Subject!);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync(new Uri("/v1/organisations/", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Decide(browser, id, "approve")).StatusCode);
        Assert.Equal(OrganisationState.Submitted, await State(id));
    }

    [Theory]
    [InlineData(null), InlineData(""), InlineData("no"), InlineData("four")]
    public async Task EveryActionNeedsAReasonOfFiveToFiveHundredCharacters(string? reason)
    {
        var (id, _) = await Org();
        using var c = await Console(PlatformRole.Reviewer);
        foreach (var route in new[] { "approve", "request-changes", "reject", "suspend", "reinstate" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Decide(c, id, route, reason)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(c, id, "approve", new string('x', 501))).StatusCode);
        Assert.Equal(OrganisationState.Submitted, await State(id));
        Assert.Empty(await PlatformAudit(id));
        Assert.DoesNotContain(sent, m => m.To == "desk@review-clinic.example");
    }

    [Fact]
    public async Task AHospitalWithoutItsSignedDpaCannotBeApprovedAndAsksForChangesInstead()
    {
        var (id, _) = await Org(OrganisationType.Hospital, withDpa: false);
        using var c = await Console(PlatformRole.Operations);
        var refused = await Decide(c, id, "approve");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("required", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("document.Dpa")[0].GetString());
        Assert.Equal(OrganisationState.Submitted, await State(id));
        Assert.Empty(await PlatformAudit(id)); // a refused decision is not logged as done

        Assert.Equal(HttpStatusCode.OK, (await Decide(c, id, "request-changes", "The signed data processing agreement is missing")).StatusCode);
        Assert.Equal(OrganisationState.NeedsChanges, await State(id));
        var mail = Assert.Single(sent, m => m.To == "desk@review-clinic.example");
        Assert.Contains("signed data processing agreement is missing", mail.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyTheMovesTheStateMachineAllowsHappenAndSuspendThenReinstateRoundTrips()
    {
        var (id, _) = await Org();
        using var c = await Console(PlatformRole.Reviewer);
        // Reinstate is for a suspended organisation and approve for a request in review; suspend needs an approved one.
        foreach (var route in new[] { "reinstate", "suspend" })
        {
            var r = await Decide(c, id, route);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("transition", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("state")[0].GetString());
        }
        Assert.Equal(HttpStatusCode.OK, (await Decide(c, id, "approve")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(c, id, "approve")).StatusCode); // already approved
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(c, id, "reject")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Decide(c, id, "suspend", "Complaint under review by Medvya")).StatusCode);
        Assert.Equal(OrganisationState.Suspended, await State(id));
        Assert.Equal(HttpStatusCode.OK, (await Decide(c, id, "reinstate", "Complaint closed, no action needed")).StatusCode);
        Assert.Equal(OrganisationState.Approved, await State(id));
        Assert.Equal(["organisation.approve", "organisation.suspend", "organisation.reinstate"], (await PlatformAudit(id)).Select(a => a.Action));

        var (rejected, _) = await Org();
        Assert.Equal(HttpStatusCode.OK, (await Decide(c, rejected, "reject", "Registration certificate is not legible")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(c, rejected, "approve")).StatusCode); // rejected is final for the request
        Assert.Equal(HttpStatusCode.NotFound, (await Decide(c, Guid.NewGuid(), "approve")).StatusCode);
    }

    [Fact]
    public async Task TheQueueFiltersByStateAndHoldsNoPatientField()
    {
        var (waiting, _) = await Org();
        var (changes, _) = await Org(state: OrganisationState.NeedsChanges);
        var (decided, _) = await Org(state: OrganisationState.Approved);
        using var c = await Console(PlatformRole.Finance);
        async Task<List<JsonElement>> Queue(string filter) =>
            [.. (await c.GetFromJsonAsync<JsonElement>(new Uri($"/v1/organisations/?filter={filter}", UriKind.Relative))).EnumerateArray()];
        static bool Has(List<JsonElement> q, Guid id) => q.Any(x => x.GetProperty("tenantId").GetGuid() == id);

        var w = await Queue("Waiting");
        Assert.True(Has(w, waiting)); Assert.False(Has(w, changes)); Assert.False(Has(w, decided));
        var n = await Queue("NeedsChanges");
        Assert.True(Has(n, changes)); Assert.False(Has(n, waiting));
        var d = await Queue("Decided");
        Assert.True(Has(d, decided)); Assert.False(Has(d, waiting));
        var all = await Queue("All");
        Assert.True(Has(all, waiting) && Has(all, changes) && Has(all, decided));

        // Control-plane facts only: no name of a person, no patient or staff field (the architecture test pins the types).
        var row = w.Single(x => x.GetProperty("tenantId").GetGuid() == waiting);
        Assert.Equal(["country", "documents", "itContactEmail", "itContactName", "name", "reason", "state", "submittedAt", "tenantId", "type"], row.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(4, row.GetProperty("documents").GetArrayLength());
    }

    [Fact]
    public async Task TheDetailShowsTheProfileDocumentsAndWhoDecidedWhat()
    {
        var (id, _) = await Org();
        using var c = await Console(PlatformRole.Reviewer);
        await Decide(c, id, "request-changes", "The tax registration page is cut off");
        var detail = await c.GetFromJsonAsync<JsonElement>(new Uri($"/v1/organisations/{id}", UriKind.Relative));
        Assert.Equal("desk@review-clinic.example", detail.GetProperty("contactEmail").GetString());
        Assert.Equal(4, detail.GetProperty("documents").GetArrayLength());
        var entry = Assert.Single(detail.GetProperty("history").EnumerateArray());
        Assert.Equal("NeedsChanges", entry.GetProperty("state").GetString());
        Assert.StartsWith("Reviewer ", entry.GetProperty("actor").GetString(), StringComparison.Ordinal); // the member's console name
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(new Uri($"/v1/organisations/{Guid.NewGuid()}", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task OpeningADocumentIsPrivateLoggedInBothAuditTrailsAndNeverACachedDownload()
    {
        var (id, doc) = await Org();
        using var c = await Console(PlatformRole.Support);
        var opened = await c.GetAsync(new Uri($"/v1/organisations/{id}/documents/{doc}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal(Pdf, await opened.Content.ReadAsByteArrayAsync());
        Assert.Equal("no-store", opened.Headers.CacheControl!.ToString());
        Assert.Equal("inline", opened.Content.Headers.ContentDisposition!.DispositionType);
        Assert.DoesNotContain("blob.test", string.Join(' ', opened.Headers.Select(h => string.Concat(h.Value))), StringComparison.Ordinal); // no storage link reaches the browser

        await using (var audit = sql.Audit(id))
        {
            var viewed = Assert.Single(await audit.Events.Where(e => e.Action == "organisation.document.view").ToListAsync());
            Assert.Equal(doc, viewed.ResourceId);
        }
        Assert.Equal("organisation.document.view", Assert.Single(await PlatformAudit(id)).Action);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(new Uri($"/v1/organisations/{id}/documents/{Guid.NewGuid()}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(new Uri($"/v1/organisations/{Guid.NewGuid()}/documents/{doc}", UriKind.Relative))).StatusCode);
    }

    // The console login's second path: whatever the host's code does, the database refuses a clinic table to medvya_review (a patient is never in reach).
    [Theory]
    [InlineData("patients.\"Patients\"")]
    [InlineData("encounters.\"Encounters\"")]
    [InlineData("scheduling.\"Appointments\"")]
    [InlineData("prescribing.\"Medicines\"")]
    [InlineData("identity.\"Users\"")]
    [InlineData("tenancy.\"Memberships\"")]
    [InlineData("tenancy.\"DoctorLicences\"")]
    [InlineData("tenancy.\"Invitations\"")]
    [InlineData("tenancy.\"Facilities\"")]
    public async Task TheReviewLoginCannotReadClinicData(string table)
    {
        await using var conn = await sql.OpenAs("review_user");
        // Even with the operator setting and a tenant of its own the table is closed to this login.
        await using (var set = new NpgsqlCommand("SELECT set_config('medvya.operator', gen_random_uuid()::text, false), set_config('medvya.tenant_id', gen_random_uuid()::text, false)", conn))
            await set.ExecuteNonQueryAsync();
        await using var cmd = new NpgsqlCommand($"SELECT * FROM {table} LIMIT 1", conn);
        var e = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteScalarAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, e.SqlState);
    }

    [Fact]
    public async Task TheReviewLoginSeesNoOrganisationWithoutTheOperatorSettingAndCannotDeleteOrChangeAuditRowsOrMoreThanADecision()
    {
        var (id, _) = await Org();
        await using var conn = await sql.OpenAs("review_user");
        await using (var none = new NpgsqlCommand("SELECT count(*) FROM tenancy.\"Tenants\"", conn))
            Assert.Equal(0L, await none.ExecuteScalarAsync());
        foreach (var statement in new[] { "DELETE FROM tenancy.\"Tenants\"", "UPDATE audit.\"AuditEvents\" SET \"Action\" = 'x'", "DELETE FROM audit.\"AuditEvents\"", "INSERT INTO tenancy.\"Facilities\" (\"Id\") VALUES (gen_random_uuid())",
            // A decision changes the state and the reason only, and the audit chain head is all it may read of the audit log.
            "UPDATE tenancy.\"Tenants\" SET \"Name\" = 'renamed'", "UPDATE tenancy.\"OrganisationRequests\" SET \"Type\" = 'Hospital'",
            "SELECT \"Action\", \"ActorUserId\" FROM audit.\"AuditEvents\"" })
        {
            await using var cmd = new NpgsqlCommand(statement, conn);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync())).SqlState);
        }
        Assert.NotEqual(Guid.Empty, id);
    }
}

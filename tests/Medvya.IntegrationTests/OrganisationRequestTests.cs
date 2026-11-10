using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-062 and MDV-P1-064 (ADR-016) on real PostgreSQL under row-level security: the onboarding request (documents, submit, resubmit), the state machine,
/// Medvya operations' approval API (capability, reason on every action, audit, email) and the cross-organisation read of the review queue.
/// Mutation checks: make OperationsPolicies.OrganisationApprove pass any signed-in user and the reader and stranger steps fail; drop the CanMove check
/// in OrganisationStates and the transition steps fail; drop the Required list from SubmitAsync and the incomplete-submit steps fail.
/// </summary>
public sealed class OrganisationRequestTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly Guid approver = Guid.NewGuid(), reader = Guid.NewGuid();
    readonly InMemoryLogos files = new();
    readonly Mail mail = new();
    WebApplicationFactory<Program>? factory;
    static readonly byte[] Pdf = "%PDF-1.4 synthetic"u8.ToArray();

    sealed class Mail : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool Fails { get; set; }
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (Fails) throw new IOException("mail server down");
            lock (Sent) Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await using var db = sql.Context(h.Tenant);
        db.Profiles.Add(new PracticeProfile { Id = Guid.NewGuid(), TenantId = h.Tenant, Email = "desk@iyer-clinic.example", ChangedAt = DateTimeOffset.UtcNow });
        db.OrganisationRequests.Add(new OrganisationRequest { TenantId = h.Tenant, Type = OrganisationType.Clinic });
        await db.SaveChangesAsync();
        await SetStateAsync(OrganisationState.Draft);
    }

    public async Task DisposeAsync()
    {
        await h.CloseAsync();
        if (factory is not null) await factory.DisposeAsync();
    }

    sealed class SignedIn(Guid tenant, Guid facility) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                if (ctx.Request.Headers["X-Test-User"].ToString() is { Length: > 0 } user)
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, user), new Claim("tenant_id", tenant.ToString()), new Claim("facility_id", facility.ToString())], "synthetic"));
                return nxt(ctx);
            });
            next(app);
        };
    }

    HttpClient Client(Guid user)
    {
        factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Operations:OrganisationApprovers:0", approver.ToString())
            .UseSetting("Operations:OrganisationReaders:0", reader.ToString())
            .ConfigureServices(s =>
            {
                s.AddSingleton<IStartupFilter>(new SignedIn(h.Tenant, h.Facility));
                s.AddSingleton<ILogoStore>(files);
                s.AddSingleton<IEmailSender>(mail);
            }));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        return client;
    }

    async Task SetStateAsync(OrganisationState state)
    {
        await using var db = sql.Context(h.Tenant);
        await db.Tenants.ExecuteUpdateAsync(s => s.SetProperty(t => t.State, state));
    }

    async Task<OrganisationState> StateAsync()
    {
        await using var db = sql.Context(h.Tenant);
        return await db.Tenants.Select(t => t.State).SingleAsync();
    }

    static Task<HttpResponseMessage> PutFile(HttpClient c, string kind, byte[] content)
    {
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return c.PutAsync($"/v1/organisation/request/documents/{kind}", body);
    }

    static async Task<string[]> ErrorsAsync(HttpResponseMessage r) =>
        [.. (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateObject().Select(p => p.Name + ":" + p.Value[0].GetString())];

    static async Task UploadAllAsync(HttpClient owner, OrganisationType type)
    {
        foreach (var kind in OrganisationDocumentRules.Required(type, ownerIsDoctor: true))
            Assert.Equal(HttpStatusCode.OK, (await PutFile(owner, kind, Pdf)).StatusCode);
    }

    [Fact]
    public async Task AClinicSubmitsOnlyWithEveryRequiredDocumentAndTheRequestLocksOnSubmit()
    {
        using var owner = Client(h.Admin);
        var view = await owner.GetFromJsonAsync<JsonElement>("/v1/organisation/request/");
        Assert.Equal("Clinic", view.GetProperty("type").GetString());
        Assert.Equal(4, view.GetProperty("requiredDocuments").GetArrayLength()); // the harness owner is a doctor: council registration is asked too

        var incomplete = await owner.PostAsync("/v1/organisation/request/submit", null);
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        Assert.Contains("document.RegistrationCertificate:required", await ErrorsAsync(incomplete));
        Assert.Equal(OrganisationState.Draft, await StateAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await PutFile(owner, "NoSuchKind", Pdf)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutFile(owner, "OwnerId", "plain text, not a document"u8.ToArray())).StatusCode); // the type comes from the bytes
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await PutFile(owner, "OwnerId", [.. "%PDF-"u8.ToArray(), .. new byte[(int)OrganisationDocumentRules.MaxBytes]])).StatusCode);
        Assert.Empty(files.Stored);

        await UploadAllAsync(owner, OrganisationType.Clinic);
        Assert.Equal(HttpStatusCode.OK, (await PutFile(owner, "OwnerId", Pdf)).StatusCode); // a new file replaces the old one
        Assert.Equal(4, files.Stored.Count);
        Assert.All(files.Stored.Keys, k => Assert.StartsWith($"onboarding/{h.Tenant}/", k, StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/v1/organisation/request/submit", null)).StatusCode);
        Assert.Equal(OrganisationState.Submitted, await StateAsync());

        var locked = await PutFile(owner, "OwnerId", Pdf);
        Assert.Equal(["request:locked"], await ErrorsAsync(locked));
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/v1/organisation/request/submit", null)).StatusCode); // already submitted

        using var desk = Client(h.Reception); // the desk is neither owner nor set-up capable
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.GetAsync("/v1/organisation/request/")).StatusCode);
    }

    [Fact]
    public async Task AHospitalAlsoNeedsTheSignatoryLetterTheSignedDpaAndAnItContact()
    {
        await using (var db = sql.Context(h.Tenant)) await db.OrganisationRequests.ExecuteUpdateAsync(s => s.SetProperty(r => r.Type, OrganisationType.Hospital));
        using var owner = Client(h.Admin);
        var errors = await ErrorsAsync(await owner.PostAsync("/v1/organisation/request/submit", null));
        Assert.Contains("document.SignatoryLetter:required", errors);
        Assert.Contains("document.Dpa:required", errors);
        Assert.Contains("itContact.name:required", errors);

        await UploadAllAsync(owner, OrganisationType.Hospital);
        Assert.Equal(["itContact.email:format"], await ErrorsAsync(await owner.PutAsJsonAsync("/v1/organisation/request/it-contact", new { name = "Sanjay Verma (synthetic)", email = "not an address" })));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync("/v1/organisation/request/it-contact", new { name = "Sanjay Verma (synthetic)", email = "it@city-hospital.example" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/v1/organisation/request/submit", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Client(approver).GetAsync("/v1/operations/organisations/")).StatusCode);
        Assert.Equal(OrganisationState.Submitted, await StateAsync());
    }

    [Fact]
    public async Task AClinicHasNoItContact()
    {
        using var owner = Client(h.Admin);
        Assert.Equal(["itContact:notAsked", "itContact.name:required", "itContact.email:required"],
            await ErrorsAsync(await owner.PutAsJsonAsync("/v1/organisation/request/it-contact", new { name = "", email = "" })));
    }

    [Fact]
    public async Task OnlyOperationsMayActAndReadersMayOnlyRead()
    {
        await SetStateAsync(OrganisationState.Submitted);
        var stranger = Client(h.Admin); // a practice owner is not Medvya operations
        var tenant = h.Tenant;
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/v1/operations/organisations/")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsJsonAsync($"/v1/operations/organisations/{tenant}/approve", new { reason = "Documents checked" })).StatusCode);

        var support = Client(reader);
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync("/v1/operations/organisations/")).StatusCode);
        foreach (var action in new[] { "approve", "request-changes", "reject", "suspend", "reinstate" })
            Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsJsonAsync($"/v1/operations/organisations/{tenant}/{action}", new { reason = "Documents checked" })).StatusCode);
        Assert.Equal(OrganisationState.Submitted, await StateAsync());
    }

    [Fact]
    public async Task EveryActionNeedsAReasonOfFiveToFiveHundredCharacters()
    {
        await SetStateAsync(OrganisationState.Submitted);
        var ops = Client(approver);
        foreach (var action in new[] { "approve", "request-changes", "reject", "suspend", "reinstate" })
        {
            var url = $"/v1/operations/organisations/{h.Tenant}/{action}";
            Assert.Equal(["reason:required"], await ErrorsAsync(await ops.PostAsJsonAsync(url, new { reason = "  " })));
            Assert.Equal(["reason:too_short"], await ErrorsAsync(await ops.PostAsJsonAsync(url, new { reason = "ok" })));
            Assert.Equal(["reason:too_long"], await ErrorsAsync(await ops.PostAsJsonAsync(url, new { reason = new string('x', 501) })));
        }
        Assert.Equal(OrganisationState.Submitted, await StateAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await ops.PostAsJsonAsync($"/v1/operations/organisations/{Guid.NewGuid()}/approve", new { reason = "Documents checked" })).StatusCode);
    }

    [Fact]
    public async Task AskForChangesResubmitRejectApproveAndSuspendFollowTheStateMachineAreAuditedAndEmailed()
    {
        await SetStateAsync(OrganisationState.Submitted);
        var ops = Client(approver);
        var owner = Client(h.Admin);
        async Task<HttpStatusCode> Act(string action, string reason = "Registration certificate is unreadable") =>
            (await ops.PostAsJsonAsync($"/v1/operations/organisations/{h.Tenant}/{action}", new { reason })).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Act("suspend")); // not Approved yet: the machine refuses
        Assert.Equal(HttpStatusCode.NoContent, await Act("request-changes"));
        Assert.Equal(OrganisationState.NeedsChanges, await StateAsync());
        var mailed = Assert.Single(mail.Sent);
        Assert.Equal("desk@iyer-clinic.example", mailed.To);
        Assert.Contains("Registration certificate is unreadable", mailed.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Meera", mailed.Body, StringComparison.Ordinal); // no patient data in the email
        var view = await owner.GetFromJsonAsync<JsonElement>("/v1/organisation/request/");
        Assert.Equal("Registration certificate is unreadable", view.GetProperty("reason").GetString()); // the applicant sees why

        Assert.Equal(HttpStatusCode.BadRequest, await Act("approve")); // NeedsChanges goes back through Submitted
        await UploadAllAsync(owner, OrganisationType.Clinic); // editable again
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/v1/organisation/request/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, await Act("reject", "Not a registered clinic"));
        Assert.Equal(OrganisationState.Rejected, await StateAsync());
        Assert.Equal(HttpStatusCode.BadRequest, await Act("approve")); // Rejected is final for the request
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/v1/organisation/request/submit", null)).StatusCode);

        await SetStateAsync(OrganisationState.Submitted);
        Assert.Equal(HttpStatusCode.NoContent, await Act("approve", "Documents checked"));
        Assert.Equal(OrganisationState.Approved, await StateAsync());
        Assert.Equal(HttpStatusCode.NoContent, await Act("suspend", "Payment dispute"));
        Assert.Equal(OrganisationState.Suspended, await StateAsync());
        Assert.Equal(HttpStatusCode.BadRequest, await Act("approve", "Dispute settled")); // approve is for a request in review
        Assert.Equal(HttpStatusCode.NoContent, await Act("reinstate", "Dispute settled")); // back to Approved, with a reason
        Assert.Equal(HttpStatusCode.BadRequest, await Act("reinstate", "Not suspended")); // only a suspended organisation is reinstated
        Assert.Equal(OrganisationState.Approved, await StateAsync());

        await using var db = sql.Context(h.Tenant);
        var changes = await db.SettingsChanges.Where(c => c.Section == "organisation").ToListAsync();
        var decisions = changes.Where(c => c.After!.Contains("\"state\":", StringComparison.Ordinal)).ToList();
        Assert.All(decisions.Where(c => !c.After!.Contains("\"state\":\"Submitted\"", StringComparison.Ordinal)), c => Assert.Equal(approver, c.ActorUserId)); // the audit names the operator
        Assert.All(decisions.Where(c => c.After!.Contains("\"state\":\"Submitted\"", StringComparison.Ordinal)), c => Assert.Equal(h.Admin, c.ActorUserId)); // the submit is the applicant's
        Assert.Contains(changes, c => c.After!.Contains("Payment dispute", StringComparison.Ordinal));
        await using var audit = sql.Audit(h.Tenant);
        Assert.Contains("organisation.state.Suspended", await audit.Events.Select(e => e.Action).ToListAsync());
        Assert.Equal(5, mail.Sent.Count); // changes, rejection, approval, suspension, reinstatement; the applicant's own submits send nothing
        Assert.All(mail.Sent, m => Assert.Equal("desk@iyer-clinic.example", m.To));
    }

    [Fact]
    public async Task AHospitalWithoutItsSignedDpaCannotBeApprovedAndOneWithItCan()
    {
        await using (var db = sql.Context(h.Tenant)) await db.OrganisationRequests.ExecuteUpdateAsync(s => s.SetProperty(r => r.Type, OrganisationType.Hospital));
        using var owner = Client(h.Admin);
        foreach (var kind in OrganisationDocumentRules.Required(OrganisationType.Hospital, false).Where(k => k != OrganisationDocumentRules.Dpa))
            Assert.Equal(HttpStatusCode.OK, (await PutFile(owner, kind, Pdf)).StatusCode);
        await SetStateAsync(OrganisationState.Submitted); // as if sent (the submit rule itself is tested above)
        var ops = Client(approver);
        var refused = await ops.PostAsJsonAsync($"/v1/operations/organisations/{h.Tenant}/approve", new { reason = "Documents checked" });
        Assert.Equal(["document.Dpa:required"], await ErrorsAsync(refused));
        Assert.Equal(OrganisationState.Submitted, await StateAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await ops.PostAsJsonAsync($"/v1/operations/organisations/{h.Tenant}/request-changes", new { reason = "Upload the signed DPA" })).StatusCode); // the other way out

        await SetStateAsync(OrganisationState.Draft);
        Assert.Equal(HttpStatusCode.OK, (await PutFile(owner, OrganisationDocumentRules.Dpa, Pdf)).StatusCode);
        await SetStateAsync(OrganisationState.Submitted);
        Assert.Equal(HttpStatusCode.NoContent, (await ops.PostAsJsonAsync($"/v1/operations/organisations/{h.Tenant}/approve", new { reason = "Documents checked" })).StatusCode);
    }

    [Fact]
    public async Task TheDetailShowsTheProfileDocumentsAndWhoDecidedWhatAndNothingAboutPatients()
    {
        using var owner = Client(h.Admin);
        await UploadAllAsync(owner, OrganisationType.Clinic);
        await SetStateAsync(OrganisationState.Submitted);
        var ops = Client(approver);
        Assert.Equal(HttpStatusCode.NoContent, (await ops.PostAsJsonAsync($"/v1/operations/organisations/{h.Tenant}/request-changes", new { reason = "Tax registration is unreadable" })).StatusCode);

        var detail = await Client(reader).GetFromJsonAsync<JsonElement>($"/v1/operations/organisations/{h.Tenant}");
        Assert.Equal(["address", "contactEmail", "country", "documents", "history", "itContactEmail", "itContactName", "name", "reason", "registrationNumber", "state", "submittedAt", "taxNumber", "type"],
            detail.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("desk@iyer-clinic.example", detail.GetProperty("contactEmail").GetString());
        Assert.Equal(4, detail.GetProperty("documents").GetArrayLength());
        var entry = Assert.Single(detail.GetProperty("history").EnumerateArray());
        Assert.Equal(("NeedsChanges", "Tax registration is unreadable"), (entry.GetProperty("state").GetString(), entry.GetProperty("reason").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await Client(reader).GetAsync($"/v1/operations/organisations/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/v1/operations/organisations/{h.Tenant}")).StatusCode); // not Medvya operations
    }

    [Fact]
    public async Task TheQueueFiltersAreWaitingNeedsChangesDecidedAndAll()
    {
        await SetStateAsync(OrganisationState.NeedsChanges);
        var ops = Client(reader);
        async Task<string> Seen() => (await Listed("Waiting") ? "W" : "-") + (await Listed("NeedsChanges") ? "N" : "-") + (await Listed("Decided") ? "D" : "-") + (await Listed("All") ? "A" : "-");
        async Task<bool> Listed(string filter) => (await ops.GetFromJsonAsync<JsonElement>($"/v1/operations/organisations/?filter={filter}")).EnumerateArray().Any(i => i.GetProperty("tenantId").GetGuid() == h.Tenant);
        Assert.Equal("-N-A", await Seen());
        await SetStateAsync(OrganisationState.Suspended);
        Assert.Equal("--DA", await Seen());
    }

    [Fact]
    public async Task AFailedEmailDoesNotUndoTheDecision()
    {
        await SetStateAsync(OrganisationState.Submitted);
        mail.Fails = true;
        var ops = Client(approver);
        Assert.Equal(HttpStatusCode.NoContent, (await ops.PostAsJsonAsync($"/v1/operations/organisations/{h.Tenant}/approve", new { reason = "Documents checked" })).StatusCode);
        Assert.Equal(OrganisationState.Approved, await StateAsync()); // the applicant reads the outcome on the status page
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task TheQueueListsEveryOrganisationsRequestAndDocumentsAndNothingElseAndIsAuditedWhenADocumentIsOpened()
    {
        using var owner = Client(h.Admin);
        await UploadAllAsync(owner, OrganisationType.Clinic);
        await SetStateAsync(OrganisationState.Submitted);
        var other = Guid.NewGuid();
        await using (var db = sql.Context(other))
        {
            db.Tenants.Add(new Tenant { State = OrganisationState.Submitted, Id = other, Name = "Second Clinic (synthetic)", CountryCode = "AE", ResidencyRegion = "uaenorth" });
            db.OrganisationRequests.Add(new OrganisationRequest { TenantId = other, Type = OrganisationType.Hospital, SubmittedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var ops = Client(reader);
        var items = (await ops.GetFromJsonAsync<JsonElement>("/v1/operations/organisations/?filter=Waiting")).EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("tenantId").GetGuid() == h.Tenant && i.GetProperty("documents").GetArrayLength() == 4);
        Assert.Contains(items, i => i.GetProperty("tenantId").GetGuid() == other && i.GetProperty("type").GetString() == "Hospital");
        // Control-plane fields only: no patient, staff or schedule data is part of the item.
        Assert.Equal(["country", "documents", "itContactEmail", "itContactName", "name", "reason", "state", "submittedAt", "tenantId", "type"],
            items[0].EnumerateObject().Select(p => p.Name).Order());
        Assert.All(items, i => Assert.Equal("Submitted", i.GetProperty("state").GetString()));
        Assert.DoesNotContain((await ops.GetFromJsonAsync<JsonElement>("/v1/operations/organisations/?filter=Decided")).EnumerateArray(), i => i.GetProperty("tenantId").GetGuid() == h.Tenant);

        // Row-level security: the queue's read widens three tables only, and a tenant-scoped connection still sees its own organisation alone.
        await using (var scoped = sql.Context(h.Tenant))
        {
            Assert.Equal([h.Tenant], await scoped.OrganisationRequests.IgnoreQueryFilters().Select(r => r.TenantId).ToListAsync());
            Assert.Equal([h.Tenant], await scoped.Tenants.IgnoreQueryFilters().Select(t => t.Id).ToListAsync());
        }
        await using (var none = sql.Context(null))
        {
            Assert.Empty(await none.OrganisationRequests.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await none.OrganisationDocuments.IgnoreQueryFilters().ToListAsync());
        }

        var doc = items.Single(i => i.GetProperty("tenantId").GetGuid() == h.Tenant).GetProperty("documents")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/v1/operations/organisations/{h.Tenant}/documents/{doc}")).StatusCode); // the applicant cannot use the review route
        var link = await ops.GetFromJsonAsync<JsonElement>($"/v1/operations/organisations/{h.Tenant}/documents/{doc}");
        Assert.StartsWith($"https://blob.test/onboarding/{h.Tenant}/", link.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await ops.GetAsync($"/v1/operations/organisations/{h.Tenant}/documents/{Guid.NewGuid()}")).StatusCode);
        await using var audit = sql.Audit(h.Tenant);
        var viewed = await audit.Events.Where(e => e.Action == "organisation.document.view").SingleAsync();
        Assert.Equal((reader, doc), (viewed.ActorUserId, viewed.ResourceId)); // naming the operator and the document, never its content
    }
}

/// <summary>MDV-P1-062: the state machine of ADR-016 (no database).</summary>
public class OrganisationTransitionTests
{
    [Theory]
    [InlineData(OrganisationState.Draft, OrganisationState.Submitted, true)]
    [InlineData(OrganisationState.NeedsChanges, OrganisationState.Submitted, true)]
    [InlineData(OrganisationState.Submitted, OrganisationState.Approved, true)]
    [InlineData(OrganisationState.Submitted, OrganisationState.NeedsChanges, true)]
    [InlineData(OrganisationState.Submitted, OrganisationState.Rejected, true)]
    [InlineData(OrganisationState.Approved, OrganisationState.Suspended, true)]
    [InlineData(OrganisationState.Suspended, OrganisationState.Approved, true)]
    [InlineData(OrganisationState.Draft, OrganisationState.Approved, false)]
    [InlineData(OrganisationState.NeedsChanges, OrganisationState.Approved, false)]
    [InlineData(OrganisationState.Rejected, OrganisationState.Submitted, false)]
    [InlineData(OrganisationState.Rejected, OrganisationState.Approved, false)]
    [InlineData(OrganisationState.Approved, OrganisationState.Rejected, false)]
    [InlineData(OrganisationState.Submitted, OrganisationState.Suspended, false)]
    [InlineData(OrganisationState.Suspended, OrganisationState.Submitted, false)]
    public void OnlyTheDocumentedMovesAreAllowed(OrganisationState from, OrganisationState to, bool allowed) => Assert.Equal(allowed, OrganisationRules.CanMove(from, to));

    [Fact]
    public void RequiredDocumentsFollowTheTypeAndWhetherTheOwnerIsADoctor()
    {
        Assert.Equal(["RegistrationCertificate", "TaxRegistration", "OwnerId", "MedicalCouncilRegistration"], OrganisationDocumentRules.Required(OrganisationType.Clinic, true));
        Assert.Equal(["RegistrationCertificate", "TaxRegistration", "OwnerId"], OrganisationDocumentRules.Required(OrganisationType.Clinic, false));
        Assert.Equal(["HospitalRegistration", "TaxRegistration", "OwnerId", "SignatoryLetter", "Dpa"], OrganisationDocumentRules.Required(OrganisationType.Hospital, true)); // the hospital attests its own doctors
    }

    [Fact]
    public void AFileIsTypedFromItsBytesNeverItsName()
    {
        Assert.Equal("application/pdf", OrganisationDocumentRules.ContentTypeOf("%PDF-1.7"u8));
        Assert.Equal("image/png", OrganisationDocumentRules.ContentTypeOf([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]));
        Assert.Equal("image/jpeg", OrganisationDocumentRules.ContentTypeOf([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Null(OrganisationDocumentRules.ContentTypeOf("<svg onload=alert(1)>"u8));
        Assert.Null(OrganisationDocumentRules.ContentTypeOf("GIF89a"u8));
    }
}

extern alias platform;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.BuildingBlocks.Domain;
using Medvya.Platform.Domain;
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
/// MDV-P1-064 on real PostgreSQL: the platform console's organisation queue (ADR-014 amended). Console roles decide who may read and act; every
/// decision needs a reason, changes the state, names the console member in the clinic audit and the platform log, and emails the applicant; the
/// review login (medvya_review) reaches the onboarding tables and nothing else.
/// Mutation checks: let PlatformPolicies.OrganisationApprove accept any verified member and the Support and Finance steps fail; drop the
/// medvya_review grants in table-rights.sql and every decision fails; grant it SELECT on patients."Patients" and the database step fails.
/// </summary>
public sealed class PlatformOrganisationReviewTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly Mail mail = new();
    WebApplicationFactory<ConsoleProgram> factory = null!;

    sealed class Mail : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            lock (Sent) Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await using (var db = sql.Context(h.Tenant))
        {
            db.Profiles.Add(new PracticeProfile { Id = Guid.NewGuid(), TenantId = h.Tenant, Email = "desk@iyer-clinic.example", ChangedAt = DateTimeOffset.UtcNow });
            db.OrganisationRequests.Add(new OrganisationRequest { TenantId = h.Tenant, Type = OrganisationType.Clinic, SubmittedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        await SetStateAsync(OrganisationState.Submitted);
        factory = PlatformConsoleTests.CreateFactory(sql, configure: b => b
            .UseSetting("ConnectionStrings:Review", sql.As("medvya_review"))
            .ConfigureServices(s => s.AddSingleton<IEmailSender>(mail).AddSingleton<ILogoStore>(h.Logos)));
    }

    public async Task DisposeAsync()
    {
        await h.CloseAsync();
        await factory.DisposeAsync();
    }

    async Task<(PlatformMember Member, HttpClient Browser)> As(PlatformRole role)
    {
        var (member, key) = await PlatformConsoleTests.Seed(sql, role);
        return (member, await PlatformConsoleTests.SignedIn(factory, member, key));
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

    Task<HttpResponseMessage> Act(HttpClient browser, string action, string? reason) =>
        PlatformConsoleTests.Post(browser, $"/v1/organisations/{h.Tenant}/{action}", new { reason });

    [Fact]
    public async Task OnlyOperationsAndReviewersDecideSupportOnlyReadsAndOwnerAndFinanceGetNothing()
    {
        foreach (var role in new[] { PlatformRole.Owner, PlatformRole.Finance })
        {
            var (_, b) = await As(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync(new Uri("/v1/organisations", UriKind.Relative))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync(new Uri($"/v1/organisations/{h.Tenant}", UriKind.Relative))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Act(b, "approve", "Documents checked")).StatusCode);
        }
        var (_, support) = await As(PlatformRole.Support);
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync(new Uri("/v1/organisations", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(support, "approve", "Documents checked")).StatusCode);
        Assert.Equal(OrganisationState.Submitted, await StateAsync());

        var (_, reviewer) = await As(PlatformRole.Reviewer);
        Assert.Equal(HttpStatusCode.NoContent, (await Act(reviewer, "approve", "Documents checked")).StatusCode);
        Assert.Equal(OrganisationState.Approved, await StateAsync());
    }

    [Fact]
    public async Task EveryDecisionNeedsAReason()
    {
        var (_, ops) = await As(PlatformRole.Operations);
        foreach (var reason in new[] { null, "", "   ", "abc", new string('x', 501) })
            foreach (var action in new[] { "approve", "request-changes", "reject", "suspend", "reinstate" })
                Assert.Equal(HttpStatusCode.BadRequest, (await Act(ops, action, reason)).StatusCode);
        Assert.Equal(OrganisationState.Submitted, await StateAsync());
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task EachDecisionChangesTheStateNamesTheMemberInBothAuditLogsAndEmailsTheReason()
    {
        var (member, ops) = await As(PlatformRole.Operations);
        Assert.Equal(HttpStatusCode.BadRequest, (await Act(ops, "suspend", "Payment dispute")).StatusCode); // not Approved: the machine refuses
        Assert.Equal(HttpStatusCode.NoContent, (await Act(ops, "request-changes", "Registration certificate is unreadable")).StatusCode);
        Assert.Equal(OrganisationState.NeedsChanges, await StateAsync());
        var mailed = Assert.Single(mail.Sent);
        Assert.Equal("desk@iyer-clinic.example", mailed.To);
        Assert.Contains("Registration certificate is unreadable", mailed.Body, StringComparison.Ordinal);

        await SetStateAsync(OrganisationState.Submitted);
        Assert.Equal(HttpStatusCode.NoContent, (await Act(ops, "reject", "Not a registered clinic")).StatusCode);
        Assert.Equal(OrganisationState.Rejected, await StateAsync());
        await SetStateAsync(OrganisationState.Submitted);
        Assert.Equal(HttpStatusCode.NoContent, (await Act(ops, "approve", "Documents checked")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Act(ops, "reinstate", "Not suspended")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Act(ops, "suspend", "Payment dispute")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Act(ops, "approve", "Dispute settled")).StatusCode); // approve is for a request in review
        Assert.Equal(HttpStatusCode.NoContent, (await Act(ops, "reinstate", "Dispute settled")).StatusCode);
        Assert.Equal(OrganisationState.Approved, await StateAsync());
        Assert.Equal(5, mail.Sent.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await PlatformConsoleTests.Post(ops, $"/v1/organisations/{Guid.NewGuid()}/approve", new { reason = "Documents checked" })).StatusCode);

        await using (var db = sql.Context(h.Tenant))
        {
            var decisions = await db.SettingsChanges.Where(c => c.Section == "organisation").ToListAsync();
            Assert.Equal(5, decisions.Count);
            Assert.All(decisions, c => Assert.Equal(member.Id, c.ActorUserId)); // the clinic audit names the console member
        }
        await using (var audit = sql.Audit(h.Tenant))
            Assert.Contains("organisation.state.Suspended", await audit.Events.Select(e => e.Action).ToListAsync());
        await using (var platform = PlatformConsoleTests.Db(sql))
        {
            var logged = await platform.AuditEntries.Where(a => a.SubjectTenantId == h.Tenant).OrderBy(a => a.OccurredAt).ThenBy(a => a.Id).ToListAsync();
            Assert.Equal(["organisation.request-changes", "organisation.reject", "organisation.approve", "organisation.suspend", "organisation.reinstate"], logged.Select(a => a.Action));
            Assert.All(logged, a => Assert.Equal(member.Id, a.ActorMemberId));
            Assert.Equal("Dispute settled", logged[^1].Reason);
        }

        var detail = await ops.GetFromJsonAsync<JsonElement>(new Uri($"/v1/organisations/{h.Tenant}", UriKind.Relative));
        Assert.All(detail.GetProperty("history").EnumerateArray(), e => Assert.Equal(member.DisplayName, e.GetProperty("actor").GetString()));
    }

    [Fact]
    public async Task TheQueueAndDetailHoldOnlyTheRequestNeverClinicData()
    {
        var (_, ops) = await As(PlatformRole.Operations);
        var queue = await ops.GetFromJsonAsync<JsonElement>(new Uri("/v1/organisations?filter=Waiting", UriKind.Relative));
        var item = Assert.Single(queue.EnumerateArray(), i => i.GetProperty("tenantId").GetGuid() == h.Tenant);
        Assert.Equal(["country", "documents", "itContactEmail", "itContactName", "name", "reason", "state", "submittedAt", "tenantId", "type"],
            item.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(queue.EnumerateArray(), i => i.GetProperty("state").GetString() != "Submitted");
        var detail = await ops.GetFromJsonAsync<JsonElement>(new Uri($"/v1/organisations/{h.Tenant}", UriKind.Relative));
        Assert.Equal(["address", "contactEmail", "country", "documents", "history", "itContactEmail", "itContactName", "name", "reason", "registrationNumber", "state", "submittedAt", "taxNumber", "type"],
            detail.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.NotFound, (await ops.GetAsync(new Uri($"/v1/organisations/{Guid.NewGuid()}", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task OpeningADocumentGivesAShortLinkAndIsAuditedNamingTheMember()
    {
        var documentId = Guid.NewGuid();
        await using (var db = sql.Context(h.Tenant))
        {
            db.OrganisationDocuments.Add(new OrganisationDocument { Id = documentId, TenantId = h.Tenant, Kind = OrganisationDocumentRules.RegistrationCertificate,
                BlobKey = $"organisations/{h.Tenant}/registration.pdf", ContentType = "application/pdf", SizeBytes = 18, UploadedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var (_, finance) = await As(PlatformRole.Finance);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.GetAsync(new Uri($"/v1/organisations/{h.Tenant}/documents/{documentId}", UriKind.Relative))).StatusCode);
        var (member, ops) = await As(PlatformRole.Operations);
        var link = await ops.GetFromJsonAsync<JsonElement>(new Uri($"/v1/organisations/{h.Tenant}/documents/{documentId}", UriKind.Relative));
        Assert.True(link.GetProperty("expiresAt").GetDateTimeOffset() <= DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.Equal(HttpStatusCode.NotFound, (await ops.GetAsync(new Uri($"/v1/organisations/{h.Tenant}/documents/{Guid.NewGuid()}", UriKind.Relative))).StatusCode);
        await using var audit = sql.Audit(h.Tenant);
        var view = Assert.Single(await audit.Events.Where(e => e.Action == "organisation.document.view").ToListAsync());
        Assert.Equal((member.Id, documentId), (view.ActorUserId!.Value, view.ResourceId!.Value));
    }

    // The database layer: whatever the console code does, its review login cannot reach clinic data, and changes only what a decision changes.
    [Theory]
    [InlineData("patients.\"Patients\"")]
    [InlineData("encounters.\"Encounters\"")]
    [InlineData("scheduling.\"Appointments\"")]
    [InlineData("identity.\"Users\"")]
    [InlineData("tenancy.\"Invitations\"")]
    [InlineData("audit.\"AuditEvents\"")]
    [InlineData("platform.\"Members\"")]
    public async Task TheReviewLoginCannotReadClinicData(string table)
    {
        await using var conn = await sql.OpenAs("review_user", h.Tenant);
        await using var cmd = new NpgsqlCommand($"SELECT * FROM {table} LIMIT 1", conn); // every column: the chain-head and policy columns alone are granted
        var e = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteScalarAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, e.SqlState);
    }

    [Fact]
    public async Task TheReviewLoginChangesOnlyTheStateAndReasonAndSeesNoOrganisationWithoutAScope()
    {
        await using (var conn = await sql.OpenAs("review_user", h.Tenant))
        {
            await using var rename = new NpgsqlCommand("UPDATE tenancy.\"Tenants\" SET \"Name\" = 'changed'", conn);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => rename.ExecuteNonQueryAsync())).SqlState);
            await using var roles = new NpgsqlCommand("SELECT \"Role\" FROM tenancy.\"Memberships\"", conn); // the policy's columns only
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => roles.ExecuteNonQueryAsync())).SqlState);
            await using var delete = new NpgsqlCommand("DELETE FROM tenancy.\"OrganisationRequests\"", conn);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync())).SqlState);
        }
        await using (var none = await sql.OpenAs("review_user"))
        {
            await using var count = new NpgsqlCommand("SELECT count(*) FROM tenancy.\"Tenants\"", none);
            Assert.Equal(0L, await count.ExecuteScalarAsync()); // no tenant and no operator setting: row-level security shows nothing
        }
    }
}

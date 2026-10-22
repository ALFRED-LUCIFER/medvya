using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Medvya.Audit.Infrastructure;
using Medvya.Patients.Infrastructure;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Medvya.IntegrationTests.PatientRulesTests;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-018 over HTTP against real SQL Server: capability policy from the active membership, search text only in the POST body,
/// privacy-safe rows, one audit event per request, facility scope and rate limiting. Synthetic data only.
/// </summary>
public sealed class PatientEndpointTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly Guid tenant = Guid.NewGuid(), facility = Guid.NewGuid(), otherFacility = Guid.NewGuid();
    readonly Dictionary<string, Guid> users = [];
    Guid meera, elsewhere;

    public async Task InitializeAsync()
    {
        await using (var db = sql.Context(tenant))
        {
            db.Tenants.Add(new Tenant { Id = tenant, Name = "Iyer Family Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            db.Facilities.Add(new Facility { Id = facility, TenantId = tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" });
            db.Facilities.Add(new Facility { Id = otherFacility, TenantId = tenant, Name = "Besant Nagar (synthetic)", TimeZone = "Asia/Kolkata" });
            foreach (var (name, role, status) in new[]
            {
                ("receptionist", Role.Receptionist, MembershipStatus.Active), ("nurse", Role.Nurse, MembershipStatus.Active),
                ("admin", Role.PracticeAdmin, MembershipStatus.Active), ("it", Role.HospitalIt, MembershipStatus.Active),
                ("patient", Role.Patient, MembershipStatus.Active), ("offboarded", Role.Receptionist, MembershipStatus.Offboarded),
            })
            {
                users[name] = Guid.NewGuid();
                db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = users[name], FacilityId = facility, Role = role, Status = status });
            }
            await db.SaveChangesAsync();
        }
        users["stranger"] = Guid.NewGuid(); // signed in, but no membership here
        meera = await Register(facility, "Meera Iyer (synthetic)", "+91 90000 00101");
        elsewhere = await Register(otherFacility, "Rahul Menon (synthetic)", "+91 90000 00102");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    async Task<Guid> Register(Guid at, string name, string mobile)
    {
        var registry = new PatientRegistry(sql.Patients(tenant), new SqlServerFixture.FixedTenant(tenant), new PatientRegistryTests.FixedFacility(at),
            new SignedInUser(Guid.NewGuid()), new AuditLog(sql.Audit(tenant)), TimeProvider.System);
        return (await registry.RegisterAsync(IndiaBase with { FacilityId = at, FullName = name, Mobile = mobile })).PatientId;
    }

    sealed class SignedInUser(Guid id) : Medvya.BuildingBlocks.Domain.ICurrentUser { public Guid? UserId => id; }

    WebApplicationFactory<Program> Host(int searchesPerMinute = 1000, int duplicateChecksPerMinute = 1000) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("RateLimiting:PatientSearchPerMinute", searchesPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .UseSetting("RateLimiting:DuplicateChecksPerMinute", duplicateChecksPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ConfigureTestServices(s =>
            {
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, SignedIn>(SignedIn.SchemeName, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => o.DefaultScheme = o.DefaultAuthenticateScheme = o.DefaultChallengeScheme = SignedIn.SchemeName);
                s.AddSingleton(new TenantOf(tenant));
            }));

    sealed record TenantOf(Guid Id);

    /// <summary>
    /// Stands in for the session after the facility picker: user, tenant and facility claims from test headers. A real
    /// authentication handler, so the principal exists only from the host's authentication step on, exactly as a session
    /// cookie's would: middleware that runs earlier (the rate limiter) sees an anonymous caller here too.
    /// </summary>
    // shortcut: the claims come from headers because sign-in does not issue tenant/facility claims before the facility picker (MDV-P0-012).
    sealed class SignedIn(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TenantOf tenant)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "synthetic";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
            List<Claim> claims = [new(ClaimTypes.NameIdentifier, user.ToString()), new("tenant_id", tenant.Id.ToString())];
            if (Request.Headers.TryGetValue("X-Test-Facility", out var f)) claims.Add(new("facility_id", f.ToString()));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }

    HttpClient Client(WebApplicationFactory<Program> host, string? user, bool withFacility = true)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (user is not null) client.DefaultRequestHeaders.Add("X-Test-User", users[user].ToString());
        if (user is not null && withFacility) client.DefaultRequestHeaders.Add("X-Test-Facility", facility.ToString());
        return client;
    }

    static Task<HttpResponseMessage> Search(HttpClient client, object body) => client.PostAsJsonAsync(new Uri("/v1/patients/search", UriKind.Relative), body);

    async Task<List<(string Action, string Purpose, Guid? PatientId)>> AuditTrail()
    {
        await using var db = sql.Audit(tenant);
        return (await db.Events.OrderBy(e => e.OccurredAt).ToListAsync()).Select(e => (e.Action, e.Purpose, e.PatientId)).ToList();
    }

    [Fact]
    public async Task TheDeskSearchesByPostSeesOnlyPrivacySafeRowsAndOpensTheRecord()
    {
        using var host = Host();
        using var desk = Client(host, "receptionist");

        var found = await Search(desk, new { query = "meera" });
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var json = await found.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Name", doc.RootElement.GetProperty("matchedOn").GetString());
        var row = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(("MI", meera), (row.GetProperty("initials").GetString(), row.GetProperty("id").GetGuid()));
        foreach (var leak in new[] { "Meera", "Iyer", "+91", "1980-01-01" }) Assert.DoesNotContain(leak, json, StringComparison.OrdinalIgnoreCase);

        var opened = await desk.GetFromJsonAsync<JsonElement>(new Uri($"/v1/patients/{meera}", UriKind.Relative));
        Assert.Equal(("Meera Iyer (synthetic)", "+919000000101"), (opened.GetProperty("fullName").GetString(), opened.GetProperty("mobile").GetString()));
    }

    [Fact]
    public async Task SearchTextIsNeverAcceptedInTheUrl()
    {
        using var host = Host();
        using var desk = Client(host, "receptionist");
        // No GET search exists, and a query string on the POST is ignored: the text is read from the body only.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await desk.GetAsync(new Uri("/v1/patients/search?query=meera", UriKind.Relative))).StatusCode);
        var post = await desk.PostAsJsonAsync(new Uri("/v1/patients/search?query=meera", UriKind.Relative), new { });
        Assert.Equal("Directory", (await post.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("matchedOn").GetString());
    }

    [Fact]
    public async Task EverySearchAndEveryOpenedRecordWritesExactlyOneAuditEvent()
    {
        using var host = Host();
        using var desk = Client(host, "receptionist");
        var before = (await AuditTrail()).Count;

        Assert.Equal(HttpStatusCode.OK, (await Search(desk, new { query = "+91 90000 00101" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await desk.GetAsync(new Uri($"/v1/patients/{meera}", UriKind.Relative))).StatusCode);

        Assert.Equal([("patient.search", "registration", null), ("patient.read", "registration", meera)], (await AuditTrail())[before..]);
    }

    [Fact]
    public async Task ARefusedSearchOrOpenIsAuditedAsDenied()
    {
        using var host = Host();
        using var it = Client(host, "it");
        await using (var before = sql.Audit(tenant)) Assert.False(await before.Events.AnyAsync(e => e.ActorUserId == users["it"]));

        Assert.Equal(HttpStatusCode.Forbidden, (await Search(it, new { query = "meera" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await it.GetAsync(new Uri($"/v1/patients/{meera}", UriKind.Relative))).StatusCode);

        await using var db = sql.Audit(tenant);
        var denied = await db.Events.Where(e => e.ActorUserId == users["it"]).OrderBy(e => e.OccurredAt).Select(e => new { e.Action, e.PatientId, e.Outcome }).ToListAsync();
        Assert.Equal([new { Action = "patient.search", PatientId = (Guid?)null, Outcome = Medvya.Audit.Domain.AuditOutcome.Denied },
            new { Action = "patient.read", PatientId = (Guid?)meera, Outcome = Medvya.Audit.Domain.AuditOutcome.Denied }], denied);
    }

    [Theory]
    [InlineData("receptionist", HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData("nurse", HttpStatusCode.OK)]             // Register / book patients: Read
    [InlineData("it", HttpStatusCode.Forbidden)]         // no access in the role matrix
    [InlineData("patient", HttpStatusCode.Forbidden)]    // Own: the portal, never the desk directory
    [InlineData("offboarded", HttpStatusCode.Forbidden)] // access ends with the membership, on the next request
    [InlineData("stranger", HttpStatusCode.Forbidden)]   // signed in, no membership at this facility
    [InlineData(null, HttpStatusCode.Unauthorized)]
    public async Task OnlyAnActiveMembershipThatMayRegisterOrBookPatientsCanSearchOrOpen(string? user, HttpStatusCode expected)
    {
        using var host = Host();
        using var client = Client(host, user);
        Assert.Equal(expected, (await Search(client, new { query = "meera" })).StatusCode);
        Assert.Equal(expected, (await client.GetAsync(new Uri($"/v1/patients/{meera}", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task WithoutAnActiveFacilityNothingIsReachable()
    {
        using var host = Host();
        using var client = Client(host, "receptionist", withFacility: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await Search(client, new { })).StatusCode);
    }

    [Fact]
    public async Task AnotherFacilitysRecordIsNotFound()
    {
        using var host = Host();
        using var desk = Client(host, "receptionist");
        Assert.Equal(HttpStatusCode.NotFound, (await desk.GetAsync(new Uri($"/v1/patients/{elsewhere}", UriKind.Relative))).StatusCode);
        // Nothing was opened, so the trail must not say it was.
        await using var db = sql.Audit(tenant);
        var row = await db.Events.SingleAsync(e => e.Action == "patient.read" && e.PatientId == elsewhere);
        Assert.Equal(Medvya.Audit.Domain.AuditOutcome.NotFound, row.Outcome);
    }

    [Fact]
    public async Task AnUnreadableSearchAnswersACodeWithoutEchoingTheInput()
    {
        using var host = Host();
        using var desk = Client(host, "receptionist");
        var res = await Search(desk, new { query = "98470 213" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("full_number_required", body, StringComparison.Ordinal);
        Assert.DoesNotContain("98470", body, StringComparison.Ordinal);
    }

    /// <summary>Per user, not per server: one desk using up its searches must not stop another desk (the limit runs after sign-in).</summary>
    [Fact]
    public async Task TheDuplicateCheckAnswersPrivacySafeCandidatesAndIsAuditedOnce()
    {
        using var host = Host();
        using var desk = Client(host, "receptionist");
        var before = (await AuditTrail()).Count;

        var res = await desk.PostAsJsonAsync(new Uri("/v1/patients/duplicate-check", UriKind.Relative), new { fullName = "Meera Iyer (synthetic)", dateOfBirth = "1980-01-01" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadAsStringAsync();
        var candidate = Assert.Single(JsonDocument.Parse(json).RootElement.GetProperty("candidates").EnumerateArray());
        Assert.Equal((meera, "MI"), (candidate.GetProperty("patientId").GetGuid(), candidate.GetProperty("initials").GetString()));
        foreach (var leak in new[] { "Meera", "Iyer", "+91", "1980" }) Assert.DoesNotContain(leak, json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([("patient.duplicate_check", "registration", null)], (await AuditTrail())[before..]);
    }

    [Fact]
    public async Task OnlyFullRegisterAccessMayCheckForDuplicatesOrRegister()
    {
        using var host = Host();
        using var nurse = Client(host, "nurse"); // Register / book patients: Read
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.PostAsJsonAsync(new Uri("/v1/patients/duplicate-check", UriKind.Relative), new { fullName = "Meera Iyer" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.PostAsJsonAsync(new Uri("/v1/patients", UriKind.Relative), new { fullName = "Meera Iyer" })).StatusCode);
    }

    [Fact]
    public async Task RegistrationTakesTheFacilityFromTheSessionAndWaitsForTheDuplicateDecision()
    {
        using var host = Host();
        using var desk = Client(host, "receptionist");
        var body = new Dictionary<string, object?>
        {
            ["quick"] = true, ["fullName"] = "Meera Iyer (synthetic)", ["dateOfBirth"] = "1980-01-01", ["sexAtBirth"] = "Female",
            ["mobile"] = "+91 90000 00105", ["facilityId"] = otherFacility, // ignored: the facility is the session's
        };
        var uri = new Uri("/v1/patients", UriKind.Relative);

        var refused = await desk.PostAsJsonAsync(uri, body);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("unresolved", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        body["duplicates"] = new { decision = "NotSamePerson", candidateIds = new[] { meera } };
        var saved = await desk.PostAsJsonAsync(uri, body);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var result = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Incomplete", result.GetProperty("status").GetString());
        var id = result.GetProperty("patientId").GetGuid();

        // Registered at the active facility, so this desk can open it; the decision is audited with the registration.
        Assert.Equal(HttpStatusCode.OK, (await desk.GetAsync(new Uri($"/v1/patients/{id}", UriKind.Relative))).StatusCode);
        Assert.Contains(("patient.duplicate.not_same", "registration", (Guid?)id), await AuditTrail());
    }

    [Fact]
    public async Task SearchIsRateLimitedPerUser()
    {
        using var host = Host(searchesPerMinute: 2);
        using var desk = Client(host, "receptionist");
        using var admin = Client(host, "admin");
        Assert.Equal(HttpStatusCode.OK, (await Search(desk, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Search(desk, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Search(desk, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Search(admin, new { })).StatusCode);
    }

    [Fact]
    public async Task TheDuplicateCheckIsRateLimitedPerUser()
    {
        using var host = Host(duplicateChecksPerMinute: 1);
        using var desk = Client(host, "receptionist");
        using var admin = Client(host, "admin");
        static Task<HttpResponseMessage> Check(HttpClient c) => c.PostAsJsonAsync(new Uri("/v1/patients/duplicate-check", UriKind.Relative), new { fullName = "Meera Iyer (synthetic)" });
        Assert.Equal(HttpStatusCode.OK, (await Check(desk)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Check(desk)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Check(admin)).StatusCode); // another desk is not affected
    }
}

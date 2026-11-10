using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Infrastructure;
using Medvya.Patients.Application;
using Medvya.Patients.Infrastructure;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Medvya.IntegrationTests.PatientRulesTests;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-019 against real SQL Server and over HTTP: no booking or sending until the practice owner accepts the current terms, one
/// append-only record of who/when/version, and the staff consent screen's reads. Synthetic data only.
/// </summary>
public sealed class TermsAcceptanceTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
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
            foreach (var (name, role) in new[] { ("receptionist", Role.Receptionist), ("admin", Role.PracticeAdmin), ("it", Role.HospitalIt), ("solo", Role.SoloDoctor) })
            {
                users[name] = Guid.NewGuid();
                db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = users[name], FacilityId = facility, Role = role, Status = MembershipStatus.Active });
            }
            await db.SaveChangesAsync();
        }
        meera = await Register(facility, "Meera Iyer (synthetic)", "+91 90000 00201");
        elsewhere = await Register(otherFacility, "Rahul Menon (synthetic)", "+91 90000 00202");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    sealed class SignedInUser(Guid id) : Medvya.BuildingBlocks.Domain.ICurrentUser { public Guid? UserId => id; }

    async Task<Guid> Register(Guid at, string name, string mobile)
    {
        var registry = new PatientRegistry(sql.Patients(tenant), new PostgresFixture.FixedTenant(tenant), new PatientRegistryTests.FixedFacility(at),
            new SignedInUser(Guid.NewGuid()), new AuditLog(sql.Audit(tenant)), TimeProvider.System, new PatientRegistryTests.FacilityZone("Asia/Kolkata"));
        return (await registry.RegisterAsync(IndiaBase with { FacilityId = at, FullName = name, Mobile = mobile })).PatientId;
    }

    WebApplicationFactory<Program> Host(bool requireTerms = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Terms:RequireAcceptance", requireTerms ? "true" : "false")
            .ConfigureTestServices(s => PatientEndpointTests.SignInFromHeaders(s, tenant)));

    HttpClient Client(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", users[user].ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", facility.ToString());
        return client;
    }

    static Uri Url(string path) => new(path, UriKind.Relative);

    static async Task<string?> Code(HttpResponseMessage r) =>
        r.StatusCode == HttpStatusCode.Forbidden && JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;

    Task<HttpResponseMessage> WalkIn(HttpClient c) => c.PostAsJsonAsync(Url("/v1/appointments/walk-in"), new { facilityId = facility, patientId = meera, reasonCategory = "ChronicReview" });

    // SAFETY: mutation check: removing .RequireAcceptedTerms() from the walk-in, booking, hold or reschedule endpoint makes this fail (that call is
    // no longer refused); moving the gate back outside the audit filter (FilterFactories.Insert(0)) leaves the refusals unaudited.
    [Fact]
    public async Task BookingHoldingAndReschedulingAreRefusedAndAuditedUntilTheOwnerAcceptsTheCurrentTermsThenAllowed()
    {
        using var host = Host();
        using var desk = Client(host, "receptionist");
        using var admin = Client(host, "admin");

        Assert.Equal("terms_not_accepted", await Code(await WalkIn(desk)));
        Assert.Equal("terms_not_accepted", await Code(await desk.PostAsJsonAsync(Url("/v1/appointments/"), new { holdId = Guid.NewGuid(), patientId = meera, reasonCategory = "ChronicReview" })));
        Assert.Equal("terms_not_accepted", await Code(await desk.PostAsJsonAsync(Url("/v1/appointments/series"), new { })));
        Assert.Equal("terms_not_accepted", await Code(await desk.PostAsJsonAsync(Url("/v1/slot-holds/"), new { })));
        Assert.Equal("terms_not_accepted", await Code(await desk.PostAsJsonAsync(Url($"/v1/appointments/{Guid.NewGuid()}/reschedule"), new { })));
        await using (var audit = sql.Audit(tenant))
            Assert.Equal(["appointment.book", "appointment.reschedule", "appointment.walk_in", "slot.hold"],
                (await audit.Events.Where(e => e.Outcome == Medvya.Audit.Domain.AuditOutcome.Denied && e.Detail == "terms_not_accepted" && e.ActorUserId == users["receptionist"])
                    .Select(e => e.Action).Distinct().ToListAsync()).Order());

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(Url("/v1/settings/terms/accept"), null)).StatusCode);

        Assert.Null(await Code(await WalkIn(desk))); // past the gate: whatever the booking says next, it is not the terms refusal
    }

    // SAFETY: mutation check: letting any role through in TermsAcceptances.AcceptAsync makes the IT and receptionist cases record a row.
    [Fact]
    public async Task OnlyAnOwnerAcceptsAndOneRecordKeepsVersionTimeAndWhoAndRepeatingAddsNothing()
    {
        using var host = Host();
        using var it = Client(host, "it");
        using var desk = Client(host, "receptionist");
        Assert.Equal(HttpStatusCode.Forbidden, (await it.PostAsync(Url("/v1/settings/terms/accept"), null)).StatusCode); // admin-level policy, but not the owner
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.PostAsync(Url("/v1/settings/terms/accept"), null)).StatusCode);
        await using (var none = sql.Context(tenant)) Assert.Equal(0, await none.TermsAcceptances.CountAsync());
        Assert.False((await it.GetFromJsonAsync<JsonElement>(Url("/v1/settings/terms"))).GetProperty("accepted").GetBoolean());

        using var solo = Client(host, "solo");
        var before = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.OK, (await solo.PostAsync(Url("/v1/settings/terms/accept"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await solo.PostAsync(Url("/v1/settings/terms/accept"), null)).StatusCode);

        await using var db = sql.Context(tenant);
        var row = await db.TermsAcceptances.SingleAsync();
        Assert.Equal((TermsPolicy.CurrentVersion, users["solo"]), (row.TermsVersion, row.AcceptedBy));
        Assert.InRange(row.AcceptedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        var status = await it.GetFromJsonAsync<JsonElement>(Url("/v1/settings/terms"));
        Assert.True(status.GetProperty("accepted").GetBoolean());
        await using var audit = sql.Audit(tenant);
        Assert.Equal(1, await audit.Events.CountAsync(e => e.Action == "tenant.terms.accept" && e.ActorUserId == users["solo"]));
    }

    [Fact]
    public async Task TheRecordCannotBeChangedAndAnotherTenantsAcceptanceDoesNotCount()
    {
        using (var host = Host()) using (var admin = Client(host, "admin"))
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(Url("/v1/settings/terms/accept"), null)).StatusCode);
        await using var db = sql.Context(tenant);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""UPDATE tenancy."TermsAcceptances" SET "TermsVersion" = 'x' WHERE "TenantId" = {tenant}"""));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""DELETE FROM tenancy."TermsAcceptances" WHERE "TenantId" = {tenant}"""));

        var other = Guid.NewGuid();
        await using (var o = sql.Context(other))
        {
            o.Tenants.Add(new Tenant { Id = other, Name = "Other Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            await o.SaveChangesAsync();
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Terms:RequireAcceptance"] = "true" }).Build();
        await using var otherDb = sql.Context(other);
        var terms = new TermsAcceptances(otherDb, new PostgresFixture.FixedTenant(other), new SignedInUser(Guid.NewGuid()), null!, null!, TimeProvider.System, config);
        Assert.False(await terms.IsSatisfiedAsync());
        Assert.False((await terms.StatusAsync()).Accepted);
    }

    // SAFETY: mutation check: removing the unique index (UniqueTermsAcceptance migration) lets the second row in.
    [Fact]
    public async Task OneAcceptancePerVersionEvenWhenTwoArriveAtOnce()
    {
        using (var host = Host())
        {
            var clicks = Enumerable.Range(0, 4).Select(_ => Client(host, "admin")).ToList();
            var answers = await Task.WhenAll(clicks.Select(c => c.PostAsync(Url("/v1/settings/terms/accept"), null)));
            Assert.All(answers, a => Assert.Equal(HttpStatusCode.OK, a.StatusCode));
            clicks.ForEach(c => c.Dispose());
        }
        await using var db = sql.Context(tenant);
        Assert.Equal(1, await db.TermsAcceptances.CountAsync());
        db.TermsAcceptances.Add(new TermsAcceptance { Id = Guid.NewGuid(), TenantId = tenant, TermsVersion = TermsPolicy.CurrentVersion, AcceptedAt = DateTimeOffset.UtcNow, AcceptedBy = users["admin"] });
        var e = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(e.InnerException).SqlState);
    }

    // SAFETY: mutation check: removing the startup check in Program.cs lets a Production host start with the gate off.
    [Fact]
    public void SwitchingTheGateOffOutsideDevelopmentStopsTheHostFromStarting()
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Production").UseSetting("Terms:RequireAcceptance", "false"));
        Assert.Contains("Development only", Assert.Throws<InvalidOperationException>(() => host.CreateClient()).Message);
    }

    [Fact]
    public async Task WithoutTheRequirementOrOnAnUnsetOneTheGateFailsClosedOnlyWhenSwitchedOff()
    {
        var off = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Terms:RequireAcceptance"] = "false" }).Build();
        var unset = new ConfigurationBuilder().Build();
        await using var db = sql.Context(tenant);
        Assert.True(await new TermsAcceptances(db, new PostgresFixture.FixedTenant(tenant), new SignedInUser(Guid.NewGuid()), null!, null!, TimeProvider.System, off).IsSatisfiedAsync());
        Assert.False(await new TermsAcceptances(db, new PostgresFixture.FixedTenant(tenant), new SignedInUser(Guid.NewGuid()), null!, null!, TimeProvider.System, unset).IsSatisfiedAsync());
    }

    // SAFETY: mutation check: dropping the facility condition in PatientConsents.CurrentAsync makes the other facility's patient readable (200).
    [Fact]
    public async Task TheConsentScreenReadsTheNewestDecisionPerTypeAtAnyLocationOfTheOrganisation()
    {
        using var host = Host(requireTerms: false);
        using var desk = Client(host, "receptionist");
        var grant = new { policyVersion = ConsentPolicy.CurrentVersion, decisions = new object[] { new { type = "Reminders", channel = "Sms", granted = true } } };
        var withdraw = new { policyVersion = ConsentPolicy.CurrentVersion, decisions = new object[] { new { type = "Reminders", channel = "Sms", granted = false } } };
        (await desk.PostAsJsonAsync(Url($"/v1/patients/{meera}/consents"), grant)).EnsureSuccessStatusCode();
        await Task.Delay(20); // distinct decision times: the newest receipt decides
        (await desk.PostAsJsonAsync(Url($"/v1/patients/{meera}/consents"), withdraw)).EnsureSuccessStatusCode();

        var current = await desk.GetFromJsonAsync<JsonElement>(Url($"/v1/patients/{meera}/consents"));
        var sms = current.GetProperty("decisions").EnumerateArray().Single(d => d.GetProperty("type").GetString() == "Reminders" && d.GetProperty("channel").GetString() == "Sms");
        Assert.False(sms.GetProperty("granted").GetBoolean());
        Assert.Equal(ConsentPolicy.CurrentVersion, current.GetProperty("policyVersion").GetString());
        Assert.Equal(HttpStatusCode.OK, (await desk.GetAsync(Url($"/v1/patients/{elsewhere}/consents"))).StatusCode); // one record for the organisation (MDV-P1-072)
        await using var audit = sql.Audit(tenant);
        Assert.True(await audit.Events.AnyAsync(e => e.Action == "patient.consent.read" && e.PatientId == meera));
    }
}

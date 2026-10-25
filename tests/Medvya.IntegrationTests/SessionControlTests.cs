using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Host.Api.Auth;
using Medvya.Identity.Domain;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-012: facility picker, /v1/me/context, server-side idle timeout and session revocation, with audit.</summary>
public sealed class SessionControlTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IDisposable
{
    readonly WebApplicationFactory<Program> factory = StaffSignInTests.CreateFactory(sql.ConnectionString);
    readonly Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid();
    Guid facilityA, facilityB;

    public void Dispose() => factory.Dispose();

    HttpClient Browser() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    async Task Clinics()
    {
        facilityA = await Clinic(tenantA, "Iyer Family Clinic (synthetic)", "clinic.alpha");
        facilityB = await Clinic(tenantB, "Lakeside Clinic (synthetic)", "clinic.beta");
    }

    async Task<Guid> Clinic(Guid tenant, string name, string flag)
    {
        await using var db = sql.Context(tenant);
        var facility = new Facility { Id = Guid.NewGuid(), TenantId = tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" };
        db.Tenants.Add(new Tenant { Id = tenant, Name = name, CountryCode = "IN", ResidencyRegion = "centralindia" });
        db.Facilities.Add(facility);
        db.FeatureFlags.Add(new FeatureFlag { Id = Guid.NewGuid(), TenantId = tenant, Key = flag, Enabled = true, Owner = "platform", Description = "test" });
        db.FeatureFlags.Add(new FeatureFlag { Id = Guid.NewGuid(), TenantId = tenant, Key = flag + ".off", Enabled = false, Owner = "platform", Description = "test" });
        await db.SaveChangesAsync();
        return facility.Id;
    }

    async Task<StaffUser> Account(string name)
    {
        var id = Guid.NewGuid();
        var user = new StaffUser { Id = id, Issuer = IdentityProviders.Development, Subject = id.ToString(), DisplayName = $"{name} (synthetic)" };
        await using var db = sql.Identity();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    async Task<Guid> Member(StaffUser user, Guid tenant, Guid facility, Role role, MembershipStatus status = MembershipStatus.Active)
    {
        await using var db = sql.Context(tenant);
        var m = new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = user.Id, FacilityId = facility, Role = role, Status = status };
        db.Memberships.Add(m);
        await db.SaveChangesAsync();
        return m.Id;
    }

    async Task<HttpClient> SignedIn(StaffUser user)
    {
        var browser = Browser();
        await StaffSignInTests.SignIn(browser, user.Subject);
        return browser;
    }

    static Task<HttpResponseMessage> Send(HttpClient browser, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add(StaffAuthentication.CsrfHeader, "1");
        return browser.SendAsync(request);
    }

    static Task<HttpResponseMessage> Choose(HttpClient browser, Guid membershipId) => Send(browser, HttpMethod.Post, "/v1/me/facility", new { membershipId });

    static async Task<JsonElement> Context(HttpClient browser)
    {
        var res = await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    static async Task<HttpStatusCode> ContextStatus(HttpClient browser) => (await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative))).StatusCode;

    async Task<List<string>> AuditActions(Guid tenant, Guid actor)
    {
        await using var db = sql.Audit(tenant);
        return await db.Events.Where(e => e.ActorUserId == actor).OrderBy(e => e.OccurredAt).Select(e => e.Action).ToListAsync();
    }

    async Task<StaffSession> SessionOf(Guid userId)
    {
        await using var db = sql.Identity();
        return await db.Sessions.AsNoTracking().OrderByDescending(s => s.CreatedAt).FirstAsync(s => s.UserId == userId);
    }

    async Task Age(Guid sessionId, TimeSpan lastSeen, TimeSpan? created = null)
    {
        await using var db = sql.Identity();
        var now = DateTimeOffset.UtcNow;
        await db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeenAt, now - lastSeen));
        if (created is { } age) await db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.CreatedAt, now - age));
    }

    [Fact]
    public async Task TwoMembershipsMeanChoosingAFacilityAndSwitchingWithoutSigningInAgain()
    {
        await Clinics();
        var user = await Account("A. Joseph");
        var atA = await Member(user, tenantA, facilityA, Role.Receptionist);
        var atB = await Member(user, tenantB, facilityB, Role.PracticeAdmin);
        using var browser = await SignedIn(user);

        var before = await Context(browser);
        Assert.Equal(JsonValueKind.Null, before.GetProperty("active").ValueKind);
        var memberships = before.GetProperty("memberships").EnumerateArray().ToList();
        Assert.Equal(2, memberships.Count);
        var a = memberships.Single(m => m.GetProperty("id").GetGuid() == atA);
        Assert.Equal("Iyer Family Clinic (synthetic)", a.GetProperty("tenantName").GetString());
        Assert.Equal("Adyar (synthetic)", a.GetProperty("facilityName").GetString());
        Assert.Equal(tenantA, a.GetProperty("tenantId").GetGuid());
        Assert.Equal(facilityA, a.GetProperty("facilityId").GetGuid());
        Assert.Equal("Receptionist", a.GetProperty("role").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await Choose(browser, atA)).StatusCode);
        var inA = (await Context(browser)).GetProperty("active");
        Assert.Equal(atA, inA.GetProperty("membershipId").GetGuid());
        Assert.Equal(tenantA, inA.GetProperty("tenantId").GetGuid());
        Assert.Equal(facilityA, inA.GetProperty("facilityId").GetGuid());
        Assert.Equal("Receptionist", inA.GetProperty("role").GetString());
        Assert.Equal(["clinic.alpha"], inA.GetProperty("flags").EnumerateArray().Select(f => f.GetString()));

        // Switch: no provider round trip, just the facility endpoint on the same session.
        Assert.Equal(HttpStatusCode.NoContent, (await Choose(browser, atB)).StatusCode);
        var inB = (await Context(browser)).GetProperty("active");
        Assert.Equal(tenantB, inB.GetProperty("tenantId").GetGuid());
        Assert.Equal("PracticeAdmin", inB.GetProperty("role").GetString());
        Assert.Equal(["clinic.beta"], inB.GetProperty("flags").EnumerateArray().Select(f => f.GetString()));

        // Audited in the tenant the user entered; each tenant sees only its own rows.
        Assert.Equal(["auth.signin"], await AuditActions(tenantA, user.Id));
        Assert.Equal(["auth.facility.switch"], await AuditActions(tenantB, user.Id));
        var session = await SessionOf(user.Id);
        Assert.Equal((tenantB, atB, 15), (session.TenantId, session.MembershipId, session.IdleTimeoutMinutes));
    }

    [Fact]
    public async Task ContextJsonUsesTheAgreedShapeWithEnumNamesAsStrings()
    {
        await Clinics();
        var user = await Account("A. Joseph");
        using var browser = await SignedIn(user);
        await Choose(browser, await Member(user, tenantA, facilityA, Role.Receptionist));

        // The app shell (MDV-P0-022) reads these exact names; numbers or camelCase enum values would break it.
        var json = await browser.GetStringAsync(new Uri("/v1/me/context", UriKind.Relative));
        foreach (var fragment in new[] { "\"user\":{\"id\":", "\"displayName\":", "\"memberships\":[{\"id\":", "\"tenantName\":", "\"facilityName\":",
            "\"role\":\"Receptionist\"", "\"active\":{\"membershipId\":", "\"country\":\"IN\"", "\"capabilities\":{\"RegisterBookPatients\":\"Full\"", "\"CheckInQueue\":\"Full\"",
            "\"flags\":[\"clinic.alpha\"]" })
            Assert.Contains(fragment, json, StringComparison.Ordinal);
        Assert.DoesNotContain("ClinicalNotes", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Role.Receptionist)]
    [InlineData(Role.Nurse)]
    [InlineData(Role.PracticeAdmin)]
    [InlineData(Role.Patient)]
    public async Task ContextNeverReturnsCapabilitiesTheRoleDoesNotGrant(Role role)
    {
        await Clinics();
        var user = await Account("Priya N.");
        var membership = await Member(user, tenantA, facilityA, role);
        using var browser = await SignedIn(user);
        await Choose(browser, membership);

        var capabilities = (await Context(browser)).GetProperty("active").GetProperty("capabilities")
            .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        var expected = Enum.GetValues<Capability>().Where(c => RoleMatrix.Of(role, c) != Access.None)
            .ToDictionary(c => c.ToString(), c => (string?)RoleMatrix.Of(role, c).ToString());
        Assert.Equal(expected, capabilities);
        Assert.DoesNotContain(nameof(Access.None), capabilities.Values);
    }

    [Fact]
    public async Task OnlyOwnActiveMembershipsCanBeChosen()
    {
        await Clinics();
        var user = await Account("S. Thomas RN");
        var colleague = await Account("Dr. S. Menon");
        var suspended = await Member(user, tenantB, facilityB, Role.Nurse, MembershipStatus.Suspended);
        var someoneElses = await Member(colleague, tenantA, facilityA, Role.HospitalDoctor);
        using var browser = await SignedIn(user);

        Assert.Equal(HttpStatusCode.Forbidden, (await Choose(browser, suspended)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Choose(browser, someoneElses)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Choose(browser, Guid.NewGuid())).StatusCode);
        var context = await Context(browser);
        Assert.Empty(context.GetProperty("memberships").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, context.GetProperty("active").ValueKind);
    }

    [Fact]
    public async Task IdleTimeoutEndsTheSessionAndBackgroundPollsDoNotKeepItAlive()
    {
        await Clinics();
        var user = await Account("K. Lakshmi");
        using var browser = await SignedIn(user);
        await Choose(browser, await Member(user, tenantA, facilityA, Role.PracticeAdmin));
        var session = await SessionOf(user.Id);

        // A passive request (the client's status poll) leaves last-seen alone; a real one moves it.
        await Age(session.Id, TimeSpan.FromMinutes(5));
        using (var poll = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/me/context", UriKind.Relative)))
        {
            poll.Headers.Add(SessionControl.PassiveHeader, "1");
            Assert.Equal(HttpStatusCode.OK, (await browser.SendAsync(poll)).StatusCode);
        }
        Assert.True(DateTimeOffset.UtcNow - (await SessionOf(user.Id)).LastSeenAt > TimeSpan.FromMinutes(4));
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(browser));
        Assert.True(DateTimeOffset.UtcNow - (await SessionOf(user.Id)).LastSeenAt < TimeSpan.FromMinutes(1));

        await Age(session.Id, TimeSpan.FromMinutes(StaffSession.DefaultIdleTimeoutMinutes));
        Assert.Equal(HttpStatusCode.Unauthorized, await ContextStatus(browser));
    }

    [Fact]
    public async Task TenantIdleTimeoutAppliesOnceAFacilityIsChosen()
    {
        await Clinics();
        await using (var db = sql.Context(tenantA))
        {
            (await db.Tenants.SingleAsync()).IdleTimeoutMinutes = 60;
            await db.SaveChangesAsync();
        }
        var user = await Account("R. Varghese");
        using var browser = await SignedIn(user);
        await Choose(browser, await Member(user, tenantA, facilityA, Role.HospitalIt));
        var session = await SessionOf(user.Id);
        Assert.Equal(60, session.IdleTimeoutMinutes);

        await Age(session.Id, TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(browser));
        await Age(session.Id, TimeSpan.Zero, created: StaffSession.MaxLifetime); // the 8 h cap holds whatever the activity
        Assert.Equal(HttpStatusCode.Unauthorized, await ContextStatus(browser));
    }

    [Fact]
    public async Task RevokingAnotherOfMySessionsEndsItOnItsNextRequest()
    {
        await Clinics();
        var user = await Account("Dr. Arjun Rao");
        var membership = await Member(user, tenantA, facilityA, Role.SoloDoctor);
        using var desk = await SignedIn(user);
        await Choose(desk, membership);
        var deskSession = await SessionOf(user.Id);
        using var phone = await SignedIn(user);
        await Choose(phone, membership);

        var listed = await (await phone.GetAsync(new Uri("/v1/me/sessions", UriKind.Relative))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, listed.GetArrayLength());
        Assert.False(listed.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == deskSession.Id).GetProperty("current").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await Send(phone, HttpMethod.Delete, $"/v1/me/sessions/{deskSession.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await ContextStatus(desk));
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(phone));
        Assert.Contains("session.revoke", await AuditActions(tenantA, user.Id));
    }

    [Fact]
    public async Task NobodyCanRevokeSomeoneElsesSessionAsTheirOwn()
    {
        await Clinics();
        var victim = await Account("A. Joseph");
        using var victimBrowser = await SignedIn(victim);
        var victimSession = await SessionOf(victim.Id);
        var other = await Account("Priya N.");
        using var browser = await SignedIn(other);
        await Choose(browser, await Member(other, tenantA, facilityA, Role.CareCoordinator));

        Assert.Equal(HttpStatusCode.NotFound, (await Send(browser, HttpMethod.Delete, $"/v1/me/sessions/{victimSession.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(victimBrowser));
    }

    [Fact]
    public async Task TenantAdminEndsAStaffMembersSessionsInThatTenantOnly()
    {
        await Clinics();
        var admin = await Account("K. Lakshmi");
        var staff = await Account("A. Joseph");
        var adminMembership = await Member(admin, tenantA, facilityA, Role.PracticeAdmin);
        var staffAtA = await Member(staff, tenantA, facilityA, Role.Receptionist);
        var staffAtB = await Member(staff, tenantB, facilityB, Role.Receptionist);
        using var staffInA = await SignedIn(staff);
        await Choose(staffInA, staffAtA);
        using var staffInB = await SignedIn(staff);
        await Choose(staffInB, staffAtB);

        // A receptionist is not a tenant admin.
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(staffInA, HttpMethod.Delete, $"/v1/staff/{admin.Id}/sessions")).StatusCode);
        using var adminBrowser = await SignedIn(admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(adminBrowser, HttpMethod.Delete, $"/v1/staff/{staff.Id}/sessions")).StatusCode); // no facility chosen yet
        await Choose(adminBrowser, adminMembership);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(adminBrowser, HttpMethod.Delete, $"/v1/staff/{staff.Id}/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await ContextStatus(staffInA));
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(staffInB)); // the other clinic's session is not this admin's to end
        Assert.Contains("session.revoke", await AuditActions(tenantA, admin.Id));
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(adminBrowser));
    }

    [Fact]
    public async Task ACookieCopiedBeforeASwitchOrSignOutNoLongerWorks()
    {
        await Clinics();
        var user = await Account("S. Thomas RN");
        var atA = await Member(user, tenantA, facilityA, Role.Nurse);
        var atB = await Member(user, tenantB, facilityB, Role.Nurse);
        using var browser = Browser();
        await StaffSignInTests.SignIn(browser, user.Subject);
        var chosen = await Choose(browser, atA);
        var copied = Assert.Single(chosen.Headers.GetValues("Set-Cookie"), c => c.StartsWith(StaffAuthentication.CookieName + "=", StringComparison.Ordinal)).Split(';')[0];

        using var thief = Browser();
        thief.DefaultRequestHeaders.Add("Cookie", copied);
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(thief));

        await Choose(browser, atB); // the copy still names tenant A
        Assert.Equal(HttpStatusCode.Unauthorized, await ContextStatus(thief));

        Assert.Equal(HttpStatusCode.NoContent, (await Send(browser, HttpMethod.Post, "/bff/logout")).StatusCode);
        Assert.Equal(["auth.signin"], await AuditActions(tenantA, user.Id));
        Assert.Equal(["auth.facility.switch", "auth.signout"], await AuditActions(tenantB, user.Id));
        Assert.NotNull((await SessionOf(user.Id)).RevokedAt);
    }
}

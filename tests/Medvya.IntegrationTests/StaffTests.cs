using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-008 on real SQL Server: Settings → Staff. Non-admins get 403; doctors are redirected and create nothing; adding staff
/// uses the P0 invitation flow (resend and revoke included); custom roles never exceed practice admin; every role change keeps
/// its before/after and an audit event; a receptionist added here still cannot reach clinical capabilities.
/// </summary>
public sealed class StaffTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    readonly Outbox outbox = new();
    WebApplicationFactory<Program>? factory;

    public Task InitializeAsync() => h.SeedAsync();

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
                        [new Claim(ClaimTypes.NameIdentifier, user), new Claim("tenant_id", tenant.ToString()), new Claim("facility_id", facility.ToString()), new Claim("role", ctx.Request.Headers["X-Test-Claim-Role"].ToString())], "synthetic"));
                return nxt(ctx);
            });
            next(app);
        };
    }

    sealed class Outbox : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) { lock (Sent) Sent.Add(message); return Task.CompletedTask; }
    }

    HttpClient Client(Role role, Guid? user = null)
    {
        factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Invitations:LinkBaseUrl", "https://clinic.test")
            .ConfigureServices(s => s.AddSingleton<IStartupFilter>(new SignedIn(h.Tenant, h.Facility)))
            .ConfigureTestServices(s => s.AddSingleton<IEmailSender>(outbox)));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", (user ?? h.ByRole[role]).ToString());
        return client;
    }

    HttpClient Admin() => Client(Role.PracticeAdmin);

    HttpRequestMessage[] Requests() =>
    [
        new(HttpMethod.Get, "/v1/staff"),
        new(HttpMethod.Get, "/v1/staff/roles"),
        new(HttpMethod.Get, "/v1/staff/access-review"), // MDV-P1-010
        new(HttpMethod.Post, $"/v1/staff/access-review/{Guid.NewGuid()}/confirm"),
        new(HttpMethod.Post, "/v1/staff") { Content = JsonContent.Create(new { email = "new@example.test", role = "Receptionist", facilityId = h.Facility }) },
        new(HttpMethod.Post, $"/v1/staff/invitations/{Guid.NewGuid()}/resend"),
        new(HttpMethod.Post, "/v1/staff/roles") { Content = JsonContent.Create(new { name = "Front desk plus", baseRole = "Receptionist", capabilities = Array.Empty<string>() }) },
        new(HttpMethod.Put, $"/v1/staff/roles/{Guid.NewGuid()}") { Content = JsonContent.Create(new { name = "x", baseRole = "Receptionist", capabilities = Array.Empty<string>() }) },
    ];

    [Theory]
    [InlineData(Role.Receptionist)]
    [InlineData(Role.Nurse)]
    [InlineData(Role.CareCoordinator)]
    [InlineData(Role.HospitalDoctor)]
    [InlineData(Role.Patient)]
    public async Task RolesWithoutStaffAdministrationGet403AndNothingChanges(Role role)
    {
        using var client = Client(role);
        foreach (var request in Requests())
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
        await using var db = sql.Context(h.Tenant);
        Assert.Empty(await db.CustomRoles.ToListAsync());
        Assert.Empty(await db.Invitations.ToListAsync());
        Assert.Empty(outbox.Sent);
    }

    [Theory]
    [InlineData("SoloDoctor")]
    [InlineData("HospitalDoctor")]
    public async Task ChoosingDoctorIsRedirectedAndCreatesNoMembership(string role)
    {
        using var client = Admin();
        await using var before = sql.Context(h.Tenant);
        var count = await before.Memberships.CountAsync();
        var response = await client.PostAsJsonAsync("/v1/staff", new { email = "dr@example.test", role, facilityId = h.Facility });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("doctor_redirect", await response.Content.ReadAsStringAsync());
        Assert.Equal(count, await before.Memberships.CountAsync());
        Assert.Empty(outbox.Sent);
    }

    [Fact]
    public async Task AddingStaffInvitesThroughTheP0FlowAndTheListShowsStateResendAndRevoke()
    {
        using var client = Admin();
        var created = await client.PostAsJsonAsync("/v1/staff", new { email = "joseph@example.test", role = "Receptionist", facilityId = h.Facility });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var first = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Single(outbox.Sent);

        var invited = Row(await client.GetFromJsonAsync<JsonElement>("/v1/staff"), r => r.GetProperty("email").GetString() == "joseph@example.test");
        Assert.Equal(("Invited", "pending", "pending", "Receptionist"),
            (invited.GetProperty("status").GetString(), invited.GetProperty("mfa").GetString(), invited.GetProperty("invitationState").GetString(), invited.GetProperty("role").GetString()));

        var resent = await client.PostAsync($"/v1/staff/invitations/{first}/resend", null);
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        var second = (await resent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(2, outbox.Sent.Count);
        // Only the newest link lives; the old one cannot be resent again.
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/v1/staff/invitations/{first}/resend", null)).StatusCode);
        await using (var db = sql.Context(h.Tenant))
        {
            Assert.NotNull((await db.Invitations.SingleAsync(i => i.Id == first)).RevokedAt);
            Assert.Null((await db.Invitations.SingleAsync(i => i.Id == second)).RevokedAt);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/v1/invitations/{second}")).StatusCode);
        var revoked = Row(await client.GetFromJsonAsync<JsonElement>("/v1/staff"), r => r.GetProperty("email").GetString() == "joseph@example.test");
        Assert.Equal(("Invited", "revoked"), (revoked.GetProperty("status").GetString(), revoked.GetProperty("invitationState").GetString()));

        // The seeded portal user (a patient) is not staff.
        Assert.DoesNotContain((await client.GetFromJsonAsync<JsonElement>("/v1/staff")).EnumerateArray(), r => r.GetProperty("role").GetString() == "Patient");

        var active = Row(await client.GetFromJsonAsync<JsonElement>("/v1/staff"), r => r.GetProperty("role").GetString() == "Receptionist" && r.GetProperty("status").GetString() == "Active");
        Assert.Equal(("Active", "enrolled"), (active.GetProperty("status").GetString(), active.GetProperty("mfa").GetString()));
    }

    static JsonElement Row(JsonElement list, Func<JsonElement, bool> match) => list.EnumerateArray().Single(match);

    // ---- Custom roles ----

    static object Role_(string name, string baseRole, params string[] capabilities) => new { name, baseRole, capabilities };

    [Theory]
    [InlineData("Nurse", "ClinicalNotes")] // practice admin has no clinical access
    [InlineData("Receptionist", "Patient360Chart")]
    [InlineData("Receptionist", "StaffRolesIntegrations")] // would let the role create more privileged roles
    [InlineData("Nurse", "CheckInQueue")] // the base role already holds it (locked row)
    [InlineData("Nurse", "PrescriptionsOrders")]
    public async Task ACustomRoleCannotExceedPracticeAdminOrChangeLockedRows(string baseRole, string capability)
    {
        using var client = Admin();
        var response = await client.PostAsJsonAsync("/v1/staff/roles", Role_("Too much (synthetic)", baseRole, capability));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("exceeds_admin", await response.Content.ReadAsStringAsync());
        await using var db = sql.Context(h.Tenant);
        Assert.Empty(await db.CustomRoles.ToListAsync());
        Assert.Empty(await db.SettingsChanges.ToListAsync());
    }

    [Fact]
    public async Task DoctorsAndHospitalItCannotBeTheBaseOfACustomRole()
    {
        using var client = Admin();
        foreach (var baseRole in new[] { "SoloDoctor", "HospitalDoctor", "HospitalIt", "Patient" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/staff/roles", Role_("Nope (synthetic)", baseRole))).StatusCode);
    }

    [Fact]
    public async Task RoleChangesKeepBeforeAndAfterAndAreAudited()
    {
        using var client = Admin();
        var created = await client.PostAsJsonAsync("/v1/staff/roles", Role_("Billing nurse (synthetic)", "Nurse", "InvoicesPayments"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/v1/staff/roles/{id}", Role_("Billing nurse (synthetic)", "Nurse", "InvoicesPayments", "AuditLog"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/v1/staff/roles/{id}", Role_("Billing nurse (synthetic)", "Nurse", "ClinicalNotes"))).StatusCode);

        await using var db = sql.Context(h.Tenant);
        var changes = await db.SettingsChanges.OrderBy(c => c.OccurredAt).ToListAsync();
        Assert.Equal(2, changes.Count); // the rejected edit left nothing behind
        Assert.Null(changes[0].Before);
        Assert.DoesNotContain("AuditLog", changes[1].Before);
        Assert.Contains("AuditLog", changes[1].After);
        Assert.All(changes, c => Assert.Equal((h.Admin, "staff.role"), (c.ActorUserId, c.Section)));
        await using var audit = sql.Audit(h.Tenant);
        Assert.Equal(["staff.role.create", "staff.role.update"], (await audit.Events.OrderBy(e => e.OccurredAt).ToListAsync()).Select(e => e.Action));
        Assert.Equal(["InvoicesPayments,AuditLog"], (await db.CustomRoles.ToListAsync()).Select(r => r.Capabilities));
    }

    [Fact]
    public async Task DuplicateRoleNameIsRefused()
    {
        using var client = Admin();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/v1/staff/roles", Role_("Desk lead (synthetic)", "Receptionist"))).StatusCode);
        var again = await client.PostAsJsonAsync("/v1/staff/roles", Role_("Desk lead (synthetic)", "Nurse"));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("duplicate", await again.Content.ReadAsStringAsync());
    }

    // ---- A receptionist added here ----

    sealed class Who(Guid id) : ICurrentUser { public Guid? UserId => id; }
    sealed class At(Guid id) : ICurrentFacility { public Guid? FacilityId => id; }

    /// <summary>Adds a person through the endpoint, lets them accept (the P0 flow's end state) and returns their access to a capability.</summary>
    async Task<Func<Capability, Task<Access>>> JoinedAs(string role, Guid? customRoleId = null)
    {
        using var client = Admin();
        var response = await client.PostAsJsonAsync("/v1/staff", new { email = $"{Guid.NewGuid():N}@example.test", role, customRoleId, facilityId = h.Facility });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = Guid.NewGuid();
        // The P0 tests cover accepting the link; here the invited membership simply becomes the user's active one.
        await using (var db = sql.Context(h.Tenant))
        {
            var invitationId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var membershipId = await db.Invitations.Where(i => i.Id == invitationId).Select(i => i.MembershipId).SingleAsync();
            await db.Memberships.Where(m => m.Id == membershipId).ExecuteUpdateAsync(m => m.SetProperty(x => x.UserId, user).SetProperty(x => x.Status, MembershipStatus.Active));
        }
        return async capability =>
        {
            await using var db = sql.Context(h.Tenant);
            return await new StaffAccess(db, new FacilityDirectory(db), new Who(user), new At(h.Facility)).AccessAsync(capability);
        };
    }

    [Fact]
    public async Task AReceptionistAddedHereCannotReachClinicalCapabilities()
    {
        var access = await JoinedAs("Receptionist");
        Assert.Equal(Access.Full, await access(Capability.RegisterBookPatients));
        foreach (var clinical in new[] { Capability.Patient360Chart, Capability.ClinicalNotes, Capability.PrescriptionsOrders, Capability.AiDraftsEvidence, Capability.ResultReviewAcknowledge, Capability.StaffRolesIntegrations })
            Assert.Equal(Access.None, await access(clinical));
    }

    [Fact]
    public async Task ACustomRoleGrantsItsExtraAtPracticeAdminLevelAndNothingClinical()
    {
        using var client = Admin();
        var role = await client.PostAsJsonAsync("/v1/staff/roles", Role_("Desk with audit (synthetic)", "Receptionist", "AuditLog"));
        var id = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var access = await JoinedAs("Receptionist", id);
        Assert.Equal(Access.Read, await access(Capability.AuditLog)); // practice admin's level
        Assert.Equal(Access.None, await access(Capability.DevicePairingFleet)); // not chosen
        Assert.Equal(Access.None, await access(Capability.ClinicalNotes));
    }

    [Fact]
    public async Task AForgedOrStaleRoleClaimGrantsNothingBecauseTheMembershipDecides()
    {
        // The reception membership is a receptionist; a cookie that still says PracticeAdmin must not open staff administration.
        using var client = Client(Role.Receptionist);
        client.DefaultRequestHeaders.Add("X-Test-Claim-Role", nameof(Role.PracticeAdmin));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/v1/staff")).StatusCode);
    }

    [Fact]
    public async Task AStoredExtraBeyondThePracticeAdminCeilingIsIgnored()
    {
        // Defence in depth: even a row written around the API (here directly) cannot grant clinical access.
        var role = new CustomRole { Id = Guid.NewGuid(), TenantId = h.Tenant, Name = "Tampered (synthetic)", BaseRole = Role.Receptionist, Capabilities = "ClinicalNotes", CreatedAt = DateTimeOffset.UtcNow };
        await using (var db = sql.Context(h.Tenant)) { db.CustomRoles.Add(role); await db.SaveChangesAsync(); }
        var access = await JoinedAs("Receptionist", role.Id);
        Assert.Equal(Access.None, await access(Capability.ClinicalNotes));
    }
}

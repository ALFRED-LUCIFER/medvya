using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Host.Api.Auth;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Domain;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-013: invite a colleague by email; the link is single-use, expires, can be revoked, and joining activates the membership once.</summary>
public sealed partial class InvitationTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime, IDisposable
{
    readonly Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid(), admin = Guid.NewGuid();
    Guid facilityA, facilityB;
    readonly Outbox outbox = new();
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        facilityA = await Clinic(tenantA, "Iyer Family Clinic (synthetic)", "Adyar (synthetic)");
        facilityB = await Clinic(tenantB, "Al Noor Family Clinic (synthetic)", "Abu Dhabi (synthetic)");
        await using (var identity = sql.Identity())
        {
            identity.Users.Add(new StaffUser { Id = admin, Issuer = IdentityProviders.Development, Subject = admin.ToString(), DisplayName = "K. Lakshmi (synthetic)" });
            await identity.SaveChangesAsync();
        }
        factory = CreateFactory();
    }

    public Task DisposeAsync() => Task.CompletedTask;
    public void Dispose() => factory.Dispose();

    async Task<Guid> Clinic(Guid tenant, string name, string facility)
    {
        await using var db = sql.Context(tenant);
        var site = new Facility { Id = Guid.NewGuid(), TenantId = tenant, Name = facility, TimeZone = "Asia/Kolkata" };
        db.Tenants.Add(new Tenant { Id = tenant, Name = name, CountryCode = "IN", ResidencyRegion = "centralindia" });
        db.Facilities.Add(site);
        db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = admin, FacilityId = site.Id, Role = Role.PracticeAdmin, Status = MembershipStatus.Active });
        await db.SaveChangesAsync();
        return site.Id;
    }

    WebApplicationFactory<Program> CreateFactory()
    {
        WebApplicationFactory<Program> f = null!;
        f = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Authentication:Oidc:Authority", "http://localhost/dev-idp")
            .UseSetting("Invitations:LinkBaseUrl", "https://clinic.test")
            .ConfigureTestServices(s =>
            {
                s.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, o => o.BackchannelHttpHandler = f.Server.CreateHandler());
                s.AddSingleton<IEmailSender>(outbox);
                // Until the facility picker (MDV-P0-012) puts tenant_id and role in the session, staff principals come from test headers.
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, HeaderAuth>(HeaderAuth.Name, null)
                    .AddPolicyScheme("test-or-cookie", null, o => o.ForwardDefaultSelector = ctx =>
                        ctx.Request.Headers.ContainsKey(HeaderAuth.User) ? HeaderAuth.Name : CookieAuthenticationDefaults.AuthenticationScheme);
                s.Configure<AuthenticationOptions>(o => o.DefaultScheme = "test-or-cookie");
            }));
        return f;
    }

    HttpClient Browser() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    HttpClient Staff(Guid tenant, Role role)
    {
        var client = Browser();
        client.DefaultRequestHeaders.Add(HeaderAuth.User, admin.ToString());
        client.DefaultRequestHeaders.Add(StaffPolicies.TenantClaim, tenant.ToString());
        client.DefaultRequestHeaders.Add(StaffPolicies.RoleClaim, role.ToString());
        return client;
    }

    static Task<HttpResponseMessage> Invite(HttpClient client, Guid facility, string role = "Receptionist", string email = "joseph@example.test") =>
        client.PostAsJsonAsync(new Uri("/v1/invitations", UriKind.Relative), new { email, role, facilityId = facility });

    /// <summary>Invites as tenant A's admin and returns the invitation id and the token from the emailed link.</summary>
    async Task<(Guid Id, string Token)> InviteReceptionist(string role = "Receptionist")
    {
        using var client = Staff(tenantA, Role.PracticeAdmin);
        var response = await Invite(client, facilityA, role);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return (id, LinkToken().Match(outbox.Sent[^1].Body).Groups[1].Value);
    }

    [GeneratedRegex(@"https://clinic\.test/invite/([A-Za-z0-9_-]+)")]
    private static partial Regex LinkToken();

    static Task<HttpResponseMessage> Preview(HttpClient client, string token) =>
        client.PostAsJsonAsync(new Uri("/v1/invitations/preview", UriKind.Relative), new { token });

    /// <summary>Join: login with the invitation → dev provider → callback. Returns the callback response.</summary>
    static async Task<HttpResponseMessage> Join(HttpClient browser, string token, string loginHint = "new")
    {
        var challenge = await browser.GetAsync(new Uri($"/bff/login?returnUrl=%2F&invitation={token}", UriKind.Relative));
        var authorize = await browser.GetAsync(new Uri($"{challenge.Headers.Location}&login_hint={loginHint}"));
        return await browser.GetAsync(authorize.Headers.Location);
    }

    async Task<int> UserCount() { await using var db = sql.Identity(); return await db.Users.CountAsync(); }

    [Fact]
    public async Task AdminInvitesAndANewPersonJoinsOnce()
    {
        var (id, token) = await InviteReceptionist();

        var mail = Assert.Single(outbox.Sent);
        Assert.Equal("joseph@example.test", mail.To);
        Assert.Contains("Iyer Family Clinic (synthetic)", mail.Subject, StringComparison.Ordinal);
        await using (var db = sql.Context(tenantA))
        {
            var invitation = await db.Invitations.SingleAsync(i => i.Id == id);
            Assert.Equal(InvitationToken.Read(token)!.Value.Hash, invitation.TokenHash); // the hash is stored, not the token
            Assert.InRange(invitation.ExpiresAt - invitation.CreatedAt, TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1), TimeSpan.FromDays(7));
            var membership = await db.Memberships.SingleAsync(m => m.Id == invitation.MembershipId);
            Assert.Equal((MembershipStatus.Invited, (Guid?)null, Role.Receptionist, facilityA), (membership.Status, membership.UserId, membership.Role, membership.FacilityId));
        }

        using var browser = Browser();
        var preview = await Preview(browser, token);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var body = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("Iyer Family Clinic (synthetic)", "Adyar (synthetic)", "K. Lakshmi (synthetic)", "Receptionist"),
            (body.GetProperty("clinic").GetString(), body.GetProperty("facility").GetString(), body.GetProperty("inviter").GetString(), body.GetProperty("role").GetString()));
        Assert.Equal(RoleMatrix.Rows.Select(r => $"{r.Capability}:{RoleMatrix.Of(Role.Receptionist, r.Capability)}"),
            body.GetProperty("capabilities").EnumerateArray().Select(c => $"{c.GetProperty("capability").GetString()}:{c.GetProperty("access").GetString()}"));
        Assert.False(body.GetProperty("licenceVerificationRequired").GetBoolean());
        Assert.DoesNotContain("joseph@example.test", body.GetRawText(), StringComparison.Ordinal);

        var users = await UserCount();
        var callback = await Join(browser, token);
        Assert.Equal("/", callback.Headers.Location!.OriginalString);
        var me = await (await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative))).Content.ReadFromJsonAsync<JsonElement>();
        var userId = me.GetProperty("user").GetProperty("id").GetGuid();
        Assert.StartsWith("New colleague", me.GetProperty("user").GetProperty("displayName").GetString(), StringComparison.Ordinal);
        Assert.Equal(users + 1, await UserCount());

        await using (var db = sql.Context(tenantA))
        {
            var membershipId = (await db.Invitations.SingleAsync(i => i.Id == id)).MembershipId;
            var membership = await db.Memberships.SingleAsync(m => m.Id == membershipId);
            Assert.Equal((MembershipStatus.Active, (Guid?)userId), (membership.Status, membership.UserId));
        }
        await using (var audit = sql.Audit(tenantA))
        {
            var events = await audit.Events.Where(e => e.ResourceId == id).OrderBy(e => e.OccurredAt).Select(e => new { e.Action, e.ActorUserId }).ToListAsync();
            Assert.Equal([("invitation.create", (Guid?)admin), ("invitation.accept", userId)], events.Select(e => (e.Action, e.ActorUserId)));
        }

        // Used: the link now shows the expired state, and joining again creates nothing.
        using var second = Browser();
        Assert.Equal(HttpStatusCode.NotFound, (await Preview(second, token)).StatusCode);
        Assert.Equal("/?signin=failed", (await Join(second, token)).Headers.Location!.OriginalString);
        Assert.Equal(users + 1, await UserCount());
    }

    [Fact]
    public async Task AnExistingMedvyaUserJoinsAnotherClinicWithTheirOwnAccount()
    {
        using var client = Staff(tenantB, Role.PracticeAdmin);
        Assert.Equal(HttpStatusCode.Created, (await Invite(client, facilityB, "SoloDoctor")).StatusCode);
        var token = LinkToken().Match(outbox.Sent[^1].Body).Groups[1].Value;
        using var browser = Browser();
        var preview = await (await Preview(browser, token)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(preview.GetProperty("licenceVerificationRequired").GetBoolean());

        var doctor = Guid.NewGuid();
        await using (var identity = sql.Identity())
        {
            identity.Users.Add(new StaffUser { Id = doctor, Issuer = IdentityProviders.Development, Subject = doctor.ToString(), DisplayName = "Dr. Arjun Rao (synthetic)" });
            await identity.SaveChangesAsync();
        }
        var users = await UserCount();
        Assert.Equal("/", (await Join(browser, token, loginHint: doctor.ToString())).Headers.Location!.OriginalString);
        Assert.Equal(users, await UserCount());
        await using var db = sql.Context(tenantB);
        Assert.Contains(await db.Memberships.ToListAsync(), m => m.UserId == doctor && m.Role == Role.SoloDoctor && m.Status == MembershipStatus.Active);
    }

    [Fact]
    public async Task SomeoneAlreadyAtTheFacilityCannotUseAnInvitationThere()
    {
        var (_, token) = await InviteReceptionist();
        using var browser = Browser();
        Assert.Equal("/?signin=failed", (await Join(browser, token, loginHint: admin.ToString())).Headers.Location!.OriginalString);
        await using var db = sql.Context(tenantA);
        Assert.Equal(1, await db.Memberships.CountAsync(m => m.UserId == admin));
        Assert.Equal(0, await db.Invitations.CountAsync(i => i.AcceptedAt != null)); // the link stays usable by the right person
    }

    [Fact]
    public async Task ExpiredRevokedAndForgedLinksShowNothingAndCreateNothing()
    {
        var (expiredId, expired) = await InviteReceptionist();
        await using (var db = sql.Context(tenantA))
            await db.Invitations.Where(i => i.Id == expiredId).ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        var (revokedId, revoked) = await InviteReceptionist();
        using (var client = Staff(tenantA, Role.PracticeAdmin))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(new Uri($"/v1/invitations/{revokedId}", UriKind.Relative))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(new Uri($"/v1/invitations/{revokedId}", UriKind.Relative))).StatusCode);
        }
        var (_, valid) = await InviteReceptionist();
        var forged = InvitationToken.New(tenantA).Token; // well-formed, never issued

        var users = await UserCount();
        foreach (var token in new[] { expired, revoked, forged, "not-a-token" })
        {
            using var browser = Browser();
            Assert.Equal(HttpStatusCode.NotFound, (await Preview(browser, token)).StatusCode);
            Assert.Equal("/?signin=failed", (await Join(browser, token)).Headers.Location!.OriginalString);
        }
        Assert.Equal(users, await UserCount());
        using (var browser = Browser())
            Assert.Equal(HttpStatusCode.OK, (await Preview(browser, valid)).StatusCode);

        await using var check = sql.Context(tenantA);
        Assert.Equal(3, await check.Memberships.CountAsync(m => m.Status == MembershipStatus.Invited)); // nothing was activated
        await using var audit = sql.Audit(tenantA);
        Assert.Equal(1, await audit.Events.CountAsync(e => e.ResourceId == revokedId && e.Action == "invitation.revoke"));
    }

    [Fact]
    public async Task OnlyStaffAdminsOfTheTenantCanInvite()
    {
        using (var anonymous = Browser())
            Assert.Equal(HttpStatusCode.Unauthorized, (await Invite(anonymous, facilityA)).StatusCode);
        foreach (var role in new[] { Role.Receptionist, Role.Nurse, Role.HospitalDoctor, Role.CareCoordinator, Role.Patient })
        {
            using var client = Staff(tenantA, role);
            Assert.Equal(HttpStatusCode.Forbidden, (await Invite(client, facilityA)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(new Uri("/v1/invitations/facilities", UriKind.Relative))).StatusCode);
        }
        using (var noTenant = Browser())
        {
            noTenant.DefaultRequestHeaders.Add(HeaderAuth.User, admin.ToString());
            noTenant.DefaultRequestHeaders.Add(StaffPolicies.RoleClaim, nameof(Role.PracticeAdmin));
            Assert.Equal(HttpStatusCode.Forbidden, (await Invite(noTenant, facilityA)).StatusCode); // admin role but no active tenant
        }
        using var staffAdmin = Staff(tenantA, Role.PracticeAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await Invite(staffAdmin, facilityB)).StatusCode); // another tenant's facility
        Assert.Equal(HttpStatusCode.BadRequest, (await Invite(staffAdmin, facilityA, role: "Patient")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Invite(staffAdmin, facilityA, email: "Joseph <joseph@example.test>")).StatusCode);
        var facilities = await staffAdmin.GetFromJsonAsync<JsonElement>(new Uri("/v1/invitations/facilities", UriKind.Relative));
        Assert.Equal([facilityA], facilities.EnumerateArray().Select(f => f.GetProperty("id").GetGuid()));
        Assert.Empty(outbox.Sent);
    }

    [Fact]
    public async Task AnInvitationThatCannotBeEmailedIsNotCreated()
    {
        outbox.Fail = true;
        using var client = Staff(tenantA, Role.PracticeAdmin);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Invite(client, facilityA)).StatusCode);
        await using var db = sql.Context(tenantA);
        Assert.Equal(0, await db.Invitations.CountAsync());
        Assert.Equal(0, await db.Memberships.CountAsync(m => m.Status == MembershipStatus.Invited));
    }

    [Fact]
    public async Task ConcurrentAcceptancesActivateTheMembershipOnce()
    {
        var (_, token) = await InviteReceptionist();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            var tenant = new SqlServerFixture.FixedTenant(tenantA);
            var invitations = new Invitations(sql.Context(tenantA), tenant, new FixedUser(null), new AuditLog(sql.Audit(tenantA)), outbox,
                Options.Create(new InvitationOptions()));
            return await invitations.AcceptAsync(token, Guid.NewGuid());
        })));
        Assert.Single(results, r => r);
        await using var db = sql.Context(tenantA);
        Assert.Equal(1, await db.Memberships.CountAsync(m => m.Role == Role.Receptionist && m.Status == MembershipStatus.Active));
    }

    [Fact]
    public async Task InvitationsAreIsolatedPerTenantEvenWithQueryFiltersOff()
    {
        await InviteReceptionist();
        await using var db = sql.Context(tenantB);
        Assert.Empty(await db.Invitations.IgnoreQueryFilters().ToListAsync()); // row-level security
    }

    sealed class FixedUser(Guid? id) : ICurrentUser { public Guid? UserId => id; }

    sealed class Outbox : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool Fail { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("mail server down");
            lock (Sent) Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>A staff principal from request headers: user id, tenant_id and role, as the session will carry them.</summary>
    sealed class HeaderAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "test-headers", User = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Request.Headers[User].ToString()) }
                .Concat(new[] { StaffPolicies.TenantClaim, StaffPolicies.RoleClaim }.Select(c => new Claim(c, Request.Headers[c].ToString())));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Name)), Name)));
        }
    }
}

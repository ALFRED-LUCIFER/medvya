using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Domain;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-071 on real PostgreSQL through the real endpoints: a person works at several locations of the organisation through one membership
/// per location. A member not assigned to a location cannot act there (403), the picker lists only theirs, the owner sets locations (an
/// invitation carries them), the starter plan counts the person once, and offboarding ends access at every location at once.
/// </summary>
public sealed partial class StaffLocationsTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime, IDisposable
{
    readonly Guid tenant = Guid.NewGuid(), admin = Guid.NewGuid(), owner = Guid.NewGuid(), desk = Guid.NewGuid();
    Guid adyar, velachery;
    WebApplicationFactory<Program> factory = null!;
    readonly Outbox outbox = new();

    public async Task InitializeAsync()
    {
        adyar = Guid.NewGuid();
        velachery = Guid.NewGuid();
        await using (var db = sql.Context(tenant))
        {
            db.Tenants.Add(new Tenant { State = OrganisationState.Approved, Id = tenant, Name = "Iyer Family Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            db.Facilities.Add(new Facility { Id = adyar, TenantId = tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" });
            db.Facilities.Add(new Facility { Id = velachery, TenantId = tenant, Name = "Velachery (synthetic)", TimeZone = "Asia/Kolkata" });
            await db.SaveChangesAsync();
            Add(db, owner, Role.SoloDoctor, adyar);
            Add(db, admin, Role.PracticeAdmin, adyar);
            Add(db, admin, Role.PracticeAdmin, velachery); // the administrator of both locations is one seat
            await db.SaveChangesAsync();
        }
        await using (var identity = sql.Identity())
        {
            identity.Users.Add(new StaffUser { Id = admin, Issuer = "test-idp", Subject = admin.ToString(), DisplayName = "K. Lakshmi (synthetic)" });
            identity.Users.Add(new StaffUser { Id = desk, Issuer = "test-idp", Subject = desk.ToString(), DisplayName = "P. Devi (synthetic)" });
            await identity.SaveChangesAsync();
        }
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Invitations:LinkBaseUrl", "https://clinic.test")
            .ConfigureTestServices(s =>
            {
                s.AddSingleton<IEmailSender>(outbox);
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, HeaderAuth>(HeaderAuth.Name, null);
                s.Configure<AuthenticationOptions>(o => o.DefaultScheme = HeaderAuth.Name);
            }));
    }

    public Task DisposeAsync() => Task.CompletedTask;
    public void Dispose() => factory.Dispose();

    Guid Add(TenancyDbContext db, Guid user, Role role, Guid at, MembershipStatus status = MembershipStatus.Active)
    {
        var id = Guid.NewGuid();
        db.Memberships.Add(new Membership { Id = id, TenantId = tenant, UserId = user, FacilityId = at, Role = role, Status = status });
        return id;
    }

    HttpClient Client(Guid user, Guid at)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(HeaderAuth.User, user.ToString());
        client.DefaultRequestHeaders.Add(StaffPolicies.TenantClaim, tenant.ToString());
        client.DefaultRequestHeaders.Add(HeaderAuth.Facility, at.ToString());
        client.DefaultRequestHeaders.Add(HeaderAuth.Reauth, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)); // a fresh step-up (MDV-P1-107)
        return client;
    }

    async Task<Guid> Desk(MembershipStatus status = MembershipStatus.Active)
    {
        await using var db = sql.Context(tenant);
        var id = Add(db, desk, Role.Receptionist, adyar, status);
        await db.SaveChangesAsync();
        return id;
    }

    // The invitee joins: the same call the invitation page makes, in the token's tenant.
    async Task<bool> Accept(string token, Guid user) =>
        await new Invitations(sql.Context(tenant), new PostgresFixture.FixedTenant(tenant), new FixedUser(null), new AuditLog(sql.Audit(tenant)), outbox,
            Options.Create(new InvitationOptions()), null!).AcceptAsync(token, user);

    [Fact]
    public async Task AMemberNotAssignedToALocationIsRefusedThereAndTheOtherWorks()
    {
        await using (var db = sql.Context(tenant))
        {
            var other = Guid.NewGuid();
            Add(db, other, Role.PracticeAdmin, adyar, MembershipStatus.Active); // an administrator at Adyar only
            await db.SaveChangesAsync();
            using var atAdyar = Client(other, adyar);
            using var atVelachery = Client(other, velachery);
            Assert.Equal(HttpStatusCode.OK, (await atAdyar.GetAsync("/v1/staff")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await atVelachery.GetAsync("/v1/staff")).StatusCode);
        }
    }

    [Fact]
    public async Task ThePickerListsOnlyTheMembersLocations()
    {
        await Desk();
        var choices = await new MembershipDirectory(sql.ConnectionString, new FixedUser(desk)).ForCurrentUserAsync();
        Assert.Equal([adyar], choices.Select(x => x.FacilityId));
        var both = await new MembershipDirectory(sql.ConnectionString, new FixedUser(admin)).ForCurrentUserAsync();
        Assert.Equal(new[] { adyar, velachery }.Order(), both.Select(x => x.FacilityId).Order());
    }

    [Fact]
    public async Task TheOwnerAddsALocationAndTheMemberSwitchesToItWithoutANewSeat()
    {
        var membership = await Desk();
        using var c = Client(admin, adyar);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync($"/v1/staff/{membership}/locations", new { facilityIds = new[] { adyar, velachery } })).StatusCode);
        var choices = await new MembershipDirectory(sql.ConnectionString, new FixedUser(desk)).ForCurrentUserAsync();
        Assert.Equal(new[] { adyar, velachery }.Order(), choices.Select(x => x.FacilityId).Order());
        Assert.All(choices, x => Assert.Equal(Role.Receptionist, x.Role));
        // The plan holds the administrator and this person: two staff seats, so one more is refused, and the person's second location took none.
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/v1/staff", new { email = "desk2@example.test", role = "Nurse", facilityId = adyar })).StatusCode);
        await using var db = sql.Context(tenant);
        Assert.Contains(await db.SettingsChanges.Select(s => s.Section).ToListAsync(), s => s == "staff.locations");
    }

    [Fact]
    public async Task TakingALocationAwayNeedsAReasonAndEndsAccessOnlyThere()
    {
        var membership = await Desk();
        using var c = Client(admin, adyar);
        await c.PutAsJsonAsync($"/v1/staff/{membership}/locations", new { facilityIds = new[] { adyar, velachery } });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/v1/staff/{membership}/locations", new { facilityIds = new[] { adyar } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync($"/v1/staff/{membership}/locations", new { facilityIds = new[] { adyar }, reason = "Moved to Adyar" })).StatusCode);
        await using var db = sql.Context(tenant);
        Assert.Equal(MembershipStatus.Active, (await db.Memberships.SingleAsync(m => m.UserId == desk && m.FacilityId == adyar)).Status);
        Assert.Equal(MembershipStatus.Offboarded, (await db.Memberships.SingleAsync(m => m.UserId == desk && m.FacilityId == velachery)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/v1/staff/{membership}/locations", new { facilityIds = Array.Empty<Guid>() })).StatusCode);
    }

    [Fact]
    public async Task ALocationOfAnotherOrganisationIsRefused()
    {
        var membership = await Desk();
        using var c = Client(admin, adyar);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync($"/v1/staff/{membership}/locations", new { facilityIds = new[] { adyar, Guid.NewGuid() } })).StatusCode);
    }

    [Fact]
    public async Task AnAdministratorOfOneLocationCannotGrantAnother()
    {
        var membership = await Desk();
        var onlyAdyar = Guid.NewGuid();
        await using (var db = sql.Context(tenant))
        {
            Add(db, onlyAdyar, Role.PracticeAdmin, adyar);
            await db.SaveChangesAsync();
        }
        using var c = Client(onlyAdyar, adyar);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync($"/v1/staff/{membership}/locations", new { facilityIds = new[] { adyar, velachery } })).StatusCode);
        await using var check = sql.Context(tenant);
        Assert.Equal(1, await check.Memberships.CountAsync(m => m.UserId == desk));
    }

    [Fact]
    public async Task OnePersonAtTwoLocationsIsOneSeat()
    {
        await using (var db = sql.Context(tenant))
        {
            foreach (var m in await db.Memberships.Where(m => m.UserId == admin).ToListAsync()) m.Status = MembershipStatus.Offboarded; // only the owner and the person below hold seats
            Add(db, desk, Role.Receptionist, adyar);
            Add(db, desk, Role.Receptionist, velachery);
            await db.SaveChangesAsync();
        }
        using var c = Client(owner, adyar);
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/v1/staff", new { email = "desk2@example.test", role = "Nurse", facilityId = adyar })).StatusCode); // second of two
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/v1/staff", new { email = "desk3@example.test", role = "Nurse", facilityId = adyar })).StatusCode);
    }

    [Fact]
    public async Task AnInvitationCarriesTheLocationsAndAcceptingGrantsThemAsOneSeat()
    {
        using var c = Client(admin, adyar);
        var created = await c.PostAsJsonAsync("/v1/staff", new { email = "new.desk@example.test", role = "Receptionist", facilityId = adyar, alsoFacilityIds = new[] { velachery } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        // The pending invitee holds one seat for both locations: one more fits (the administrator is the other), a further one does not.
        await using (var db = sql.Context(tenant))
        {
            Assert.Equal(1, await db.Memberships.CountAsync(m => m.Status == MembershipStatus.Invited));
            Assert.Equal([velachery], (await db.Invitations.SingleAsync()).AlsoFacilityIds);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/v1/staff", new { email = "desk2@example.test", role = "Nurse", facilityId = adyar })).StatusCode);

        Assert.True(await Accept(outbox.Token(), desk));
        var choices = await new MembershipDirectory(sql.ConnectionString, new FixedUser(desk)).ForCurrentUserAsync();
        Assert.Equal(new[] { adyar, velachery }.Order(), choices.Select(x => x.FacilityId).Order());
        // The grant is checked server-side like the first location: an invitation to a location the inviter does not administer is refused.
        var other = Guid.NewGuid();
        await using (var db = sql.Context(tenant))
        {
            Add(db, other, Role.PracticeAdmin, adyar);
            await db.SaveChangesAsync();
        }
        using var limited = Client(other, adyar);
        Assert.Equal(HttpStatusCode.NotFound, (await limited.PostAsJsonAsync("/v1/staff", new { email = "x@example.test", role = "Nurse", facilityId = adyar, alsoFacilityIds = new[] { velachery } })).StatusCode);
    }

    [Fact]
    public async Task OffboardingEndsAccessAtEveryLocationAtOnceAndFreesTheSeat()
    {
        Guid first;
        await using (var db = sql.Context(tenant))
        {
            first = Add(db, desk, Role.Receptionist, adyar);
            Add(db, desk, Role.Receptionist, velachery);
            await db.SaveChangesAsync();
        }
        using var c = Client(admin, adyar);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/v1/staff/{first}/offboard", new { reason = "Left the practice" })).StatusCode);
        Assert.Empty(await new MembershipDirectory(sql.ConnectionString, new FixedUser(desk)).ForCurrentUserAsync());
        await using var check = sql.Context(tenant);
        Assert.Equal(2, await check.Memberships.CountAsync(m => m.UserId == desk && m.Status == MembershipStatus.Offboarded));
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/v1/staff", new { email = "desk2@example.test", role = "Nurse", facilityId = adyar })).StatusCode);
    }

    [Fact]
    public async Task ADoctorAtTwoLocationsHasAProfileAndLicenceAtEach()
    {
        using var c = Client(admin, adyar);
        var doctor = new
        {
            fullName = "Dr. Sana Khan (synthetic)", email = "sana.khan@example.test", specialty = "Paediatrics", facilityId = adyar, alsoFacilityIds = new[] { velachery },
            authority = "TNMC", licenceNumber = "84213", category = "Permanent", attested = true,
            expiresOn = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(5), newVisitFee = 50000, followUpFee = 30000,
        };
        await using (var db = sql.Context(tenant)) // the starter plan's one doctor seat is the owner's: free it for this test
        {
            (await db.Memberships.SingleAsync(m => m.UserId == owner)).Role = Role.PracticeAdmin;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/v1/settings/doctors", doctor)).StatusCode);
        Assert.True(await Accept(outbox.Token(), desk));
        await using var check = sql.Context(tenant);
        var memberships = await check.Memberships.Where(m => m.UserId == desk).ToListAsync();
        Assert.Equal(new[] { adyar, velachery }.Order(), memberships.Select(m => m.FacilityId).Order());
        foreach (var m in memberships)
        {
            Assert.Equal("Dr. Sana Khan (synthetic)", (await check.DoctorProfiles.SingleAsync(p => p.MembershipId == m.Id)).FullName);
            Assert.Equal("84213", (await check.DoctorLicences.SingleAsync(l => l.MembershipId == m.Id)).Number);
        }
    }

    sealed class FixedUser(Guid? id) : ICurrentUser { public Guid? UserId => id; }

    sealed partial class Outbox : IEmailSender
    {
        string? last;
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            last = message.Body;
            return Task.CompletedTask;
        }

        public string Token() => LinkToken().Match(last ?? throw new InvalidOperationException("No email sent.")).Groups[1].Value;

        [GeneratedRegex(@"/invite/(\S+)")]
        private static partial Regex LinkToken();
    }

    sealed class HeaderAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "test-headers", User = "X-Test-User", Facility = "facility_id", Reauth = "reauth_at";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Request.Headers[User].ToString()) }
                .Concat(new[] { StaffPolicies.TenantClaim, Facility, Reauth }.Where(Request.Headers.ContainsKey).Select(c => new Claim(c, Request.Headers[c].ToString())));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Name)), Name)));
        }
    }
}

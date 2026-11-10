using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Domain;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
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
/// MDV-P1-069 on real PostgreSQL through the real endpoints: the starter plan allows 1 doctor and 2 other staff per organisation,
/// across its locations, refused server-side with one message. Patients are not counted; offboarding and revoking free a seat.
/// </summary>
public sealed class PlanCapTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime, IDisposable
{
    const string Message = "Your plan allows 1 doctor and 2 staff; contact Medvya to upgrade";
    readonly Guid tenant = Guid.NewGuid(), admin = Guid.NewGuid(), owner = Guid.NewGuid();
    Guid facility, second;
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await using (var db = sql.Context(tenant))
        {
            facility = Guid.NewGuid();
            second = Guid.NewGuid();
            db.Tenants.Add(new Tenant { State = OrganisationState.Approved, Id = tenant, Name = "Iyer Family Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            db.Facilities.Add(new Facility { Id = facility, TenantId = tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" });
            db.Facilities.Add(new Facility { Id = second, TenantId = tenant, Name = "Velachery (synthetic)", TimeZone = "Asia/Kolkata" });
            await db.SaveChangesAsync();
            // The owner-doctor is the plan's one doctor; the admin is the first of two staff.
            Add(db, owner, Role.SoloDoctor, MembershipStatus.Active);
            Add(db, admin, Role.PracticeAdmin, MembershipStatus.Active);
            await db.SaveChangesAsync();
        }
        await using (var identity = sql.Identity())
        {
            identity.Users.Add(new StaffUser { Id = admin, Issuer = "test-idp", Subject = admin.ToString(), DisplayName = "K. Lakshmi (synthetic)" });
            await identity.SaveChangesAsync();
        }
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Invitations:LinkBaseUrl", "https://clinic.test")
            .ConfigureTestServices(s =>
            {
                s.AddSingleton<IEmailSender, Sink>();
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, HeaderAuth>(HeaderAuth.Name, null);
                s.Configure<AuthenticationOptions>(o => o.DefaultScheme = HeaderAuth.Name);
            }));
    }

    public Task DisposeAsync() => Task.CompletedTask;
    public void Dispose() => factory.Dispose();

    Guid Add(TenancyDbContext db, Guid? user, Role role, MembershipStatus status, Guid? at = null)
    {
        var id = Guid.NewGuid();
        db.Memberships.Add(new Membership { Id = id, TenantId = tenant, UserId = user, FacilityId = at ?? facility, Role = role, Status = status });
        return id;
    }

    HttpClient Client()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(HeaderAuth.User, admin.ToString());
        client.DefaultRequestHeaders.Add(StaffPolicies.TenantClaim, tenant.ToString());
        client.DefaultRequestHeaders.Add(HeaderAuth.Facility, facility.ToString());
        return client;
    }

    Task<HttpResponseMessage> AddStaff(HttpClient c, string email, Guid? at = null, string role = "Receptionist") =>
        c.PostAsJsonAsync("/v1/staff", new { email, role, facilityId = at ?? facility });

    static async Task AssertRefused(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Message, body.GetProperty("title").GetString());
        Assert.Equal("plan_limit", body.GetProperty("code").GetString());
    }

    static object Doctor(string email, Guid at) => new
    {
        fullName = "Dr. Sana Khan (synthetic)", email, specialty = "Paediatrics", facilityId = at, authority = "TNMC", licenceNumber = "84213",
        category = "Permanent", attested = true, expiresOn = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(5), newVisitFee = 50000, followUpFee = 30000,
    };

    [Fact]
    public async Task ThirdStaffMemberIsRefusedByAddAndInviteAndNothingIsSaved()
    {
        using var c = Client();
        Assert.Equal(HttpStatusCode.Created, (await AddStaff(c, "desk1@example.test")).StatusCode); // second of two
        await AssertRefused(await AddStaff(c, "desk2@example.test"));
        await AssertRefused(await c.PostAsJsonAsync("/v1/invitations", new { email = "desk3@example.test", role = "Nurse", facilityId = facility }));
        await using var db = sql.Context(tenant);
        Assert.Equal(1, await db.Invitations.CountAsync());
        Assert.Equal(1, await db.Memberships.CountAsync(m => m.Status == MembershipStatus.Invited));
    }

    [Fact]
    public async Task SecondDoctorIsRefusedAndStaffRoomDoesNotHelp()
    {
        using var c = Client();
        await AssertRefused(await c.PostAsJsonAsync("/v1/settings/doctors", Doctor("sana.khan@example.test", facility)));
        Assert.Equal(HttpStatusCode.Created, (await AddStaff(c, "desk1@example.test")).StatusCode); // staff seats are separate from the doctor seat
    }

    [Fact]
    public async Task LocationsShareOneCap()
    {
        await using (var db = sql.Context(tenant))
        {
            Add(db, Guid.NewGuid(), Role.Receptionist, MembershipStatus.Active, second); // the second staff seat is used at the other location
            await db.SaveChangesAsync();
        }
        using var c = Client();
        await AssertRefused(await AddStaff(c, "desk1@example.test", facility));
    }

    [Fact]
    public async Task PatientsAreNotCounted()
    {
        await using (var db = sql.Context(tenant))
        {
            for (var i = 0; i < 5; i++) Add(db, Guid.NewGuid(), Role.Patient, MembershipStatus.Active);
            await db.SaveChangesAsync();
        }
        using var c = Client();
        Assert.Equal(HttpStatusCode.Created, (await AddStaff(c, "desk1@example.test")).StatusCode);
    }

    [Fact]
    public async Task RevokingAnInvitationFreesTheSeat()
    {
        using var c = Client();
        var created = await AddStaff(c, "desk1@example.test");
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await AssertRefused(await AddStaff(c, "desk2@example.test"));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/v1/invitations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddStaff(c, "desk2@example.test")).StatusCode);
    }

    [Fact]
    public async Task OffboardingFreesTheSeatAndReactivatingNeedsOne()
    {
        Guid gone;
        await using (var db = sql.Context(tenant))
        {
            gone = Add(db, Guid.NewGuid(), Role.Receptionist, MembershipStatus.Offboarded);
            await db.SaveChangesAsync();
        }
        using var c = Client();
        Assert.Equal(HttpStatusCode.Created, (await AddStaff(c, "desk1@example.test")).StatusCode); // took the freed seat
        await AssertRefused(await c.PostAsJsonAsync($"/v1/staff/{gone}/reactivate", new { reason = "Returned from leave" }));
    }

    [Fact]
    public async Task AcceptingAnInvitationHoldingTheLastSeatChangesNothing()
    {
        using var c = Client();
        await AddStaff(c, "desk1@example.test");
        await using var db = sql.Context(tenant);
        // The pending invitation already holds the seat, so accepting it (Invited to Active) leaves the count, and the refusal, unchanged.
        var invited = await db.Memberships.SingleAsync(m => m.Status == MembershipStatus.Invited);
        (invited.UserId, invited.Status) = (Guid.NewGuid(), MembershipStatus.Active);
        await db.SaveChangesAsync();
        await AssertRefused(await AddStaff(c, "desk2@example.test"));
    }

    [Fact]
    public async Task ParallelAddsCannotExceedTheCap()
    {
        using var c = Client();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => AddStaff(c, $"racer{i}@example.test")));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var db = sql.Context(tenant);
        Assert.Equal(1, await db.Memberships.CountAsync(m => m.Status == MembershipStatus.Invited));
    }

    sealed class Sink : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    sealed class HeaderAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "test-headers", User = "X-Test-User", Facility = "facility_id";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Request.Headers[User].ToString()) }
                .Concat(new[] { StaffPolicies.TenantClaim, Facility }.Where(Request.Headers.ContainsKey).Select(c => new Claim(c, Request.Headers[c].ToString())));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Name)), Name)));
        }
    }
}

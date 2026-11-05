using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Infrastructure;
using Medvya.Patients.Application;
using Medvya.Patients.Infrastructure;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.PatientRulesTests;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-017 over HTTP: who may ask for a review (desk, Full), approve (clinician, Sign) and undo (admin, Config) is enforced by the
/// capability policy on the server, and a refused call changes nothing. Synthetic data only.
/// </summary>
public sealed class PatientMergeEndpointTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly Guid tenant = Guid.NewGuid(), facility = Guid.NewGuid();
    readonly Dictionary<string, Guid> users = [];
    Guid survivor, merged, request;

    public async Task InitializeAsync()
    {
        await using (var db = sql.Context(tenant))
        {
            db.Tenants.Add(new Tenant { Id = tenant, Name = "Iyer Family Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            db.Facilities.Add(new Facility { Id = facility, TenantId = tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" });
            foreach (var (name, role) in new[] { ("receptionist", Role.Receptionist), ("doctor", Role.HospitalDoctor), ("unverified", Role.HospitalDoctor), ("admin", Role.PracticeAdmin), ("nurse", Role.Nurse) })
            {
                users[name] = Guid.NewGuid();
                var membership = new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = users[name], FacilityId = facility, Role = role, Status = MembershipStatus.Active };
                db.Memberships.Add(membership);
                if (name == "doctor")
                    db.DoctorLicences.Add(new DoctorLicence { MembershipId = membership.Id, TenantId = tenant, Authority = "TNMC", Number = "84213", SubmittedAt = DateTimeOffset.UtcNow, VerifiedAt = DateTimeOffset.UtcNow });
            }
            await db.SaveChangesAsync();
        }
        var registry = new PatientRegistry(sql.Patients(tenant), new SqlServerFixture.FixedTenant(tenant), new PatientRegistryTests.FixedFacility(facility),
            new User(users["receptionist"]), new AuditLog(sql.Audit(tenant)), TimeProvider.System, new PatientRegistryTests.FacilityZone("Asia/Kolkata"));
        var one = await registry.RegisterAsync(IndiaBase with { FacilityId = facility, FullName = "Meera Iyer (synthetic)", Mobile = "+91 90000 00301" });
        var two = await registry.RegisterAsync(IndiaBase with { FacilityId = facility, FullName = "Anita Rao (synthetic)", Mobile = "+91 90000 00302", DateOfBirth = new DateOnly(1990, 5, 5) });
        (survivor, merged) = (one.PatientId, two.PatientId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    sealed class User(Guid id) : Medvya.BuildingBlocks.Domain.ICurrentUser { public Guid? UserId => id; }

    WebApplicationFactory<Program> Host() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s => PatientEndpointTests.SignInFromHeaders(s, tenant)));

    HttpClient Client(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", users[user].ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", facility.ToString());
        return client;
    }

    static Task<HttpResponseMessage> Post(HttpClient c, string path, object? body = null) => c.PostAsJsonAsync(new Uri(path, UriKind.Relative), body ?? new { });

    async Task<string> StatusAsync()
    {
        await using var db = sql.Patients(tenant);
        return (await db.MergeRequests.SingleAsync(r => r.Id == request)).Status.ToString();
    }

    [Fact]
    public async Task EachStepNeedsItsOwnRoleAndARefusedCallChangesNothing()
    {
        using var host = Host();
        using var nurse = Client(host, "nurse");
        using var desk = Client(host, "receptionist");
        using var doctor = Client(host, "doctor");
        using var admin = Client(host, "admin");

        // A nurse can read but not ask for a review.
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(nurse, "/v1/patients/merge-requests", new { patientId = merged, otherPatientId = survivor })).StatusCode);
        var created = await Post(desk, "/v1/patients/merge-requests", new { patientId = merged, otherPatientId = survivor });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        request = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await nurse.GetAsync(new Uri($"/v1/patients/merge-requests/{request}", UriKind.Relative))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Post(desk, $"/v1/patients/merge-requests/{request}/request-approval")).StatusCode);
        // The desk, the admin, a nurse and a doctor whose licence is not verified cannot approve: that is verified clinician work.
        using var unverified = Client(host, "unverified");
        foreach (var who in new[] { desk, admin, nurse, unverified })
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(who, $"/v1/patients/merge-requests/{request}/approve")).StatusCode);
        Assert.Equal("AwaitingApproval", await StatusAsync());

        Assert.Equal(HttpStatusCode.OK, (await Post(doctor, $"/v1/patients/merge-requests/{request}/approve")).StatusCode);
        Assert.Equal("Merged", await StatusAsync());

        // Only an admin undoes.
        foreach (var who in new[] { desk, doctor })
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(who, $"/v1/patients/merge-requests/{request}/undo")).StatusCode);
        Assert.Equal("Merged", await StatusAsync());
        var history = await desk.GetStringAsync(new Uri($"/v1/patients/{survivor}/merges", UriKind.Relative));
        Assert.Contains("Merged", history, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(admin, $"/v1/patients/merge-requests/{request}/undo")).StatusCode);
        Assert.Equal("Unmerged", await StatusAsync());
    }
}

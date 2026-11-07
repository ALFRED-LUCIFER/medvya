using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P2-024 over HTTP on real PostgreSQL: who may ask for and decide access (server-side, whatever the screen shows), the facility from
/// the session, field/code errors, and exactly one audit row per request whatever the answer.
/// </summary>
public sealed class AccessRequestEndpointTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s =>
            {
                PatientEndpointTests.SignInFromHeaders(s, h.Tenant);
                s.AddSingleton<TimeProvider>(h.Clock);
            }));
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    HttpClient Client(Guid user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        return client;
    }

    static Uri U(string path) => new(path, UriKind.Relative);

    Task<HttpResponseMessage> Ask(Guid user, Guid patient, object? body) =>
        Client(user).PostAsJsonAsync(U($"/v1/patients/{patient}/access-requests"), body);

    async Task<List<(string Action, AuditOutcome Outcome, Guid? Patient)>> Rows()
    {
        await using var db = sql.Audit(h.Tenant);
        return [.. (await db.Events.Where(e => e.Action.StartsWith("patient.access")).OrderBy(e => e.Id).ToListAsync()) // version-7 ids: creation order, whichever clock stamped the row
            .Select(e => (e.Action, e.Outcome, e.PatientId))];
    }

    [Fact]
    public async Task AClinicianAsksOnceAndEveryAttemptIsAuditedOnce()
    {
        using (var desk = await Ask(h.Reception, h.OtherFacilityPatient, new { reason = "CoveringColleague" }))
            Assert.Equal(HttpStatusCode.Forbidden, desk.StatusCode);
        using (var unknown = await Ask(h.Doctor, Guid.NewGuid(), new { reason = "CoveringColleague" }))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using (var noReason = await Ask(h.Doctor, h.OtherFacilityPatient, new { reason = 99 }))
            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        using var asked = await Ask(h.Doctor, h.OtherFacilityPatient, new { reason = "CoveringColleague" });
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.Equal("Pending", (await asked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var rows = await Rows();
        Assert.Equal(
            [("patient.access.request", AuditOutcome.Denied, h.OtherFacilityPatient), ("patient.access.request", AuditOutcome.Rejected, h.OtherFacilityPatient),
             ("patient.access.request", AuditOutcome.Allowed, h.OtherFacilityPatient)],
            rows.Where(r => r.Outcome != AuditOutcome.NotFound));
        Assert.Single(rows, r => r.Outcome == AuditOutcome.NotFound);
    }

    [Fact]
    public async Task OwnersSeeTheQueueWithNamesAndDecideOthersCannot()
    {
        using (await Ask(h.SecondDoctor, h.OtherFacilityPatient, new { reason = "Referral" })) { }

        using (var nurse = await Client(h.Nurse).GetAsync(U("/v1/access-requests")))
            Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode);
        using var queue = await Client(h.Admin).GetAsync(U("/v1/access-requests"));
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        var body = await queue.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(body.GetProperty("requests").EnumerateArray());
        Assert.Equal(("Lakshmi Pillai (synthetic)", "Dr. S. Menon (synthetic)", "Referral"),
            (item.GetProperty("patientName").GetString(), item.GetProperty("requesterName").GetString(), item.GetProperty("reason").GetString()));
        Assert.Equal([1, 7, 30], body.GetProperty("grantDays").EnumerateArray().Select(d => d.GetInt32()));
        var id = item.GetProperty("id").GetGuid();

        using (var nurse = await Client(h.Nurse).PostAsJsonAsync(U($"/v1/access-requests/{id}/grant"), new { days = 7 }))
            Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode);
        using (var odd = await Client(h.Admin).PostAsJsonAsync(U($"/v1/access-requests/{id}/grant"), new { days = 3 }))
            Assert.Equal(HttpStatusCode.BadRequest, odd.StatusCode);
        using (var granted = await Client(h.Admin).PostAsJsonAsync(U($"/v1/access-requests/{id}/grant"), new { days = 7 }))
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        using (var twice = await Client(h.Admin).PostAsync(U($"/v1/access-requests/{id}/decline"), null))
        {
            Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
            Assert.Equal("not_pending", (await twice.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        using (var missing = await Client(h.Admin).PostAsync(U($"/v1/access-requests/{Guid.NewGuid()}/decline"), null))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var outcomes = (await Rows()).Where(r => r.Action != "patient.access.request").Select(r => (r.Action, r.Outcome)).ToList();
        Assert.Equal(
            [("patient.access.queue", AuditOutcome.Denied), ("patient.access.queue", AuditOutcome.Allowed), ("patient.access.grant", AuditOutcome.Denied),
             ("patient.access.grant", AuditOutcome.Rejected), ("patient.access.grant", AuditOutcome.Allowed), ("patient.access.decline", AuditOutcome.Rejected),
             ("patient.access.decline", AuditOutcome.NotFound)],
            outcomes);
    }

    [Fact]
    public async Task OnlyAnOwnerSetsTheClinicPolicy()
    {
        using (var nurse = await Client(h.Nurse).PutAsJsonAsync(U("/v1/settings/clinical-access"), new { policy = "AssignedOnly" }))
            Assert.Equal(HttpStatusCode.Forbidden, nurse.StatusCode);
        using (var set = await Client(h.Admin).PutAsJsonAsync(U("/v1/settings/clinical-access"), new { policy = "AssignedOnly" }))
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        using var read = await Client(h.Doctor).GetAsync(U("/v1/settings/clinical-access"));
        Assert.Equal("AssignedOnly", (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("policy").GetString());
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P2-018 over HTTP: only the ordering doctor places, errors are codes, a refusal or a missing encounter is audited, and nothing travels in a URL.</summary>
public sealed class OrderEndpointTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;
    Guid encounter;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await using (var db = sql.Context(h.Tenant))
        {
            var membership = await db.Memberships.SingleAsync(m => m.UserId == h.Doctor);
            db.DoctorLicences.Add(new Medvya.Tenancy.Domain.DoctorLicence { MembershipId = membership.Id, TenantId = h.Tenant, Authority = "TNMC", Number = "84213", SubmittedAt = h.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        var visit = await h.BookAsync(10);
        await h.FrontDesk(h.Reception).CheckInAsync(visit.Id, new Medvya.Scheduling.Application.CheckIn());
        encounter = (await h.Get<IEncounters>(h.Doctor).StartAsync(new StartEncounter(visit.Id)))!.Id;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s => PatientEndpointTests.SignInFromHeaders(s, h.Tenant)));
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

    [Fact]
    public async Task TheDoctorComposesPlacesAndPrintsAndEveryCallLeavesOneAuditRowWithThePatient()
    {
        var doctor = Client(h.Doctor);
        using (var add = await doctor.PostAsJsonAsync(U($"/v1/encounters/{encounter}/orders"), new { kind = "Lab", name = "HbA1c", note = "fasting 10 h" }))
        {
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
            Assert.Equal("Draft", (await add.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("state").GetString());
        }
        using (var none = await doctor.GetAsync(U($"/v1/encounters/{encounter}/orders/slip"))) Assert.Equal(HttpStatusCode.NotFound, none.StatusCode); // nothing placed
        using (var place = await doctor.PostAsJsonAsync(U($"/v1/encounters/{encounter}/orders/place"), new { destination = "Metro Diagnostics" }))
            Assert.Equal("Ordered", (await place.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("state").GetString());
        using (var again = await doctor.PostAsJsonAsync(U($"/v1/encounters/{encounter}/orders/place"), new { }))
        {
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal("nothing_to_place", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        using (var slip = await doctor.GetAsync(U($"/v1/encounters/{encounter}/orders/slip")))
        {
            var body = await slip.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(("Meera Iyer (synthetic)", "000001", "Metro Diagnostics"), (body.GetProperty("patient").GetProperty("fullName").GetString(), body.GetProperty("patient").GetProperty("mrn").GetString(), body.GetProperty("destination").GetString()));
        }
        using (var list = await doctor.GetAsync(U($"/v1/encounters/{encounter}/orders"))) Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.ResourceType == "order").OrderBy(e => e.Id).ToListAsync();
        // add, the empty slip (NotFound, written by the endpoint), place, the repeated place (Rejected), the slip, the list.
        Assert.Equal(["order.add", "order.place", "order.place", "order.read", "order.slip", "order.slip"], rows.Select(r => r.Action).Order());
        Assert.Equal(1, rows.Count(r => r.Outcome == AuditOutcome.NotFound));
        Assert.Equal(1, rows.Count(r => r.Outcome == AuditOutcome.Rejected));
        Assert.All(rows.Where(r => r.Outcome == AuditOutcome.Allowed), r => Assert.Equal(h.Patient, r.PatientId));
    }

    [Fact]
    public async Task ValidationIsA400WithCodesAnUnknownEncounterIsA404AndOtherRolesAre403()
    {
        using (var bad = await Client(h.Doctor).PostAsJsonAsync(U($"/v1/encounters/{encounter}/orders"), new { kind = "Lab", name = " " }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal("required", (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("name")[0].GetString());
        }
        using (var empty = await Client(h.Doctor).PostAsync(U($"/v1/encounters/{encounter}/orders"), new StringContent("null", System.Text.Encoding.UTF8, "application/json")))
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using (var unknown = await Client(h.Doctor).GetAsync(U($"/v1/encounters/{Guid.NewGuid()}/orders"))) Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        foreach (var user in new[] { h.Nurse, h.Reception, h.Admin })
        {
            using var denied = await Client(user).GetAsync(U($"/v1/encounters/{encounter}/orders"));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using var place = await Client(h.Nurse).PostAsJsonAsync(U($"/v1/encounters/{encounter}/orders/place"), new { });
        Assert.Equal(HttpStatusCode.Forbidden, place.StatusCode);
        await using var db = sql.Prescribing(h.Tenant);
        Assert.Equal(0, await db.Orders.CountAsync()); // nothing was written by a refused or invalid call
    }
}

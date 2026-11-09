using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P2-014 over HTTP: the doctor reads, the practice adds under settings, the other roles are refused, and errors come back as codes.</summary>
public sealed class FormularyEndpointTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        // Signing capabilities (Prescriptions and orders at Sign) stay off until the doctor's licence is verified.
        await using (var db = sql.Context(h.Tenant))
        {
            var membership = await db.Memberships.SingleAsync(m => m.UserId == h.Doctor);
            db.DoctorLicences.Add(new Medvya.Tenancy.Domain.DoctorLicence { MembershipId = membership.Id, TenantId = h.Tenant, Authority = "TNMC", Number = "84213", SubmittedAt = DateTimeOffset.UtcNow, VerifiedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
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
    public async Task ADoctorSearchesAndTheAdminAddsAndBothSeeIt()
    {
        using (var add = await Client(h.Admin).PostAsJsonAsync(U("/v1/settings/formulary"),
            new { generic = "Glimepiride", strength = "2 mg", form = "Tablet", brands = (string[])["Synthetic brand"], regClass = "ScheduleH", atc = "A10BB12" }))
        {
            Assert.Equal(HttpStatusCode.Created, add.StatusCode);
            var body = await add.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(("ClinicAdded", "ScheduleH"), (body.GetProperty("source").GetString(), body.GetProperty("regClass").GetString()));
            Assert.Equal("warning_schedule_h", body.GetProperty("printRequirements")[0].GetString());
        }
        foreach (var path in new[] { "/v1/formulary?q=synthetic%20brand", "/v1/settings/formulary?q=glimep" })
        {
            using var found = await Client(path.StartsWith("/v1/settings", StringComparison.Ordinal) ? h.Admin : h.Doctor).GetAsync(U(path));
            Assert.Equal(HttpStatusCode.OK, found.StatusCode);
            Assert.Equal("Glimepiride", (await found.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("generic").GetString());
        }
        await using var audit = sql.Audit(h.Tenant);
        Assert.Equal(1, await audit.Events.CountAsync(e => e.Action == "formulary.add" && e.Outcome == AuditOutcome.Allowed && e.ActorUserId == h.Admin));
    }

    [Fact]
    public async Task ABadAddIsA400WithCodesAndADuplicateIsA409()
    {
        using var bad = await Client(h.Admin).PostAsJsonAsync(U("/v1/settings/formulary"), new { generic = "Synthetic", strength = "1 mg", form = "Tablet", regClass = "Controlled" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("not_in_country", (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("regClass")[0].GetString());
        using var empty = await Client(h.Admin).PostAsync(U("/v1/settings/formulary"), new StringContent("null", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using var duplicate = await Client(h.Admin).PostAsJsonAsync(U("/v1/settings/formulary"), new { generic = "metformin", strength = "500 mg", form = "Tablet", regClass = "ScheduleH" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await using var audit = sql.Audit(h.Tenant);
        Assert.False(await audit.Events.AnyAsync(e => e.Action == "formulary.add")); // nothing was added, so nothing is recorded as done
    }

    [Fact]
    public async Task OrderSetsAreBuiltByTheAdminAndReadByTheDoctor()
    {
        using (var create = await Client(h.Admin).PostAsJsonAsync(U("/v1/settings/order-sets"), new { name = "Diabetes review labs", items = (string[])["HbA1c", "Fasting glucose"] }))
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var list = await Client(h.Doctor).GetAsync(U("/v1/order-sets"));
        var sets = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("Diabetes review labs", 2), (sets[0].GetProperty("name").GetString(), sets[0].GetProperty("items").GetArrayLength()));
        using var again = await Client(h.Admin).PostAsJsonAsync(U("/v1/settings/order-sets"), new { name = "diabetes review labs", items = (string[])["x"] });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task OnlyTheRightRolesAreLetIn()
    {
        foreach (var (user, path, expected) in new[]
        {
            (h.Nurse, "/v1/formulary", HttpStatusCode.Forbidden), (h.Reception, "/v1/order-sets", HttpStatusCode.Forbidden),
            (h.Admin, "/v1/formulary", HttpStatusCode.Forbidden), (h.SecondDoctor, "/v1/settings/formulary", HttpStatusCode.Forbidden),
            (h.Doctor, "/v1/formulary", HttpStatusCode.OK), (h.Admin, "/v1/settings/formulary", HttpStatusCode.OK),
        })
        {
            using var response = await Client(user).GetAsync(U(path));
            Assert.Equal(expected, response.StatusCode);
        }
    }
}

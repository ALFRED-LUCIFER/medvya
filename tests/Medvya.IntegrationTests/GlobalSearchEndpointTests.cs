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
/// MDV-P2-003 over HTTP on real PostgreSQL: the ⌘K lookup returns only initials, age and the MRN tail (payload test), says per row whether the
/// caller may open the chart or must ask, and audits each search for treatment. Synthetic data only.
/// </summary>
public sealed class GlobalSearchEndpointTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
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

    async Task<(HttpStatusCode Status, string Json)> Lookup(Guid user, object body)
    {
        using var res = await Client(user).PostAsJsonAsync(U("/v1/patients/lookup"), body);
        return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    static string AccessOf(string json) => JsonDocument.Parse(json).RootElement.GetProperty("items")[0].GetProperty("access").GetString()!;

    [Fact]
    public async Task ResultsCarryOnlyInitialsAgeAndTheMrnTail()
    {
        var (status, json) = await Lookup(h.Doctor, new { query = "meera" });
        Assert.Equal(HttpStatusCode.OK, status);
        var row = Assert.Single(JsonDocument.Parse(json).RootElement.GetProperty("items").EnumerateArray());
        // The whole shape, so a field added later fails here until someone decides it is safe to show before the chart is opened.
        Assert.Equal(["access", "ageEstimated", "ageYears", "id", "initials", "mrnTail"], row.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(("MI", "Open"), (row.GetProperty("initials").GetString(), row.GetProperty("access").GetString()));
        foreach (var leak in new[] { "Meera", "Iyer", "+91", "9000000101", "1972", "000001" }) Assert.DoesNotContain(leak, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NothingTypedListsNothingAndTheTextIsNeverTakenFromTheUrl()
    {
        Assert.Equal(0, JsonDocument.Parse((await Lookup(h.Doctor, new { query = "  " })).Json).RootElement.GetProperty("items").GetArrayLength());
        using var viaUrl = await Client(h.Doctor).PostAsJsonAsync(U("/v1/patients/lookup?query=meera"), new { });
        Assert.Equal(0, (await viaUrl.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").GetArrayLength());
        using var get = await Client(h.Doctor).GetAsync(U("/v1/patients/lookup?query=meera"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
    }

    [Fact]
    public async Task OutsideTheCareRelationshipTheRowOffersARequestAndThenShowsItPending()
    {
        using (var set = await Client(h.Admin).PutAsJsonAsync(U("/v1/settings/clinical-access"), new { policy = "AssignedOnly" }))
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal("CanRequest", AccessOf((await Lookup(h.SecondDoctor, new { query = "meera" })).Json));

        using (var asked = await Client(h.SecondDoctor).PostAsJsonAsync(U($"/v1/patients/{h.Patient}/access-requests"), new { reason = "Referral" }))
            Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.Equal("RequestPending", AccessOf((await Lookup(h.SecondDoctor, new { query = "meera" })).Json));
        // Someone else's request opens nothing for this doctor.
        Assert.Equal("CanRequest", AccessOf((await Lookup(h.Doctor, new { query = "meera" })).Json));
    }

    [Fact]
    public async Task ARoleThatDoesNotTreatPatientsCannotOpenOrAsk()
    {
        Assert.Equal("NoAccess", AccessOf((await Lookup(h.Reception, new { query = "meera" })).Json));
        Assert.Equal(HttpStatusCode.Forbidden, (await Lookup(h.PortalUser, new { query = "meera" })).Status);
    }

    [Fact]
    public async Task EachLookupIsAuditedOnceForTreatmentWithoutAPatient()
    {
        await Lookup(h.Doctor, new { query = "meera" });
        await using var db = sql.Audit(h.Tenant);
        var row = Assert.Single(await db.Events.Where(e => e.Action == "patient.search").ToListAsync());
        Assert.Equal((AuditPurpose.Treatment, AuditOutcome.Allowed, (Guid?)null), (row.Purpose, row.Outcome, row.PatientId));
    }

    // MDV-P2-003 follow-up (#957): the lookup has its own per-user budget; a desk that used up its searches can still use ⌘K, and the reverse.
    [Fact]
    public async Task TheLookupHasItsOwnRateLimitSeparateFromDeskSearch()
    {
        await using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:PatientSearchPerMinute", "1").UseSetting("RateLimiting:PatientLookupsPerMinute", "2"));
        using var doctor = limited.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        doctor.DefaultRequestHeaders.Add("X-Test-User", h.Doctor.ToString());
        doctor.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        async Task<HttpStatusCode> Post(string path)
        {
            using var r = await doctor.PostAsJsonAsync(U(path), new { query = "meera" });
            return r.StatusCode;
        }
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], [await Post("/v1/patients/search"), await Post("/v1/patients/search")]);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests],
            [await Post("/v1/patients/lookup"), await Post("/v1/patients/lookup"), await Post("/v1/patients/lookup")]);
    }
}

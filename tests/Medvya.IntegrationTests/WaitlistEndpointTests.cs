using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Domain;
using Medvya.Scheduling.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-027 through HTTP on real PostgreSQL: the JSON the waitlist screen reads, answers for refused callers and other tenants,
/// and one audit row per attempt (a refusal is Denied).
/// </summary>
public sealed class WaitlistEndpointTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    static readonly string[] Days = ["Monday", "Wednesday"]; // the API names days, as the screen sends and reads them
    readonly SchedulingHarness h = new(sql), other = new(sql);
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await other.SeedAsync();
        await h.DoctorTemplateAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s => s.AddSingleton<TimeProvider>(h.Clock).AddSingleton<IStartupFilter>(new FrontDeskEndpointTests.SignedInFromHeader(h.Tenant))));
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    HttpClient Client(Guid? user, Guid? tenant = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (user is { } u) client.DefaultRequestHeaders.Add("X-Test-User", u.ToString());
        if (tenant is { } t) client.DefaultRequestHeaders.Add("X-Test-Tenant", t.ToString());
        client.DefaultRequestHeaders.Add("X-Medvya-Csrf", "1");
        return client;
    }

    [Fact]
    public async Task TheDeskAddsOffersAndAcceptsOverHttpInTheShapeTheScreenReads()
    {
        var desk = Client(h.Reception);
        var added = await desk.PostAsJsonAsync("/v1/waitlist", new
        {
            facilityId = h.Facility, patientId = h.Patient, reasonCategory = "General", visitType = "FollowUp", days = Days, from = "09:00:00", until = "11:00:00",
        });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var entry = await added.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(Days, entry.GetProperty("days").EnumerateArray().Select(d => d.GetString()));
        Assert.Equal("09:00:00", entry.GetProperty("from").GetString());
        Assert.Equal("Waiting", entry.GetProperty("state").GetString());

        h.Clock.Advance(TimeSpan.FromHours(1));
        var list = await desk.GetFromJsonAsync<JsonElement>($"/v1/waitlist?facilityId={h.Facility}");
        var row = list.GetProperty("entries")[0];
        var match = row.GetProperty("match");
        Assert.Equal(At(9), match.GetProperty("start").GetDateTimeOffset());
        Assert.Equal("Dr. Arjun Rao (synthetic)", match.GetProperty("calendarName").GetString());

        var id = row.GetProperty("id").GetGuid();
        var offered = await desk.PostAsJsonAsync($"/v1/waitlist/{id}/offer", new { calendarId = match.GetProperty("calendarId").GetGuid(), start = match.GetProperty("start").GetDateTimeOffset() });
        Assert.Equal(HttpStatusCode.OK, offered.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await desk.PostAsJsonAsync($"/v1/waitlist/{id}/offer", new { calendarId = h.Doctor, start = At(9) })).StatusCode); // already offered
        var accepted = await desk.PostAsync($"/v1/waitlist/{id}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(At(9), (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("start").GetDateTimeOffset());
        Assert.Equal(HttpStatusCode.Conflict, (await desk.DeleteAsync($"/v1/waitlist/{id}")).StatusCode); // booked: nothing left to remove
    }

    [Fact]
    public async Task RefusedCallersAndOtherTenantsAreTurnedAwayAndEveryAttemptIsAudited()
    {
        var entry = await h.Get<Medvya.Scheduling.Application.IWaitlistService>(h.Reception).AddAsync(new() { FacilityId = h.Facility, PatientId = h.Patient });
        var url = $"/v1/waitlist?facilityId={h.Facility}";

        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(h.Nurse).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(h.PortalUser).PostAsync($"/v1/waitlist/{entry.Id}/decline", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(h.Elsewhere).DeleteAsync($"/v1/waitlist/{entry.Id}")).StatusCode); // another location's admin
        Assert.Equal(HttpStatusCode.NotFound, (await Client(other.Reception, other.Tenant).DeleteAsync($"/v1/waitlist/{entry.Id}")).StatusCode); // another practice
        Assert.Equal(HttpStatusCode.OK, (await Client(h.Reception).GetAsync(url)).StatusCode);

        await using var audit = sql.Audit(h.Tenant);
        var rows = await audit.Events.Where(e => e.ResourceType == "waitlist").OrderBy(e => e.OccurredAt).Select(e => new { e.ActorUserId, e.Outcome }).ToListAsync();
        Assert.Contains(rows, r => r.ActorUserId == h.Nurse && r.Outcome == AuditOutcome.Denied);
        Assert.Contains(rows, r => r.ActorUserId == h.PortalUser && r.Outcome == AuditOutcome.Denied);
        Assert.Contains(rows, r => r.ActorUserId == h.Reception && r.Outcome == AuditOutcome.Allowed);
        Assert.Equal(WaitlistState.Waiting, (await h.Get<Medvya.Scheduling.Application.IWaitlistService>(h.Reception).ListAsync(h.Facility)).Entries.Single().State);
    }
}

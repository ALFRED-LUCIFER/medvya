using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-028 through HTTP on real SQL Server: the board payload is nonclinical (security test on the exact fields), moves
/// answer 409 with a code, unknown visits 404, and every request writes one audit row.
/// </summary>
public sealed class FrontDeskEndpointTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql);
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s => s.AddSingleton<TimeProvider>(h.Clock).AddSingleton<IStartupFilter>(new SignedInFromHeader(h.Tenant))));
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await h.CloseAsync();
    }

    /// <summary>The synthetic user named in X-Test-User, in the test tenant.</summary>
    // shortcut: sets the tenant claim directly because sign-in does not issue one before the facility picker (MDV-P0-012).
    internal sealed class SignedInFromHeader(Guid tenant) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                if (ctx.Request.Headers["X-Test-User"].ToString() is { Length: > 0 } user)
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user), new Claim("tenant_id", tenant.ToString())], "synthetic"));
                return nxt(ctx);
            });
            next(app);
        };
    }

    HttpClient Client(Guid user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        return client;
    }

    async Task<List<string>> AuditActions()
    {
        await using var db = sql.Audit(h.Tenant);
        return await db.Events.OrderBy(e => e.OccurredAt).Select(e => e.Action).ToListAsync();
    }

    [Fact]
    public async Task TheBoardPayloadCarriesOnlyNonclinicalFields()
    {
        await h.BookAsync(9); // booked with a free-text reason in the patient's words
        using var response = await Client(h.Reception).GetAsync($"/v1/front-desk/board?facilityId={h.Facility}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;

        Assert.Equal(["facilityId", "timeZone", "date", "now", "cards"], json.EnumerateObject().Select(p => p.Name));
        var card = Assert.Single(json.GetProperty("cards").EnumerateArray());
        // Exactly these fields: adding one (the reason text, a phone number, an identifier) must be a reviewed change here.
        Assert.Equal(
            ["id", "reference", "patientId", "patientName", "dateOfBirth", "registrationIncomplete", "calendarId", "calendarName",
             "visitType", "reasonCategory", "start", "end", "state", "since"],
            card.EnumerateObject().Select(p => p.Name));
        Assert.Equal(("ChronicReview", "Booked", "1972-03-12"), (card.GetProperty("reasonCategory").GetString(), card.GetProperty("state").GetString(), card.GetProperty("dateOfBirth").GetString()));
        Assert.DoesNotContain("chest", body, StringComparison.OrdinalIgnoreCase); // the reason in the patient's words
        Assert.DoesNotContain("+9190", body, StringComparison.Ordinal); // the patient's mobile
        Assert.DoesNotContain("000001", body, StringComparison.Ordinal); // no MRN on the board either
    }

    [Fact]
    public async Task CheckInThenAWrongMoveGets409AndAnUnknownVisit404()
    {
        var visit = await h.BookAsync(9);
        var reception = Client(h.Reception);

        using var checkIn = await reception.PostAsJsonAsync($"/v1/appointments/{visit.Id}/check-in", new { elapsedMs = 9000 });
        Assert.Equal(HttpStatusCode.OK, checkIn.StatusCode);
        Assert.Equal("CheckedIn", JsonDocument.Parse(await checkIn.Content.ReadAsStringAsync()).RootElement.GetProperty("state").GetString());
        using var repeat = await reception.PostAsync($"/v1/appointments/{visit.Id}/check-in", null);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode); // a double tap is answered, not refused

        using var skip = await reception.PostAsJsonAsync($"/v1/appointments/{visit.Id}/status", new { state = "InConsult" });
        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);
        Assert.Equal("invalid_transition", JsonDocument.Parse(await skip.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());

        using var next = await reception.PostAsJsonAsync($"/v1/appointments/{visit.Id}/status", new { state = "WithNurse" });
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);

        using var unknown = await reception.PostAsJsonAsync($"/v1/appointments/{Guid.NewGuid()}/status", new { state = "WithNurse" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var readOnly = await Client(h.SecondDoctor).PostAsJsonAsync($"/v1/appointments/{visit.Id}/status", new { state = "ReadyForDoctor" });
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);

        // One event per attempt. Moves are audited by the use case on the test's stopped clock, so they are compared as a set.
        Assert.Equal(["appointment.check_in", "appointment.check_in", "appointment.status", "appointment.status", "appointment.status", "appointment.status"],
            (await AuditActions()).Where(a => a.StartsWith("appointment.", StringComparison.Ordinal) && a != "appointment.book").Order(StringComparer.Ordinal));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
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
public sealed class FrontDeskEndpointTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql), other = new(sql);
    WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await other.SeedAsync(); // a second clinic in its own tenant
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
                {
                    List<Claim> claims = [new(ClaimTypes.NameIdentifier, user), new("tenant_id", ctx.Request.Headers["X-Test-Tenant"].ToString() is { Length: > 0 } t ? t : tenant.ToString())];
                    // The access log is scoped by role and facility claims (as AccessLogTests signs in).
                    if (ctx.Request.Headers["X-Test-Role"].ToString() is { Length: > 0 } role)
                        claims.AddRange([new Claim("role", role), new Claim("facility_id", ctx.Request.Headers["X-Test-Facility"].ToString())]);
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "synthetic"));
                }
                return nxt(ctx);
            });
            next(app);
        };
    }

    HttpClient Client(Guid user, Guid? tenant = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        if (tenant is { } t) client.DefaultRequestHeaders.Add("X-Test-Tenant", t.ToString());
        return client;
    }

    async Task<List<string>> AuditActions()
    {
        await using var db = sql.Audit(h.Tenant);
        return await db.Events.OrderBy(e => e.OccurredAt).Select(e => e.Action).ToListAsync();
    }

    async Task<List<(Guid? Actor, AuditOutcome Outcome)>> BoardReads()
    {
        await using var db = sql.Audit(h.Tenant);
        return (await db.Events.Where(e => e.Action == "front_desk.board.read").OrderBy(e => e.OccurredAt).Select(e => new { e.ActorUserId, e.Outcome }).ToListAsync())
            .Select(e => (e.ActorUserId, e.Outcome)).ToList();
    }

    [Fact]
    public async Task RapidBoardReadsByOneUserAreAuditedOncePerWindowAndEachOtherUserAndRefusalIsAuditedAlways()
    {
        var url = $"/v1/front-desk/board?facilityId={h.Facility}";
        var reception = Client(h.Reception);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await reception.GetAsync(url)).StatusCode);
        Assert.Equal([(h.Reception, AuditOutcome.Allowed)], (await BoardReads()).Select(r => ((Guid?)r.Actor, r.Outcome)).ToList());

        // Another user is not covered by this user's window; a refusal is never covered, however often it repeats.
        Assert.Equal(HttpStatusCode.OK, (await Client(h.Nurse).GetAsync(url)).StatusCode);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.Forbidden, (await Client(h.PortalUser).GetAsync(url)).StatusCode);
        // Another facility's board is a different read, so it is not hidden either (refused here, and recorded).
        Assert.Equal(HttpStatusCode.Forbidden, (await reception.GetAsync($"/v1/front-desk/board?facilityId={h.OtherFacility}")).StatusCode);
        Assert.Equal(
            [(h.Reception, AuditOutcome.Allowed), (h.Nurse, AuditOutcome.Allowed), (h.PortalUser, AuditOutcome.Denied), (h.PortalUser, AuditOutcome.Denied), (h.Reception, AuditOutcome.Denied)],
            (await BoardReads()).Select(r => (r.Actor!.Value, r.Outcome)).ToList());

        // Inside the window nothing more; after it, the first read writes again.
        h.Clock.Advance(AuditWindow.Repeated - TimeSpan.FromSeconds(1));
        await reception.GetAsync(url);
        Assert.Equal(5, (await BoardReads()).Count);
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        await reception.GetAsync(url);
        Assert.Equal(6, (await BoardReads()).Count);

        // The access log still lists the surviving rows: one Allowed read by reception per window, none for the coalesced calls.
        var admin = Client(h.Admin);
        admin.DefaultRequestHeaders.Add("X-Test-Role", "PracticeAdmin");
        admin.DefaultRequestHeaders.Add("X-Test-Facility", h.Facility.ToString());
        var log = await admin.GetFromJsonAsync<JsonElement>("/v1/audit/access-log?limit=200");
        Assert.Equal(2, log.GetProperty("items").EnumerateArray().Count(i =>
            i.GetProperty("action").GetString() == "front_desk.board.read" && i.GetProperty("actorUserId").GetGuid() == h.Reception && i.GetProperty("outcome").GetString() == "Allowed"));
    }

    [Fact]
    public async Task AnotherTenantsBoardReadInsideThisTenantsWindowIsAuditedInItsOwnTenant()
    {
        Assert.Equal(HttpStatusCode.OK, (await Client(h.Reception).GetAsync($"/v1/front-desk/board?facilityId={h.Facility}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(other.Reception, other.Tenant).GetAsync($"/v1/front-desk/board?facilityId={other.Facility}")).StatusCode);
        await using var db = sql.Audit(other.Tenant);
        Assert.Equal([other.Reception], await db.Events.Where(e => e.Action == "front_desk.board.read").Select(e => e.ActorUserId!.Value).ToListAsync());
    }

    [Fact]
    public void TheWindowKeyNamesTheTenantSoOneTenantsWindowNeverCoversAnother()
    {
        var window = new AuditWindow(h.Clock);
        Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid(), user = Guid.NewGuid();
        window.Record(AuditWindow.Key(tenantA, user, "front_desk.board.read", "/board?facilityId=1"), AuditWindow.Repeated);
        Assert.True(window.Covered(AuditWindow.Key(tenantA, user, "front_desk.board.read", "/board?facilityId=1")));
        Assert.False(window.Covered(AuditWindow.Key(tenantB, user, "front_desk.board.read", "/board?facilityId=1")));
    }

    [Fact]
    public async Task TheBoardPayloadCarriesOnlyNonclinicalFields()
    {
        await h.BookAsync(9); // booked with a free-text reason in the patient's words
        using var response = await Client(h.Reception).GetAsync($"/v1/front-desk/board?facilityId={h.Facility}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;

        Assert.Equal(["facilityId", "timeZone", "date", "now", "cards", "leftWithoutBeingSeen"], json.EnumerateObject().Select(p => p.Name)); // a count only (MDV-P1-100)
        var card = Assert.Single(json.GetProperty("cards").EnumerateArray());
        // Exactly these fields: adding one (the reason text, a phone number, an identifier) must be a reviewed change here.
        Assert.Equal(
            ["id", "reference", "patientId", "patientName", "dateOfBirth", "registrationIncomplete", "calendarId", "calendarName",
             "visitType", "reasonCategory", "start", "end", "state", "since", "room", "token", "walkIn", "unscheduled", "sex", "withDoctor", "followUp", "queuePlace"], // queuePlace: the place in the clinic's queue order, a number only (MDV-P1-103); // sex at birth: with the date of birth, how a desk tells two patients of one name apart (MDV-AUDIT-1105); withDoctor: a consultation is open (MDV-P1-100); followUp: on a Done card, the date and doctor only (MDV-P1-102, its fields guarded in FollowUpDueTests)
            card.EnumerateObject().Select(p => p.Name));
        Assert.Equal(("ChronicReview", "Booked", "1972-03-12"), (card.GetProperty("reasonCategory").GetString(), card.GetProperty("state").GetString(), card.GetProperty("dateOfBirth").GetString()));
        Assert.Equal("Female", card.GetProperty("sex").GetString());
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
        Assert.Equal("ReadyForDoctor", JsonDocument.Parse(await checkIn.Content.ReadAsStringAsync()).RootElement.GetProperty("state").GetString());
        using var repeat = await reception.PostAsync($"/v1/appointments/{visit.Id}/check-in", null);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode); // a double tap is answered, not refused

        using var skip = await reception.PostAsJsonAsync($"/v1/appointments/{visit.Id}/status", new { state = "Done" }); // nurse step off: check-in already landed in Ready for doctor
        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);
        Assert.Equal("invalid_transition", JsonDocument.Parse(await skip.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());

        using var next = await reception.PostAsJsonAsync($"/v1/appointments/{visit.Id}/status", new { state = "ReadyForDoctor" });
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);

        using var unknown = await reception.PostAsJsonAsync($"/v1/appointments/{Guid.NewGuid()}/status", new { state = "ReadyForDoctor" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var readOnly = await Client(h.SecondDoctor).PostAsJsonAsync($"/v1/appointments/{visit.Id}/status", new { state = "ReadyForDoctor" });
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);

        // One event per attempt. Moves are audited by the use case on the test's stopped clock, so they are compared as a set.
        Assert.Equal(["appointment.check_in", "appointment.check_in", "appointment.status", "appointment.status", "appointment.status", "appointment.status", "appointment.status"],
            (await AuditActions()).Where(a => a.StartsWith("appointment.", StringComparison.Ordinal) && a != "appointment.book").Order(StringComparer.Ordinal));
    }

    // SAFETY (MDV-P2-046), mutation checks: a patient field added to TokenSlip fails the field list; dropping the void-state refusal
    // in TokenSlipAsync reprints the left visit's token and fails the 409.
    [Fact]
    public async Task TheTokenSlipCarriesClinicTokenDoctorAndTimeOnlyAndAVoidTokenIsRefused()
    {
        var visit = await h.BookAsync(9);
        var booked = await h.BookAsync(10);
        var reception = Client(h.Reception);
        using var checkIn = await reception.PostAsJsonAsync($"/v1/appointments/{visit.Id}/check-in", new { elapsedMs = 9000 });
        var token = JsonDocument.Parse(await checkIn.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString();

        using var slip = await reception.GetAsync($"/v1/appointments/{visit.Id}/token-slip");
        Assert.Equal(HttpStatusCode.OK, slip.StatusCode);
        var body = await slip.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        // Exactly these fields: the slip leaves the clinic in the patient's hand, so a name, MRN, phone or reason must never join them.
        Assert.Equal(["clinicName", "token", "doctorName", "issuedAt", "timeZone"], json.EnumerateObject().Select(p => p.Name));
        Assert.Equal(token, json.GetProperty("token").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("doctorName").GetString()));
        Assert.DoesNotContain("Meera", body, StringComparison.Ordinal);
        Assert.DoesNotContain("000001", body, StringComparison.Ordinal);
        Assert.Equal(body, await reception.GetStringAsync($"/v1/appointments/{visit.Id}/token-slip")); // a reprint is the same token

        async Task<(HttpStatusCode, string?)> Refused(Guid id, HttpClient client)
        {
            using var r = await client.GetAsync($"/v1/appointments/{id}/token-slip");
            var text = await r.Content.ReadAsStringAsync();
            return (r.StatusCode, r.StatusCode == HttpStatusCode.Conflict ? JsonDocument.Parse(text).RootElement.GetProperty("code").GetString() : null);
        }
        Assert.Equal((HttpStatusCode.Conflict, "no_token"), await Refused(booked.Id, reception)); // not checked in
        Assert.Equal((HttpStatusCode.Forbidden, (string?)null), await Refused(visit.Id, Client(h.PortalUser)));
        Assert.Equal((HttpStatusCode.NotFound, (string?)null), await Refused(visit.Id, Client(other.Reception, other.Tenant)));

        using var left = await reception.PostAsJsonAsync($"/v1/appointments/{visit.Id}/left", new { reason = "PatientUnwell" });
        Assert.Equal(HttpStatusCode.OK, left.StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, "token_void"), await Refused(visit.Id, reception));
        Assert.Contains("appointment.token_slip.read", await AuditActions());
    }
}

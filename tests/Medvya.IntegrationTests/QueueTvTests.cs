using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Medvya.Audit.Domain;
using Medvya.Scheduling.Api;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-031 on real SQL Server: check-in issues "A-01"-style tokens, never twice; a waiting-room TV signs in with a revocable
/// display token of one facility (never a staff session), sees tokens, rooms, doctor names and waits only, and hears a call-in
/// within 2 seconds through an empty push.
/// </summary>
public sealed class QueueTvTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql), other = new(sql);
    readonly BoardHubTests.SignedInDesks desks = new();
    WebApplicationFactory<Program> factory = null!;
    readonly List<HubConnection> connections = [];

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await other.SeedAsync();
        foreach (var user in new[] { h.Reception, h.Admin }) await desks.SignInAsync(sql, h.Tenant, user, h.Facility);
        await desks.SignInAsync(sql, h.Tenant, h.Elsewhere, h.OtherFacility);
        await desks.SignInAsync(sql, other.Tenant, other.Admin, other.Facility);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .ConfigureTestServices(s => s.AddSingleton<TimeProvider>(h.Clock).AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(desks)));
    }

    public async Task DisposeAsync()
    {
        foreach (var c in connections) await c.DisposeAsync();
        await factory.DisposeAsync();
        await h.CloseAsync();
        await other.CloseAsync();
    }

    HttpClient Staff(Guid user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        return client;
    }

    /// <summary>A browser on the TV: keeps cookies, no staff session.</summary>
    HttpClient Tv() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    async Task<(Guid Id, string Token)> NewDisplay(Guid admin, Guid facility, string name = "Waiting room TV (synthetic)")
    {
        using var response = await Staff(admin).PostAsJsonAsync("/v1/queue/displays/", new { facilityId = facility, name });
        var json = JsonDocument.Parse(await response.EnsureSuccessStatusCode().Content.ReadAsStringAsync()).RootElement;
        return (json.GetProperty("display").GetProperty("id").GetGuid(), json.GetProperty("token").GetString()!);
    }

    /// <summary>A TV paired with the display token; the cookie it got, for clients that do not keep cookies (SignalR).</summary>
    async Task<(HttpClient Tv, string Cookie)> Paired(string token)
    {
        var tv = Tv();
        using var response = await tv.PostAsJsonAsync("/v1/queue/tv/session", new { token });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        return (tv, cookie.Split(';')[0]);
    }

    async Task Move(Guid visit, string state) =>
        (await Staff(h.Reception).PostAsJsonAsync($"/v1/appointments/{visit}/status", new { state })).EnsureSuccessStatusCode();

    async Task CheckIn(Guid visit) => (await Staff(h.Reception).PostAsync($"/v1/appointments/{visit}/check-in", null)).EnsureSuccessStatusCode();

    [Fact]
    public async Task TheTvShowsTokensRoomsDoctorsAndWaitsOnly()
    {
        var (first, second, third) = (await h.BookAsync(9), await h.BookAsync(9, 20), await h.BookAsync(9, 40));
        foreach (var v in new[] { first, second, third }) await CheckIn(v.Id);
        foreach (var state in new[] { "WithNurse", "ReadyForDoctor", "InConsult" }) await Move(first.Id, state); // called in
        var (tv, _) = await Paired((await NewDisplay(h.Admin, h.Facility)).Token);

        using var response = await tv.GetAsync("/v1/queue/tv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        // Exactly these fields: adding one (a name, initials, an MRN, a reason) must be a reviewed change here.
        Assert.Equal(["now", "next"], json.EnumerateObject().Select(p => p.Name));
        var now = Assert.Single(json.GetProperty("now").EnumerateArray());
        Assert.Equal(["token", "room", "doctor"], now.EnumerateObject().Select(p => p.Name));
        Assert.Equal(("A-01", "Room 3", "Dr. Arjun Rao (synthetic)"), (now.GetProperty("token").GetString(), now.GetProperty("room").GetString(), now.GetProperty("doctor").GetString()));
        var next = json.GetProperty("next").EnumerateArray().ToList();
        Assert.All(next, n => Assert.Equal(["token", "waitMinutes"], n.EnumerateObject().Select(p => p.Name)));
        // Dr. Rao is busy with A-01 and sees patients every 20 minutes: A-02 in about 20, A-03 in about 40.
        Assert.Equal([("A-02", 20), ("A-03", 40)], next.Select(n => (n.GetProperty("token").GetString(), n.GetProperty("waitMinutes").GetInt32())));
        foreach (var leak in new[] { "Meera", "Iyer", "Deepak", "000001", "chest", "1972", h.Patient.ToString(), first.Id.ToString() })
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckInIssuesOneTokenPerVisitAndNeverTheSameNumberTwice()
    {
        var visits = new List<Guid>();
        for (var i = 0; i < 8; i++) visits.Add((await h.BookAsync(9, 0, start: SchedulingHarness.At(9).AddMinutes(20 * i))).Id);
        var desksAtOnce = await h.AddReceptionistsAsync(visits.Count);
        await Task.WhenAll(visits.Select((v, i) => h.FrontDesk(desksAtOnce[i]).CheckInAsync(v, new CheckIn())));
        var again = await h.FrontDesk(h.Reception).CheckInAsync(visits[0], new CheckIn()); // a repeat issues nothing

        await using var db = sql.Scheduling(h.Tenant);
        var tokens = await db.QueueTokens.Select(t => QueueToken.Format(t.Prefix, t.Number)).ToListAsync();
        Assert.Equal(Enumerable.Range(1, 8).Select(n => $"A-{n:00}"), tokens.Order(StringComparer.Ordinal));
        Assert.StartsWith("A-0", again.Token, StringComparison.Ordinal); // the card shows the visit's token
    }

    [Fact]
    public void PrefixesRunAToZThenTwoLetters()
    {
        int[] indexes = [0, 1, 25, 26, 27, 51, 52];
        Assert.Equal(["A", "B", "Z", "AA", "AB", "AZ", "BA"], indexes.Select(QueueToken.PrefixAt));
        Assert.Equal("C-07", QueueToken.Format("C", 7));
    }

    [Fact]
    public async Task OnlyAPairedDisplayReadsTheTvAndOnlyItsOwnFacility()
    {
        var visit = await h.BookAsync(9);
        await CheckIn(visit.Id);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Tv().GetAsync("/v1/queue/tv")).StatusCode); // nobody
        Assert.Equal(HttpStatusCode.Unauthorized, (await Staff(h.Admin).GetAsync("/v1/queue/tv")).StatusCode); // a staff session is not a display
        using (var badToken = await Tv().PostAsJsonAsync("/v1/queue/tv/session", new { token = "not-a-display-token" }))
            Assert.Equal(HttpStatusCode.Unauthorized, badToken.StatusCode);
        var (id, token) = await NewDisplay(h.Admin, h.Facility);
        // The same secret claimed for another tenant is another hash: refused.
        var forged = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + new string('=', (4 - token.Length % 4) % 4));
        other.Tenant.TryWriteBytes(forged);
        using (var forgedPair = await Tv().PostAsJsonAsync("/v1/queue/tv/session", new { token = Convert.ToBase64String(forged).TrimEnd('=').Replace('+', '-').Replace('/', '_') }))
            Assert.Equal(HttpStatusCode.Unauthorized, forgedPair.StatusCode);

        var (tv, _) = await Paired(token);
        Assert.Equal("A-01", JsonDocument.Parse(await tv.GetStringAsync("/v1/queue/tv")).RootElement.GetProperty("next")[0].GetProperty("token").GetString());
        // The display is not staff: staff endpoints refuse it.
        Assert.Equal(HttpStatusCode.Unauthorized, (await tv.GetAsync($"/v1/front-desk/board?facilityId={h.Facility}")).StatusCode);

        // Another facility's TV in this clinic, and another clinic's TV, see none of this facility's queue.
        var (elsewhereTv, _) = await Paired((await NewDisplay(h.Elsewhere, h.OtherFacility)).Token);
        var (otherTv, _) = await Paired((await NewDisplay(other.Admin, other.Facility)).Token);
        foreach (var foreign in new[] { elsewhereTv, otherTv })
            Assert.Equal("""{"now":[],"next":[]}""", await foreign.GetStringAsync("/v1/queue/tv"));

        // Revoked: the very next request is refused.
        Assert.Equal(HttpStatusCode.NoContent, (await Staff(h.Admin).DeleteAsync($"/v1/queue/displays/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tv.GetAsync("/v1/queue/tv")).StatusCode);
        using (var repair = await Tv().PostAsJsonAsync("/v1/queue/tv/session", new { token }))
            Assert.Equal(HttpStatusCode.Unauthorized, repair.StatusCode);
    }

    async Task<IEnumerable<string>> PairHeaders(string token)
    {
        using var response = await Tv().PostAsJsonAsync("/v1/queue/tv/session", new { token });
        return response.Headers.GetValues("Set-Cookie");
    }

    [Fact]
    public async Task TheDisplayCookieIsHttpOnlySecureStrictAndLimitedToTheQueue()
    {
        var (_, token) = await NewDisplay(h.Admin, h.Facility);
        var cookie = Assert.Single(await PairHeaders(token)).ToLowerInvariant();
        foreach (var attribute in new[] { "httponly", "secure", "samesite=strict", "path=/v1/queue" }) Assert.Contains(attribute, cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyStaffWhoConfigureTheFacilityManageDisplaysAndEveryChangeIsAudited()
    {
        using (var reception = await Staff(h.Reception).PostAsJsonAsync("/v1/queue/displays/", new { facilityId = h.Facility, name = "TV (synthetic)" }))
            Assert.Equal(HttpStatusCode.Forbidden, reception.StatusCode);
        using (var elsewhere = await Staff(h.Elsewhere).PostAsJsonAsync("/v1/queue/displays/", new { facilityId = h.Facility, name = "TV (synthetic)" }))
            Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode); // an admin of another facility
        var (id, token) = await NewDisplay(h.Admin, h.Facility);
        await Paired(token);
        Assert.Equal(HttpStatusCode.NoContent, (await Staff(h.Admin).DeleteAsync($"/v1/queue/displays/{id}")).StatusCode);
        var listed = JsonDocument.Parse(await Staff(h.Admin).GetStringAsync($"/v1/queue/displays/?facilityId={h.Facility}")).RootElement;
        Assert.Equal(["id", "facilityId", "name", "createdAt", "revokedAt"], listed[0].EnumerateObject().Select(p => p.Name)); // never the token
        Assert.NotEqual(JsonValueKind.Null, listed[0].GetProperty("revokedAt").ValueKind);

        await using var audit = sql.Audit(h.Tenant);
        var rows = (await audit.Events.Where(e => e.ResourceType == SchedulingAudit.DisplayResource)
            .Select(e => new { e.Action, e.Outcome, e.ActorUserId }).ToListAsync()).Select(r => $"{r.Action} {r.Outcome} {r.ActorUserId}");
        // One row per attempt (use-case rows carry the test's clock, filter rows the real one, so they are compared as a set).
        string[] expected =
        [
            $"{SchedulingAudit.DisplayCreate} Denied {h.Reception}", $"{SchedulingAudit.DisplayCreate} Denied {h.Elsewhere}",
            $"{SchedulingAudit.DisplayCreate} Allowed {h.Admin}", $"{SchedulingAudit.DisplayPair} Allowed ", $"{SchedulingAudit.DisplayRevoke} Allowed {h.Admin}",
            $"queue_display.read Allowed {h.Admin}",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), rows.Order(StringComparer.Ordinal));
    }

    /// <summary>A TV's live connection with its display cookie; the push arrives with no arguments or it is not delivered at all.</summary>
    async Task<(HubConnection Connection, Channel<int> Pushes)> Live(string? cookie, HttpTransportType transport = HttpTransportType.LongPolling, Guid? staff = null)
    {
        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, QueueHub.Path), transport, o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.WebSocketFactory = async (context, ct) =>
                {
                    var ws = server.CreateWebSocketClient();
                    ws.ConfigureRequest = r =>
                    {
                        if (cookie is not null) r.Headers["Cookie"] = cookie;
                        if (staff is { } s) r.Headers["X-Test-User"] = s.ToString();
                    };
                    return await ws.ConnectAsync(context.Uri, ct);
                };
                if (cookie is not null) o.Headers["Cookie"] = cookie;
                if (staff is { } u) o.Headers["X-Test-User"] = u.ToString();
            })
            .Build();
        connections.Add(connection);
        var pushes = Channel.CreateUnbounded<int>();
        connection.On(QueueHub.Changed, [], args => { pushes.Writer.TryWrite(args.Length); return Task.CompletedTask; });
        await connection.StartAsync();
        await connection.InvokeAsync(nameof(QueueHub.Watch));
        return (connection, pushes);
    }

    [Theory]
    [InlineData(HttpTransportType.LongPolling)]
    [InlineData(HttpTransportType.WebSockets)]
    public async Task ACallInReachesTheTvWithinTwoSecondsAsAnEmptyPush(HttpTransportType transport)
    {
        var visit = await h.BookAsync(9);
        await CheckIn(visit.Id);
        await Move(visit.Id, "WithNurse");
        await Move(visit.Id, "ReadyForDoctor");
        var (_, cookie) = await Paired((await NewDisplay(h.Admin, h.Facility)).Token);
        var (_, pushes) = await Live(cookie, transport);
        var (_, elsewhereCookie) = await Paired((await NewDisplay(h.Elsewhere, h.OtherFacility)).Token);
        var (_, elsewherePushes) = await Live(elsewhereCookie, transport);

        var clock = Stopwatch.StartNew();
        await Move(visit.Id, "InConsult");
        using var within = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await pushes.Reader.ReadAsync(within.Token)); // no arguments: the TV refetches its tokens-only queue
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");
        using var quiet = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => elsewherePushes.Reader.ReadAsync(quiet.Token).AsTask()); // another facility hears nothing
    }

    [Fact]
    public async Task TheLiveQueueRefusesStaffSessionsAndRevokedDisplays()
    {
        var anonymous = await Assert.ThrowsAsync<HttpRequestException>(() => Live(null));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var staff = await Assert.ThrowsAsync<HttpRequestException>(() => Live(null, staff: h.Admin));
        Assert.Equal(HttpStatusCode.Unauthorized, staff.StatusCode);
        var (id, token) = await NewDisplay(h.Admin, h.Facility);
        var (_, cookie) = await Paired(token);
        await Staff(h.Admin).DeleteAsync($"/v1/queue/displays/{id}");
        var revoked = await Assert.ThrowsAsync<HttpRequestException>(() => Live(cookie));
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public void TheGroupNameIsOpaqueAndIncludesTheTenant()
    {
        Assert.Equal($"queue:{h.Tenant:N}:{h.Facility:N}", QueueGroups.Of(h.Tenant, h.Facility));
        Assert.NotEqual(QueueGroups.Of(h.Tenant, h.Facility), BoardGroups.Of(h.Tenant, h.Facility)); // TVs never join a staff board's group
    }
}

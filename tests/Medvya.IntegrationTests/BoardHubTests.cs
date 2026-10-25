using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using Medvya.Audit.Domain;
using Medvya.Identity.Domain;
using Medvya.Scheduling.Api;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-029 on real SQL Server through a real SignalR client: a change on one desk reaches another desk's board within
/// 2 seconds as ids and state only; connecting needs board access at the session's facility and a refusal is audited; every
/// call is checked again (live session, live membership, only the session's own facility) and audited Allowed or Denied; and
/// no connection ever hears another facility's or another tenant's board.
/// </summary>
public sealed class BoardHubTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly SchedulingHarness h = new(sql), other = new(sql);
    readonly SignedInDesks desks = new();
    WebApplicationFactory<Program> factory = null!;
    readonly List<HubConnection> connections = [];

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.DoctorTemplateAsync();
        await other.SeedAsync(); // a second clinic in its own tenant
        // Elsewhere also works reception here, but is signed in to the other facility: a session sees only its own facility's board.
        await using (var tenancy = sql.Context(h.Tenant))
        {
            tenancy.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = h.Tenant, UserId = h.Elsewhere, FacilityId = h.Facility, Role = Role.Receptionist, Status = MembershipStatus.Active });
            await tenancy.SaveChangesAsync();
        }
        foreach (var user in new[] { h.Reception, h.Nurse, h.PortalUser }) await desks.SignInAsync(sql, h.Tenant, user, h.Facility);
        await desks.SignInAsync(sql, h.Tenant, h.Elsewhere, h.OtherFacility);
        await desks.SignInAsync(sql, other.Tenant, other.Reception, other.Facility);
        factory = Host();
    }

    WebApplicationFactory<Program> Host(Action<IServiceCollection>? services = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("RateLimiting:BoardWatchPerMinute", "3")
            .ConfigureTestServices(s =>
            {
                s.AddSingleton<TimeProvider>(h.Clock).AddSingleton<IStartupFilter>(desks);
                services?.Invoke(s);
            }));

    public async Task DisposeAsync()
    {
        foreach (var c in connections) await c.DisposeAsync();
        await factory.DisposeAsync();
        await h.CloseAsync();
        await other.CloseAsync();
    }

    /// <summary>
    /// Each synthetic desk signed in with a real server-side session scoped to one membership, as the facility picker leaves it:
    /// the claims the session cookie would carry, set from X-Test-User (no cookie in tests).
    /// </summary>
    internal sealed class SignedInDesks : IStartupFilter
    {
        readonly Dictionary<Guid, Claim[]> claims = [];
        public readonly Dictionary<Guid, Guid> Sessions = [], Memberships = [];

        public async Task SignInAsync(SqlServerFixture sql, Guid tenant, Guid user, Guid facility)
        {
            await using var tenancy = sql.Context(tenant);
            var membership = await tenancy.Memberships.Where(m => m.UserId == user && m.FacilityId == facility).Select(m => m.Id).SingleAsync();
            await using var identity = sql.Identity();
            if (!await identity.Users.AnyAsync(u => u.Id == user))
                identity.Users.Add(new StaffUser { Id = user, Issuer = "test-idp", Subject = user.ToString(), DisplayName = "Desk (synthetic)" });
            var now = DateTimeOffset.UtcNow;
            var session = new StaffSession
            {
                Id = Guid.CreateVersion7(), UserId = user, TenantId = tenant, MembershipId = membership, IdleTimeoutMinutes = 60, Device = "Test desk", CreatedAt = now, LastSeenAt = now,
            };
            identity.Sessions.Add(session);
            await identity.SaveChangesAsync();
            (Sessions[user], Memberships[user]) = (session.Id, membership);
            claims[user] =
            [
                new(ClaimTypes.NameIdentifier, user.ToString()), new("sid", session.Id.ToString()), new("tenant_id", tenant.ToString()),
                new("membership_id", membership.ToString()), new("facility_id", facility.ToString()),
            ];
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                if (Guid.TryParse(ctx.Request.Headers["X-Test-User"].ToString(), out var user) && claims.TryGetValue(user, out var c))
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(c, "synthetic"));
                return nxt(ctx);
            });
            next(app);
        };
    }

    /// <summary>A board connection as <paramref name="user"/>, whose raw messages land in the channel.</summary>
    async Task<(HubConnection Connection, Channel<JsonElement> Messages)> Board(Guid? user, HttpTransportType transport = HttpTransportType.LongPolling, WebApplicationFactory<Program>? on = null)
    {
        var server = (on ?? factory).Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, BoardHub.Path), transport, o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.WebSocketFactory = async (context, ct) =>
                {
                    var ws = server.CreateWebSocketClient();
                    ws.ConfigureRequest = r => { if (user is { } u) r.Headers["X-Test-User"] = u.ToString(); };
                    return await ws.ConnectAsync(context.Uri, ct);
                };
                if (user is { } id) o.Headers["X-Test-User"] = id.ToString();
            })
            .Build();
        connections.Add(connection);
        var messages = Channel.CreateUnbounded<JsonElement>();
        connection.On<JsonElement>(nameof(BoardChanged), m => messages.Writer.TryWrite(m));
        await connection.StartAsync();
        return (connection, messages);
    }

    HttpClient Desk(Guid user, WebApplicationFactory<Program>? on = null)
    {
        var client = (on ?? factory).CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        return client;
    }

    static async Task<JsonElement?> Next(Channel<JsonElement> messages, TimeSpan within)
    {
        using var timeout = new CancellationTokenSource(within);
        try
        {
            return await messages.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    async Task<List<(string Action, AuditOutcome Outcome, Guid? Actor)>> HubAudit(Guid tenant)
    {
        await using var db = sql.Audit(tenant);
        return (await db.Events.Where(e => e.Action.StartsWith("front_desk.board.") && e.Action != "front_desk.board.read")
            .OrderBy(e => e.OccurredAt).Select(e => new { e.Action, e.Outcome, e.ActorUserId }).ToListAsync())
            .Select(e => (e.Action, e.Outcome, e.ActorUserId)).ToList();
    }

    [Theory]
    [InlineData(HttpTransportType.LongPolling)]
    [InlineData(HttpTransportType.WebSockets)]
    public async Task ACheckInOnOneDeskReachesAnotherBoardWithinTwoSecondsAsIdsAndStateOnly(HttpTransportType transport)
    {
        var visit = await h.BookAsync(9);
        var (board, messages) = await Board(h.Nurse, transport);
        await board.InvokeAsync(nameof(BoardHub.Watch), h.Facility);

        var clock = Stopwatch.StartNew();
        using var response = await Desk(h.Reception).PostAsJsonAsync($"/v1/appointments/{visit.Id}/check-in", new { elapsedMs = 8000 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var message = await Next(messages, TimeSpan.FromSeconds(2));
        clock.Stop();

        Assert.NotNull(message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"took {clock.Elapsed}");
        // Exactly these fields: no name, reason, room or anything else a message could leak. Adding one is a reviewed change here.
        Assert.Equal(["appointmentId", "state", "changedAt"], message.Value.EnumerateObject().Select(p => p.Name));
        Assert.Equal((visit.Id, "CheckedIn"), (message.Value.GetProperty("appointmentId").GetGuid(), message.Value.GetProperty("state").GetString()));
        Assert.DoesNotContain("Meera", message.Value.GetRawText(), StringComparison.Ordinal);
        // The joining call was audited, as the hub's own row (the check-in has its own).
        Assert.Contains((BoardHub.WatchAction, AuditOutcome.Allowed, (Guid?)h.Nurse), await HubAudit(h.Tenant));
    }

    [Fact]
    public async Task ABookingForTodayAndACallInReachTheBoardToo()
    {
        var (board, messages) = await Board(h.Reception);
        await board.InvokeAsync(nameof(BoardHub.Watch), h.Facility);

        // Booked at the desk through the API, as the calendar does: a new card on today's board.
        var desk = Desk(h.Reception);
        using var hold = await desk.PostAsJsonAsync("/v1/slot-holds/", new { facilityId = h.Facility, calendarId = h.Doctor, visitType = "FollowUp", start = SchedulingHarness.At(9) });
        var holdId = JsonDocument.Parse(await hold.EnsureSuccessStatusCode().Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        using var booked = await desk.PostAsJsonAsync("/v1/appointments/", new { holdId, patientId = h.Patient, reasonCategory = "ChronicReview", channel = "Phone" });
        var visit = new { Id = JsonDocument.Parse(await booked.EnsureSuccessStatusCode().Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid() };
        Assert.Equal("Booked", (await Next(messages, TimeSpan.FromSeconds(2)))?.GetProperty("state").GetString());

        (await desk.PostAsync($"/v1/appointments/{visit.Id}/check-in", null)).EnsureSuccessStatusCode();
        foreach (var state in new[] { "WithNurse", "ReadyForDoctor", "InConsult" })
            (await desk.PostAsJsonAsync($"/v1/appointments/{visit.Id}/status", new { state })).EnsureSuccessStatusCode();
        var states = new List<string?>();
        for (var i = 0; i < 4; i++) states.Add((await Next(messages, TimeSpan.FromSeconds(2)))?.GetProperty("state").GetString());
        Assert.Equal(["CheckedIn", "WithNurse", "ReadyForDoctor", "InConsult"], states);
        // A refused move changes nothing and tells no board.
        using var skip = await desk.PostAsJsonAsync($"/v1/appointments/{visit.Id}/status", new { state = "Done" });
        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);
        Assert.Null(await Next(messages, TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task ConnectingNeedsBoardAccessAtTheSessionsFacilityAndARefusalIsAudited()
    {
        var anonymous = await Assert.ThrowsAsync<HttpRequestException>(() => Board(null));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var portal = await Assert.ThrowsAsync<HttpRequestException>(() => Board(h.PortalUser)); // a patient role: no board access
        Assert.Equal(HttpStatusCode.Forbidden, portal.StatusCode);

        Assert.Equal([(BoardHub.ConnectAction, AuditOutcome.Denied, (Guid?)h.PortalUser)], await HubAudit(h.Tenant));
    }

    [Fact]
    public async Task AConnectionWatchesOnlyItsSessionsFacilityAndNobodyElseHearsIt()
    {
        var visit = await h.BookAsync(9);
        // Reception here who is signed in to the clinic's other facility, and reception of another clinic (another tenant).
        var (elsewhere, elsewhereMessages) = await Board(h.Elsewhere);
        var (otherClinic, otherMessages) = await Board(other.Reception);
        foreach (var refused in new[] { elsewhere, otherClinic })
        {
            var e = await Assert.ThrowsAsync<HubException>(() => refused.InvokeAsync(nameof(BoardHub.Watch), h.Facility));
            Assert.Contains(BoardHub.Refused, e.Message, StringComparison.Ordinal);
        }
        // Each watches its own board: allowed, and must never hear about this facility.
        await elsewhere.InvokeAsync(nameof(BoardHub.Watch), h.OtherFacility);
        await otherClinic.InvokeAsync(nameof(BoardHub.Watch), other.Facility);
        var (own, ownMessages) = await Board(h.Reception);
        await own.InvokeAsync(nameof(BoardHub.Watch), h.Facility);

        (await Desk(h.Reception).PostAsync($"/v1/appointments/{visit.Id}/check-in", null)).EnsureSuccessStatusCode();

        Assert.NotNull(await Next(ownMessages, TimeSpan.FromSeconds(2)));
        Assert.Null(await Next(elsewhereMessages, TimeSpan.FromMilliseconds(500)));
        Assert.Null(await Next(otherMessages, TimeSpan.FromMilliseconds(100)));
        // One row per call, in the caller's own tenant: refused Denied, joined Allowed.
        Assert.Equal(
            [(BoardHub.WatchAction, AuditOutcome.Denied, h.Elsewhere), (BoardHub.WatchAction, AuditOutcome.Allowed, h.Elsewhere), (BoardHub.WatchAction, AuditOutcome.Allowed, h.Reception)],
            await HubAudit(h.Tenant));
        Assert.Equal([(BoardHub.WatchAction, AuditOutcome.Denied, other.Reception), (BoardHub.WatchAction, AuditOutcome.Allowed, other.Reception)], await HubAudit(other.Tenant));
    }

    [Fact]
    public async Task EveryCallIsCheckedAgainAgainstTheLiveMembershipAndSession()
    {
        // WebSockets: one HTTP request for the whole connection, so only the hub's own checks stand between a call and the board
        // (long polling also re-runs the endpoint's capability policy on every poll).
        var (nurse, _) = await Board(h.Nurse, HttpTransportType.WebSockets);
        await nurse.InvokeAsync(nameof(BoardHub.Watch), h.Facility);

        // Suspended while connected: the next call is refused and audited.
        await using (var tenancy = sql.Context(h.Tenant))
            await tenancy.Memberships.Where(m => m.Id == desks.Memberships[h.Nurse]).ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, MembershipStatus.Suspended));
        var suspended = await Assert.ThrowsAsync<HubException>(() => nurse.InvokeAsync(nameof(BoardHub.Watch), h.Facility));
        Assert.Contains(BoardHub.Refused, suspended.Message, StringComparison.Ordinal);
        Assert.Equal((BoardHub.WatchAction, AuditOutcome.Denied, (Guid?)h.Nurse), (await HubAudit(h.Tenant))[^1]);

        // Session revoked while connected: the next call ends the connection.
        var (reception, _) = await Board(h.Reception, HttpTransportType.WebSockets);
        var closed = new TaskCompletionSource();
        reception.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        await using (var identity = sql.Identity())
            await identity.Sessions.Where(s => s.Id == desks.Sessions[h.Reception]).ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow));
        await Assert.ThrowsAnyAsync<Exception>(() => reception.InvokeAsync(nameof(BoardHub.Watch), h.Facility));
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HubConnectionState.Disconnected, reception.State);
    }

    [Fact]
    public async Task WatchingIsLimitedPerUser()
    {
        var (board, _) = await Board(h.Reception);
        for (var i = 0; i < 3; i++) await board.InvokeAsync(nameof(BoardHub.Watch), h.Facility); // the test host allows 3 a minute
        var e = await Assert.ThrowsAsync<HubException>(() => board.InvokeAsync(nameof(BoardHub.Watch), h.Facility));
        Assert.Contains("Too many requests.", e.Message, StringComparison.Ordinal);
        Assert.Equal(3, (await HubAudit(h.Tenant)).Count); // a limited call reads nothing and is not counted as an access
    }

    [Fact]
    public async Task AFailedPushNeverFailsTheCommittedMove()
    {
        await using var failing = Host(s => s.Replace(ServiceDescriptor.Singleton<IBoardNotifier, FailingNotifier>()));
        var visit = await h.BookAsync(9);
        using var response = await Desk(h.Reception, failing).PostAsync($"/v1/appointments/{visit.Id}/check-in", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    sealed class FailingNotifier : IBoardNotifier
    {
        public Task ChangedAsync(BoardChange change, CancellationToken cancellationToken = default) => throw new InvalidOperationException("push failed (synthetic)");
    }

    [Fact]
    public void TheGroupNameIsOpaqueAndIncludesTheTenant()
    {
        var group = BoardGroups.Of(h.Tenant, h.Facility);
        Assert.Equal($"board:{h.Tenant:N}:{h.Facility:N}", group);
        Assert.NotEqual(group, BoardGroups.Of(other.Tenant, h.Facility)); // the same facility id in another tenant is another group
    }
}

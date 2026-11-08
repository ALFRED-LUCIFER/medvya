extern alias platform;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Medvya.Platform.Api;
using Medvya.Platform.Domain;
using Medvya.Platform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ConsoleProgram = platform::Program;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P5-040: the platform console shell. Company sign-on then a security key; every action needs a reason and is logged to an
/// append-only audit log; a lost key is recovered by another owner with a one-hour link; the console's login reads no clinic data.
/// </summary>
public sealed class PlatformConsoleTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IDisposable
{
    internal const string Origin = "https://localhost";
    readonly WebApplicationFactory<ConsoleProgram> factory = CreateFactory(sql);

    internal static WebApplicationFactory<ConsoleProgram> CreateFactory(PostgresFixture sql, int signInPerMinute = 20)
    {
        WebApplicationFactory<ConsoleProgram> factory = null!;
        factory = new WebApplicationFactory<ConsoleProgram>().WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("ConnectionStrings:Platform", sql.As("medvya_platform"))
            .UseSetting("Authentication:Oidc:Authority", "http://localhost/dev-idp")
            .UseSetting("Platform:Origins:0", Origin)
            .UseSetting("RateLimiting:SignInPerMinute", signInPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ConfigureServices(s => s.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme,
                o => o.BackchannelHttpHandler = factory.Server.CreateHandler())));
        return factory;
    }

    public void Dispose() => factory.Dispose();

    internal static HttpClient Browser(WebApplicationFactory<ConsoleProgram> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(Origin), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Medvya-Csrf", "1");
        return client;
    }

    HttpClient Browser() => Browser(factory);

    Task<(PlatformMember Member, SoftSecurityKey Key)> Member(PlatformRole role, bool withKey = true) => Seed(sql, role, withKey);

    /// <summary>A member bound to a dev company account, with a registered software key unless <paramref name="withKey"/> is false.</summary>
    internal static async Task<(PlatformMember Member, SoftSecurityKey Key)> Seed(PostgresFixture sql, PlatformRole role, bool withKey = true)
    {
        var soft = new SoftSecurityKey(Origin);
        var member = new PlatformMember
        {
            DisplayName = $"{role} {Guid.NewGuid().ToString("N")[..6]} (synthetic)", Email = $"{Guid.NewGuid():N}@medvya.example", Role = role,
            Issuer = ConsoleProgram.DevCompanyIssuer, Subject = Guid.NewGuid().ToString(), InvitedAt = DateTimeOffset.UtcNow,
            InvitedBy = Guid.Empty,
        };
        await using var db = Db(sql);
        db.Members.Add(member);
        if (withKey) db.Keys.Add(new SecurityKey { MemberId = member.Id, CredentialId = soft.CredentialId, PublicKey = soft.CosePublicKey(), CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return (member, soft);
    }

    PlatformDbContext Db() => Db(sql);

    internal static PlatformDbContext Db(PostgresFixture sql) => new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(sql.As("medvya_platform")).Options);

    /// <summary>Company sign-on through the dev provider; returns the callback response.</summary>
    internal static async Task<HttpResponseMessage> SignOn(HttpClient browser, string subject, string? link = null)
    {
        var challenge = await browser.GetAsync(new Uri(link is null ? "/bff/login" : $"/bff/login?link={link}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var authorize = await browser.GetAsync(new Uri($"{challenge.Headers.Location}&login_hint={Uri.EscapeDataString(subject)}"));
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        return await browser.GetAsync(authorize.Headers.Location);
    }

    internal static async Task<HttpStatusCode> TouchKey(HttpClient browser, SoftSecurityKey key, Guid memberId)
    {
        var options = await browser.PostAsync(new Uri("/bff/key/check/options", UriKind.Relative), null);
        if (options.StatusCode != HttpStatusCode.OK) return options.StatusCode;
        var answer = key.Get(await options.Content.ReadAsStringAsync(), memberId);
        return (await browser.PostAsync(new Uri("/bff/key/check", UriKind.Relative), new StringContent(answer, Encoding.UTF8, "application/json"))).StatusCode;
    }

    internal static async Task<HttpStatusCode> RegisterKey(HttpClient browser, SoftSecurityKey key)
    {
        var options = await browser.PostAsync(new Uri("/bff/key/registration/options", UriKind.Relative), null);
        if (options.StatusCode != HttpStatusCode.OK) return options.StatusCode;
        var answer = key.Create(await options.Content.ReadAsStringAsync());
        return (await browser.PostAsync(new Uri("/bff/key/registration", UriKind.Relative), new StringContent(answer, Encoding.UTF8, "application/json"))).StatusCode;
    }

    Task<HttpClient> SignedIn(PlatformMember member, SoftSecurityKey key) => SignedIn(factory, member, key);

    internal static async Task<HttpClient> SignedIn(WebApplicationFactory<ConsoleProgram> factory, PlatformMember member, SoftSecurityKey key)
    {
        var browser = Browser(factory);
        await SignOn(browser, member.Subject!);
        Assert.Equal(HttpStatusCode.NoContent, await TouchKey(browser, key, member.Id));
        return browser;
    }

    static async Task<JsonElement> Me(HttpClient browser) => await browser.GetFromJsonAsync<JsonElement>(new Uri("/v1/me", UriKind.Relative));

    internal static Task<HttpResponseMessage> Post(HttpClient browser, string path, object body) => browser.PostAsJsonAsync(new Uri(path, UriKind.Relative), body);

    static async Task<List<JsonElement>> Audit(HttpClient owner) =>
        [.. (await owner.GetFromJsonAsync<JsonElement>(new Uri("/v1/audit?limit=100", UriKind.Relative))).EnumerateArray()];

    // Check 1: company sign-on alone opens nothing; neither does a key that does not verify its user, nor another member's key.
    [Fact]
    public async Task SigningOnWithoutTheSecurityKeyOpensNothing()
    {
        var (owner, key) = await Member(PlatformRole.Owner);
        var (_, otherKey) = await Member(PlatformRole.Owner);
        using var browser = Browser();
        var callback = await SignOn(browser, owner.Subject!);
        Assert.Equal("/", callback.Headers.Location!.OriginalString);
        Assert.False((await Me(browser)).GetProperty("keyVerified").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync(new Uri("/v1/team", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync(new Uri("/v1/audit", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(browser, "/v1/team/access-review", new { reason = "Quarterly review of console access" })).StatusCode);

        key.Verifies = false; // touched, but no PIN or biometrics
        Assert.Equal(HttpStatusCode.Unauthorized, await TouchKey(browser, key, owner.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, await TouchKey(browser, otherKey, owner.Id));
        // An answer to a challenge already used (or never asked) is refused too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsync(new Uri("/bff/key/check", UriKind.Relative),
            new StringContent(key.Get("""{"challenge":"AAAA"}""", owner.Id), Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync(new Uri("/v1/team", UriKind.Relative))).StatusCode);

        key.Verifies = true;
        Assert.Equal(HttpStatusCode.NoContent, await TouchKey(browser, key, owner.Id));
        Assert.True((await Me(browser)).GetProperty("keyVerified").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/v1/team", UriKind.Relative))).StatusCode);
        var actions = (await Audit(browser)).Where(a => a.GetProperty("actor").GetString() == owner.DisplayName).Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Contains("auth.signon", actions);
        Assert.Contains("auth.key.refused", actions);
        Assert.Contains("auth.signin", actions);
    }

    [Fact]
    public async Task ACompanyAccountThatIsNoMemberCannotSignOn()
    {
        await Member(PlatformRole.Owner); // the team is not empty, so nobody is taken for the first owner either
        using var browser = Browser();
        var callback = await SignOn(browser, "new");
        Assert.Equal("/sign-in?signin=failed", callback.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(new Uri("/v1/me", UriKind.Relative))).StatusCode);
    }

    // Check 2: a platform action without a reason is rejected before anything happens; with one, the reason is in the audit log.
    [Fact]
    public async Task AnActionWithoutAReasonIsRejected()
    {
        var (owner, key) = await Member(PlatformRole.Owner);
        using var browser = await SignedIn(owner, key);
        foreach (var body in new object[] { new { }, new { reason = "   " }, new { reason = "ok" }, new { reason = new string('x', 501) } })
        {
            var refused = await Post(browser, "/v1/team/access-review", body);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("reason", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        var invite = await Post(browser, "/v1/team/invitations", new { displayName = "Meera (synthetic)", email = "meera@medvya.example", role = "Support" });
        Assert.Equal(HttpStatusCode.BadRequest, invite.StatusCode);
        Assert.False(await Db().Members.AnyAsync(m => m.Email == "meera@medvya.example"));

        Assert.Equal(HttpStatusCode.NoContent, (await Post(browser, "/v1/team/access-review", new { reason = "  Quarterly review, all access still needed  " })).StatusCode);
        var entry = Assert.Single(await Audit(browser), a => a.GetProperty("action").GetString() == "team.access_review" && a.GetProperty("actor").GetString() == owner.DisplayName);
        Assert.Equal("Quarterly review, all access still needed", entry.GetProperty("reason").GetString());
    }

    [Fact]
    public void EveryConsoleWriteRequiresAReason()
    {
        _ = factory.CreateClient();
        var writes = factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/v1/", StringComparison.Ordinal)
                        && e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Any(m => m != "GET") == true).ToList();
        Assert.NotEmpty(writes);
        Assert.Empty(writes.Where(e => e.Metadata.GetMetadata<RequiresReasonMetadata>() is null).Select(e => e.RoutePattern.RawText));
    }

    [Fact]
    public void EveryConsoleEndpointDeclaresAPolicyOrIsExplicitlyPublic()
    {
        _ = factory.CreateClient();
        Assert.Empty(EndpointAuthorizationTests.Undeclared(factory.Services.GetServices<EndpointDataSource>()));
    }

    [Fact]
    public async Task AnInvitedMemberJoinsWithTheirLinkAndGetsOnlyTheirRole()
    {
        var (owner, ownerKey) = await Member(PlatformRole.Owner);
        using var ownerBrowser = await SignedIn(owner, ownerKey);
        var invite = await Post(ownerBrowser, "/v1/team/invitations",
            new { displayName = "Kavya (synthetic)", email = "Kavya.Reviewer@medvya.example", role = "Reviewer", reason = "New licence reviewer joining" });
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        var issued = await invite.Content.ReadFromJsonAsync<JsonElement>();
        var link = issued.GetProperty("token").GetString()!;

        using var browser = Browser();
        Assert.Equal("/", (await SignOn(browser, "new", link)).Headers.Location!.OriginalString);
        Assert.True((await Me(browser)).GetProperty("canRegister").GetBoolean());
        using var soft = new SoftSecurityKey(Origin);
        Assert.Equal(HttpStatusCode.NoContent, await RegisterKey(browser, soft));
        var me = await Me(browser);
        Assert.True(me.GetProperty("keyVerified").GetBoolean());
        Assert.Equal("Reviewer", me.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync(new Uri("/v1/team", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync(new Uri("/v1/audit", UriKind.Relative))).StatusCode);

        // The link is spent: nobody else can sign on with it.
        using var stranger = Browser();
        Assert.Equal("/sign-in?signin=failed", (await SignOn(stranger, "new", link)).Headers.Location!.OriginalString);
        var team = await ownerBrowser.GetFromJsonAsync<JsonElement>(new Uri("/v1/team", UriKind.Relative));
        var joined = team.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("email").GetString() == "kavya.reviewer@medvya.example");
        Assert.True(joined.GetProperty("hasKey").GetBoolean());
        var actions = (await Audit(ownerBrowser)).Select(a => (a.GetProperty("action").GetString(), a.GetProperty("actor").GetString())).ToList();
        Assert.Contains(("team.invite", owner.DisplayName), actions);
        Assert.Contains(("key.register", "Kavya (synthetic)"), actions);
    }

    [Fact]
    public async Task ASyncedPasskeyIsRefused()
    {
        var (owner, ownerKey) = await Member(PlatformRole.Owner);
        using var ownerBrowser = await SignedIn(owner, ownerKey);
        var link = (await (await Post(ownerBrowser, "/v1/team/invitations",
            new { displayName = "Ravi (synthetic)", email = "ravi@medvya.example", role = "Support", reason = "Support engineer joining" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        using var browser = Browser();
        await SignOn(browser, "new", link);
        using var synced = new SoftSecurityKey(Origin) { BackupEligible = true };
        Assert.Equal(HttpStatusCode.Unauthorized, await RegisterKey(browser, synced));
        Assert.False((await Me(browser)).GetProperty("keyVerified").GetBoolean());
    }

    // Check 4: another owner issues a one-hour link; the old key stops at once; the new key works; both steps are logged.
    [Fact]
    public async Task ALostKeyIsRecoveredByAnotherOwnerWithAOneHourLink()
    {
        var (owner, ownerKey) = await Member(PlatformRole.Owner);
        var (lost, oldKey) = await Member(PlatformRole.Operations);
        using var ownerBrowser = await SignedIn(owner, ownerKey);
        using var lostBrowser = await SignedIn(lost, oldKey);

        Assert.Equal(HttpStatusCode.Conflict, (await Post(ownerBrowser, $"/v1/team/{owner.Id}/recovery", new { reason = "Trying to recover my own key" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(ownerBrowser, $"/v1/team/{lost.Id}/recovery", new { })).StatusCode);
        var issued = await Post(ownerBrowser, $"/v1/team/{lost.Id}/recovery", new { reason = "Key lost; identity checked on a video call" });
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var body = await issued.Content.ReadFromJsonAsync<JsonElement>();
        var expires = body.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));

        // The old key and the session it opened stop working now, before any new key exists.
        Assert.Equal(HttpStatusCode.Unauthorized, (await lostBrowser.GetAsync(new Uri("/v1/me", UriKind.Relative))).StatusCode);
        using var finder = Browser();
        await SignOn(finder, lost.Subject!);
        Assert.Equal(HttpStatusCode.Conflict, await TouchKey(finder, oldKey, lost.Id));
        Assert.Equal(HttpStatusCode.Forbidden, await RegisterKey(finder, new SoftSecurityKey(Origin))); // no link, no new key

        // The link fits only the member it was issued for: another company account is refused with it.
        using var stranger = Browser();
        Assert.Equal("/sign-in?signin=failed", (await SignOn(stranger, "new", body.GetProperty("token").GetString())).Headers.Location!.OriginalString);

        using var recovering = Browser();
        await SignOn(recovering, lost.Subject!, body.GetProperty("token").GetString());
        using var newKey = new SoftSecurityKey(Origin);
        Assert.Equal(HttpStatusCode.NoContent, await RegisterKey(recovering, newKey));
        Assert.Equal(HttpStatusCode.OK, (await recovering.GetAsync(new Uri("/v1/audit", UriKind.Relative))).StatusCode);
        using var next = await SignedIn(lost, newKey);
        Assert.Equal(HttpStatusCode.Unauthorized, await TouchKey(next, oldKey, lost.Id));

        var log = (await Audit(ownerBrowser)).ToList();
        var recovery = Assert.Single(log, a => a.GetProperty("action").GetString() == "key.recovery.issue");
        Assert.Equal(owner.DisplayName, recovery.GetProperty("actor").GetString());
        Assert.Equal("Key lost; identity checked on a video call", recovery.GetProperty("reason").GetString());
        Assert.Equal(lost.Id, recovery.GetProperty("resourceId").GetGuid());
        Assert.Contains(log, a => a.GetProperty("action").GetString() == "key.register" && a.GetProperty("actor").GetString() == lost.DisplayName);
        await using var db = Db();
        Assert.NotNull((await db.Keys.SingleAsync(k => k.CredentialId == oldKey.CredentialId)).RevokedAt);
    }

    [Fact]
    public void AKeyLinkLastsOneHour()
    {
        var now = DateTimeOffset.UtcNow;
        var link = new KeyLink { TokenHash = new byte[32], IssuedAt = now, ExpiresAt = now + KeyLink.Lifetime };
        Assert.Equal(TimeSpan.FromHours(1), KeyLink.Lifetime);
        Assert.True(link.IsUsable(now.AddMinutes(59)));
        Assert.False(link.IsUsable(now.AddHours(1)));
        link.UsedAt = now;
        Assert.False(link.IsUsable(now.AddMinutes(1)));
    }

    [Fact]
    public async Task ARemovedMemberLosesAccessAtOnceAndTheLastOwnerStays()
    {
        var (owner, ownerKey) = await Member(PlatformRole.Owner);
        var (leaving, leavingKey) = await Member(PlatformRole.Finance);
        using var ownerBrowser = await SignedIn(owner, ownerKey);
        using var leavingBrowser = await SignedIn(leaving, leavingKey);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(ownerBrowser, $"/v1/team/{leaving.Id}/removal", new { reason = "Left Medvya on 7 Nov" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await leavingBrowser.GetAsync(new Uri("/v1/me", UriKind.Relative))).StatusCode);
        using var again = Browser();
        Assert.Equal("/sign-in?signin=failed", (await SignOn(again, leaving.Subject!)).Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(ownerBrowser, $"/v1/team/{owner.Id}/removal", new { reason = "Removing myself by mistake" })).StatusCode);

        // Access that ended by date (least access by default) refuses sign-on the same way.
        var (expired, _) = await Member(PlatformRole.Support);
        await using (var db = Db())
        {
            (await db.Members.SingleAsync(m => m.Id == expired.Id)).AccessEndsOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            await db.SaveChangesAsync();
        }
        using var late = Browser();
        Assert.Equal("/sign-in?signin=failed", (await SignOn(late, expired.Subject!)).Headers.Location!.OriginalString);
    }

    // Entries saved together share a time; paging one at a time must still return each of them once.
    [Fact]
    public async Task TheAuditLogPagesThroughEntriesThatShareATime()
    {
        var (owner, key) = await Member(PlatformRole.Owner);
        using var browser = await SignedIn(owner, key);
        var at = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var db = Db())
        {
            for (var i = 0; i < 3; i++) db.AuditEntries.Add(new PlatformAuditEntry { Action = "team.access_review", ResourceType = "platform_team", OccurredAt = at, Reason = $"Tied entry {i}" });
            await db.SaveChangesAsync();
        }
        var seen = new List<string>();
        var url = $"/v1/audit?limit=1&before={Uri.EscapeDataString(at.AddDays(1).ToString("O"))}";
        for (var page = 0; page < 5; page++)
        {
            var entries = (await browser.GetFromJsonAsync<JsonElement>(new Uri(url, UriKind.Relative))).EnumerateArray().ToList();
            if (entries.Count == 0) break;
            var last = entries[0];
            seen.Add(last.GetProperty("reason").GetString()!);
            url = $"/v1/audit?limit=1&before={Uri.EscapeDataString(last.GetProperty("occurredAt").GetString()!)}&beforeId={last.GetProperty("id").GetGuid()}";
        }
        Assert.Equal(["Tied entry 0", "Tied entry 1", "Tied entry 2"], seen.Order());
    }

    [Fact]
    public async Task TheExportIsLoggedFirstAndDefusesSpreadsheetFormulas()
    {
        var (owner, key) = await Member(PlatformRole.Owner);
        using var browser = await SignedIn(owner, key);
        await Post(browser, "/v1/team/access-review", new { reason = "=HYPERLINK(\"x\") review" });
        var export = await Post(browser, "/v1/audit/export", new { reason = "Annual audit request" });
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType!.MediaType);
        var csv = await export.Content.ReadAsStringAsync();
        Assert.StartsWith("occurred_at,action,resource_type,resource_id,practice_id,actor,reason", csv, StringComparison.Ordinal);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\") review\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"audit.export\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"Annual audit request\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAuditLogIsAppendOnlyForEveryRole()
    {
        var (owner, key) = await Member(PlatformRole.Owner);
        using var signedIn = await SignedIn(owner, key); // writes sign-in entries
        foreach (var user in new[] { "platform_user", "dbo" })
        {
            await using var conn = await sql.OpenAs(user);
            foreach (var statement in new[] { "UPDATE platform.\"AuditEntries\" SET \"Reason\" = 'changed'", "DELETE FROM platform.\"AuditEntries\"", "TRUNCATE platform.\"AuditEntries\"" })
            {
                await using var cmd = new NpgsqlCommand(statement, conn);
                var e = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
                // The console role holds no such right; even the superuser is stopped by the trigger.
                Assert.Equal(user == "dbo" ? PostgresErrorCodes.RestrictViolation : PostgresErrorCodes.InsufficientPrivilege, e.SqlState);
            }
        }
    }

    // Check 3, database layer: the console's login has no right on any clinic schema (the host references none either: PlatformConsoleTests).
    [Theory]
    [InlineData("patients.\"Patients\"")]
    [InlineData("encounters.\"Encounters\"")]
    [InlineData("scheduling.\"Appointments\"")]
    [InlineData("tenancy.\"Tenants\"")]
    [InlineData("audit.\"AuditEvents\"")]
    [InlineData("identity.\"Users\"")]
    public async Task TheConsoleLoginCannotReadClinicData(string table)
    {
        await using var conn = await sql.OpenAs("platform_user");
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table}", conn);
        var e = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteScalarAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, e.SqlState);
    }
}

/// <summary>MDV-P5-040: the configured first owner registers the very first key without a link, once.</summary>
public sealed class PlatformBootstrapTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IDisposable
{
    readonly WebApplicationFactory<ConsoleProgram> factory = PlatformConsoleTests.CreateFactory(sql);

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task TheFirstOwnerRegistersTheFirstKeyAndNobodyElseCanWithoutALink()
    {
        using var browser = PlatformConsoleTests.Browser(factory);
        Assert.Equal("/", (await PlatformConsoleTests.SignOn(browser, "platform-owner")).Headers.Location!.OriginalString);
        using var key = new SoftSecurityKey(PlatformConsoleTests.Origin);
        Assert.Equal(HttpStatusCode.NoContent, await PlatformConsoleTests.RegisterKey(browser, key));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(new Uri("/v1/team", UriKind.Relative))).StatusCode);

        // A second session of the same owner cannot add a key without a link now that one exists.
        using var second = PlatformConsoleTests.Browser(factory);
        await PlatformConsoleTests.SignOn(second, "platform-owner");
        Assert.Equal(HttpStatusCode.Forbidden, await PlatformConsoleTests.RegisterKey(second, new SoftSecurityKey(PlatformConsoleTests.Origin)));
        // And a new company account is not taken for the first owner once the team exists.
        using var stranger = PlatformConsoleTests.Browser(factory);
        Assert.Equal("/sign-in?signin=failed", (await PlatformConsoleTests.SignOn(stranger, "new")).Headers.Location!.OriginalString);
    }
}

/// <summary>MDV-P5-040: two owners removing each other at the same moment never leave the console without an owner. A fresh database, so
/// the only owners are the ones each round creates.</summary>
public sealed class PlatformLastOwnerTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IDisposable
{
    // Ten sign-ins in a few seconds would hit the per-address sign-in limit, which is not what this test is about.
    readonly WebApplicationFactory<ConsoleProgram> factory = PlatformConsoleTests.CreateFactory(sql, signInPerMinute: 1000);

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task TwoOwnersRemovingEachOtherAtOnceLeaveOneOwner()
    {
        var (survivor, survivorKey) = await PlatformConsoleTests.Seed(sql, PlatformRole.Owner);
        for (var round = 0; round < 5; round++)
        {
            var (other, otherKey) = await PlatformConsoleTests.Seed(sql, PlatformRole.Owner);
            using var a = await PlatformConsoleTests.SignedIn(factory, survivor, survivorKey);
            using var b = await PlatformConsoleTests.SignedIn(factory, other, otherKey);
            var answers = await Task.WhenAll(
                PlatformConsoleTests.Post(a, $"/v1/team/{other.Id}/removal", new { reason = "Race test, round " + round }),
                PlatformConsoleTests.Post(b, $"/v1/team/{survivor.Id}/removal", new { reason = "Race test, round " + round }));
            await using var db = PlatformConsoleTests.Db(sql);
            var owners = await db.Members.Where(m => m.Role == PlatformRole.Owner && m.RemovedAt == null).ToListAsync();
            var left = Assert.Single(owners);
            // Exactly one removal succeeds. The other is refused: 409 when it reaches the last-owner check, or 401 when the winner's removal
            // committed before the loser's request was authenticated (the loser is no longer a member). Both leave one owner, asserted above.
            var codes = answers.Select(r => r.StatusCode).Order().ToList();
            Assert.Equal(HttpStatusCode.NoContent, codes[0]);
            Assert.Contains(codes[1], new[] { HttpStatusCode.Conflict, HttpStatusCode.Unauthorized });
            (survivor, survivorKey) = left.Id == survivor.Id ? (survivor, survivorKey) : (other, otherKey);
        }
    }
}

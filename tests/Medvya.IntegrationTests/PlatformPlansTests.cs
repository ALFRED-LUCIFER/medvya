extern alias platform;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Platform.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ConsoleProgram = platform::Program;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P5-042, ADR-019: versioned plans in the console. Version 1 is the approved price list; an edit adds a version and never changes one;
/// money is integer minor units with its currency; owner and finance save, every other role reads; every save needs a reason and is audited.
/// </summary>
public sealed class PlatformPlansTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IDisposable
{
    readonly WebApplicationFactory<ConsoleProgram> factory = PlatformConsoleTests.CreateFactory(sql);

    public void Dispose() => factory.Dispose();

    async Task<(PlatformMember Member, HttpClient Browser)> SignedIn(PlatformRole role)
    {
        var (member, key) = await PlatformConsoleTests.Seed(sql, role);
        return (member, await PlatformConsoleTests.SignedIn(factory, member, key));
    }

    static async Task<List<JsonElement>> Plans(HttpClient browser) =>
        [.. (await browser.GetFromJsonAsync<JsonElement>(new Uri("/v1/plans", UriKind.Relative))).EnumerateArray()];

    static JsonElement Latest(List<JsonElement> plans, string code) =>
        plans.Where(p => p.GetProperty("code").GetString() == code).MaxBy(p => p.GetProperty("version").GetInt32());

    static JsonElement Price(JsonElement plan, string country) =>
        plan.GetProperty("prices").EnumerateArray().Single(p => p.GetProperty("country").GetString() == country);

    /// <summary>The latest clinic version as an edit, with the India monthly price changed.</summary>
    static object ClinicEdit(JsonElement latest, long inrMonthly, string? reason = "Price list 2027.01 approved by the owner", string[]? features = null) => new
    {
        baseVersion = latest.GetProperty("version").GetInt32(),
        doctors = (int?)null, staff = 15, locations = (int?)null, assistVisitsPerDoctorMonth = 300, trialDays = 14,
        features = features ?? ["ai.ambient_notes", "patient_app.booking"],
        prices = new object[]
        {
            new { country = "IN", monthly = inrMonthly, yearly = 3499000L, assistMonthly = (long?)null },
            new { country = "AE", monthly = 59900L, yearly = 599000L, assistMonthly = (long?)null },
        },
        reason,
    };

    [Fact]
    public async Task VersionOneIsTheApprovedPriceListInMinorUnits()
    {
        var (_, support) = await SignedIn(PlatformRole.Support);
        using var _b = support;
        var plans = await Plans(support);
        var v1 = plans.Where(p => p.GetProperty("version").GetInt32() == 1).ToDictionary(p => p.GetProperty("code").GetString()!);
        Assert.Equal(["clinic", "hospital", "solo"], v1.Keys.Order(StringComparer.Ordinal));

        var solo = v1["solo"];
        Assert.Equal(("INR", 199900L, 1999000L, 99900L), (Price(solo, "IN").GetProperty("currency").GetString(), Price(solo, "IN").GetProperty("monthly").GetInt64(),
            Price(solo, "IN").GetProperty("yearly").GetInt64(), Price(solo, "IN").GetProperty("assistMonthly").GetInt64()));
        Assert.Equal(("AED", 34900L, 14900L), (Price(solo, "AE").GetProperty("currency").GetString(), Price(solo, "AE").GetProperty("monthly").GetInt64(),
            Price(solo, "AE").GetProperty("assistMonthly").GetInt64()));
        Assert.Equal((1, 2, 1, 14), (solo.GetProperty("doctors").GetInt32(), solo.GetProperty("staff").GetInt32(), solo.GetProperty("locations").GetInt32(), solo.GetProperty("trialDays").GetInt32()));
        Assert.DoesNotContain("ai.ambient_notes", solo.GetProperty("features").EnumerateArray().Select(f => f.GetString()));

        var clinic = v1["clinic"];
        Assert.True(clinic.GetProperty("perDoctor").GetBoolean());
        Assert.Equal(JsonValueKind.Null, clinic.GetProperty("doctors").ValueKind); // per doctor: no cap
        Assert.Equal((15, 300), (clinic.GetProperty("staff").GetInt32(), clinic.GetProperty("assistVisitsPerDoctorMonth").GetInt32()));
        Assert.Equal(JsonValueKind.Null, Price(clinic, "IN").GetProperty("assistMonthly").ValueKind); // Assist included, no add-on
        Assert.Contains("ai.ambient_notes", clinic.GetProperty("features").EnumerateArray().Select(f => f.GetString()));

        var hospital = v1["hospital"];
        Assert.True(hospital.GetProperty("custom").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Price(hospital, "AE").GetProperty("monthly").ValueKind);
        Assert.Equal(4000000L, Price(hospital, "AE").GetProperty("yearly").GetInt64());
    }

    [Fact]
    public async Task AnEditAddsANewVersionAndLeavesTheOldOneUntouched()
    {
        var (finance, browser) = await SignedIn(PlatformRole.Finance);
        using var _b = browser;
        var before = Latest(await Plans(browser), "clinic");
        var baseVersion = before.GetProperty("version").GetInt32();
        var oldRow = await Row(before.GetProperty("id").GetGuid());

        var saved = await PlatformConsoleTests.Post(browser, "/v1/plans/clinic/versions",
            ClinicEdit(before, 379900, reason: "  Price list 2027.01 approved by the owner  ", features: ["patient_app.booking", "ai.ambient_notes", " ai.ambient_notes"]));
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var created = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(baseVersion + 1, created.GetProperty("version").GetInt32());
        Assert.Equal(379900L, Price(created, "IN").GetProperty("monthly").GetInt64());
        Assert.Equal("INR", Price(created, "IN").GetProperty("currency").GetString()); // the country's currency, set by the server
        Assert.Equal(["ai.ambient_notes", "patient_app.booking"], created.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(finance.DisplayName, created.GetProperty("createdBy").GetString());
        Assert.True(created.GetProperty("perDoctor").GetBoolean()); // carried from the version it replaces

        // The old version is exactly as it was, and both are listed.
        Assert.Equal(oldRow, await Row(before.GetProperty("id").GetGuid()));
        var versions = (await Plans(browser)).Where(p => p.GetProperty("code").GetString() == "clinic").Select(p => p.GetProperty("version").GetInt32()).ToList();
        Assert.Equal(versions.OrderDescending(), versions);
        Assert.Contains(baseVersion, versions);

        await using var db = PlatformConsoleTests.Db(sql);
        var entry = await db.AuditEntries.SingleAsync(a => a.Action == "plan.version.create" && a.ResourceId == created.GetProperty("id").GetGuid());
        Assert.Equal((finance.Id, "Price list 2027.01 approved by the owner"), (entry.ActorMemberId!.Value, entry.Reason));

        // Saving again from the old version is refused: someone else's version would be overwritten without them knowing.
        Assert.Equal(HttpStatusCode.Conflict, (await PlatformConsoleTests.Post(browser, "/v1/plans/clinic/versions", ClinicEdit(before, 399900))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PlatformConsoleTests.Post(browser, "/v1/plans/gold/versions", ClinicEdit(before, 399900))).StatusCode);
    }

    [Fact]
    public async Task TheDatabaseRefusesToChangeASavedVersion()
    {
        await using var conn = new NpgsqlConnection(sql.As("medvya_platform"));
        await conn.OpenAsync();
        foreach (var command in new[]
                 {
                     """UPDATE platform."PlanPrices" SET "Monthly" = 1""", """DELETE FROM platform."PlanPrices" """,
                     """UPDATE platform."PlanVersions" SET "TrialDays" = 30""", """DELETE FROM platform."PlanVersions" """,
                 })
        {
            await using var cmd = new NpgsqlCommand(command, conn);
            var refused = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
        }
        // The trigger refuses it for the owner too, whatever the grants.
        await using var admin = new NpgsqlConnection(sql.AdminConnectionString);
        await admin.OpenAsync();
        await using var update = new NpgsqlCommand("""UPDATE platform."PlanVersions" SET "TrialDays" = 30""", admin);
        Assert.Equal(PostgresErrorCodes.RestrictViolation, (await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync())).SqlState);
    }

    [Theory]
    [InlineData(PlatformRole.Operations)]
    [InlineData(PlatformRole.Support)]
    [InlineData(PlatformRole.Reviewer)]
    public async Task OnlyOwnerAndFinanceSaveAPlan(PlatformRole role)
    {
        var (_, browser) = await SignedIn(role);
        using var _b = browser;
        var latest = Latest(await Plans(browser), "clinic"); // every role reads
        Assert.Equal(HttpStatusCode.Forbidden, (await PlatformConsoleTests.Post(browser, "/v1/plans/clinic/versions", ClinicEdit(latest, 1))).StatusCode);
        Assert.Equal(latest.GetProperty("version").GetInt32(), Latest(await Plans(browser), "clinic").GetProperty("version").GetInt32());

        var (_, owner) = await SignedIn(PlatformRole.Owner);
        using var _o = owner;
        Assert.Equal(HttpStatusCode.Created, (await PlatformConsoleTests.Post(owner, "/v1/plans/clinic/versions", ClinicEdit(latest, 349900))).StatusCode);
    }

    [Fact]
    public async Task ASaveWithoutAReasonOrWithBadValuesChangesNothing()
    {
        var (_, browser) = await SignedIn(PlatformRole.Owner);
        using var _b = browser;
        var latest = Latest(await Plans(browser), "clinic");
        var version = latest.GetProperty("version").GetInt32();
        var bad = new (object Body, string Field)[]
        {
            (ClinicEdit(latest, 349900, reason: null), "reason"),
            (ClinicEdit(latest, 349900, reason: "ok"), "reason"),
            (ClinicEdit(latest, -1), "prices"),
            (ClinicEdit(latest, 349900, features: ["AI Notes"]), "features"),
            (new { baseVersion = version, trialDays = 14, staff = 15, prices = new[] { new { country = "IN", monthly = 1L } }, reason = "Only India priced" }, "prices"),
            (new { baseVersion = version, trialDays = 400, prices = Array.Empty<object>(), reason = "Long trial for everyone" }, "trialDays"),
            (new { baseVersion = version, trialDays = 14, doctors = 0, prices = Array.Empty<object>(), reason = "No doctors at all" }, "doctors"),
            (new { baseVersion = version, staff = 15, prices = Array.Empty<object>(), reason = "Trial left out by mistake" }, "trialDays"),
        };
        foreach (var (body, field) in bad)
        {
            var refused = await PlatformConsoleTests.Post(browser, "/v1/plans/clinic/versions", body);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains($"\"{field}\"", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        Assert.Equal(version, Latest(await Plans(browser), "clinic").GetProperty("version").GetInt32());
    }

    async Task<string> Row(Guid id)
    {
        await using var db = PlatformConsoleTests.Db(sql);
        var v = await db.PlanVersions.AsNoTracking().Include(x => x.Prices).SingleAsync(x => x.Id == id);
        return JsonSerializer.Serialize(v);
    }
}

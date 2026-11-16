extern alias platform;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Platform.Domain;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ConsoleProgram = platform::Program;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P5-041 through the console host on real PostgreSQL: the Features page reads and writes Medvya's releases in each country's database through the
/// narrow medvya_features login. Mutation checks: let every console role change (PlatformPolicies.FeatureChange to Console) and the Support, Finance and
/// Reviewer steps fail; drop the sign-off rule in FeatureReleaseConsole and the clinical refusal fails; drop the audit call and the audit steps fail.
/// </summary>
public sealed class PlatformFeatureTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime, IDisposable
{
    static readonly string Nurse = Features.NurseIntake.Key;
    const string SignOff = "Clinical safety officer (synthetic), HL-041";
    WebApplicationFactory<ConsoleProgram>? factory;
    // The platform audit log is append-only and shared by the class: each test reads the entries made since it started.
    readonly DateTimeOffset started = DateTimeOffset.UtcNow;

    public async Task InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(sql.AdminConnectionString);
        await admin.OpenAsync();
        await using var clear = new NpgsqlCommand("DELETE FROM tenancy.\"FeatureReleases\"", admin);
        await clear.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory?.Dispose();

    async Task<HttpClient> Console(PlatformRole role)
    {
        // One database stands in for both countries here; the rows carry their country.
        factory ??= PlatformConsoleTests.CreateFactory(sql, signInPerMinute: 1000, configure: b => b
            .UseSetting("ConnectionStrings:Features:IN", sql.As("medvya_features"))
            .UseSetting("ConnectionStrings:Features:AE", sql.As("medvya_features")));
        var (member, key) = await PlatformConsoleTests.Seed(sql, role);
        return await PlatformConsoleTests.SignedIn(factory, member, key);
    }

    static Task<HttpResponseMessage> Set(HttpClient c, string country, string key, string stage, string? reason = "Pilot clinics asked for it", string? signOff = null,
        int? percentage = null, Guid[]? organisations = null) =>
        PlatformConsoleTests.Post(c, $"/v1/features/{country}/{key}", new { stage, percentage, organisations, clinicalSignOff = signOff, reason });

    static Task<HttpResponseMessage> TurnOff(HttpClient c, string key, string? reason = "Incident: intake saves fail for some clinics") =>
        PlatformConsoleTests.Post(c, $"/v1/features/{key}/off", new { reason });

    static async Task<JsonElement> Row(HttpClient c, string country, string key) =>
        (await c.GetFromJsonAsync<JsonElement>(new Uri("/v1/features", UriKind.Relative))).EnumerateArray().Single(x => x.GetProperty("country").GetString() == country)
            .GetProperty("features").EnumerateArray().Single(f => f.GetProperty("key").GetString() == key);

    async Task<List<PlatformAuditEntry>> Audit(string key)
    {
        await using var db = PlatformConsoleTests.Db(sql);
        return await db.AuditEntries.Where(a => a.OccurredAt >= started && a.ResourceType.StartsWith("feature:" + key + ":")).OrderBy(a => a.OccurredAt).ToListAsync();
    }

    [Theory]
    [InlineData(PlatformRole.Owner), InlineData(PlatformRole.Operations)]
    public async Task OwnerAndOperationsChangeAReleaseAndEachChangeIsAuditedWithItsReason(PlatformRole role)
    {
        using var c = await Console(role);
        Assert.True((await Row(c, "IN", Features.Billing.Key)).GetProperty("isNew").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await Set(c, "IN", Features.Billing.Key, "Percentage", percentage: 20)).StatusCode);
        var row = await Row(c, "IN", Features.Billing.Key);
        Assert.Equal(("Percentage", 20, false), (row.GetProperty("stage").GetString(), row.GetProperty("percentage").GetInt32(), row.GetProperty("isNew").GetBoolean()));
        Assert.Equal("Everyone", (await Row(c, "AE", Features.Billing.Key)).GetProperty("stage").GetString()); // the other country is untouched

        var entry = Assert.Single(await Audit(Features.Billing.Key));
        Assert.Equal(("feature.percentage", "feature:billing.invoices:IN", "Pilot clinics asked for it"), (entry.Action, entry.ResourceType, entry.Reason));
        Assert.NotNull(entry.ActorMemberId);
    }

    [Theory]
    [InlineData(PlatformRole.Support), InlineData(PlatformRole.Finance), InlineData(PlatformRole.Reviewer)]
    public async Task SupportFinanceAndReviewerReadButEveryChangeIs403(PlatformRole role)
    {
        using var c = await Console(role);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(new Uri("/v1/features", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Set(c, "IN", Nurse, "Off")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await TurnOff(c, Nurse)).StatusCode);
        Assert.True((await Row(c, "IN", Nurse)).GetProperty("isNew").GetBoolean());
        Assert.Empty(await Audit(Nurse));
    }

    [Fact]
    public async Task SigningOnWithoutTheKeyOpensNoFeatureEndpoint()
    {
        await Console(PlatformRole.Owner); // builds the factory
        var (member, _) = await PlatformConsoleTests.Seed(sql, PlatformRole.Owner);
        using var browser = PlatformConsoleTests.Browser(factory!);
        await PlatformConsoleTests.SignOn(browser, member.Subject!);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync(new Uri("/v1/features", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await TurnOff(browser, Nurse)).StatusCode);
    }

    [Theory]
    [InlineData(null), InlineData(""), InlineData("four")]
    public async Task EveryChangeNeedsAReason(string? reason)
    {
        using var c = await Console(PlatformRole.Owner);
        Assert.Equal(HttpStatusCode.BadRequest, (await Set(c, "IN", Nurse, "Off", reason)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await TurnOff(c, Nurse, reason)).StatusCode);
        Assert.True((await Row(c, "IN", Nurse)).GetProperty("isNew").GetBoolean());
        Assert.Empty(await Audit(Nurse));
    }

    [Fact]
    public async Task AClinicalFeatureCannotReachEveryoneWithoutARecordedSignOff()
    {
        using var c = await Console(PlatformRole.Operations);
        Assert.Equal(HttpStatusCode.NoContent, (await Set(c, "IN", Nurse, "Internal")).StatusCode);
        var refused = await Set(c, "IN", Nurse, "Everyone");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("required", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("clinicalSignOff")[0].GetString());
        Assert.Equal("Internal", (await Row(c, "IN", Nurse)).GetProperty("stage").GetString());
        Assert.Equal(["feature.internal"], (await Audit(Nurse)).Select(a => a.Action)); // a refused change is not logged as done

        Assert.Equal(HttpStatusCode.NoContent, (await Set(c, "IN", Nurse, "Everyone", signOff: SignOff)).StatusCode);
        var row = await Row(c, "IN", Nurse);
        Assert.Equal(("Everyone", SignOff), (row.GetProperty("stage").GetString(), row.GetProperty("clinicalSignOff").GetString()));
    }

    [Fact]
    public async Task AlwaysOnFeaturesAreLocked()
    {
        using var c = await Console(PlatformRole.Owner);
        var row = await Row(c, "IN", "prescribing.signing");
        Assert.Equal((true, "Everyone"), (row.GetProperty("alwaysOn").GetBoolean(), row.GetProperty("stage").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await Set(c, "IN", "prescribing.signing", "Off")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await TurnOff(c, "prescribing.signing")).StatusCode);
        Assert.Empty(await Audit("prescribing.signing"));
        Assert.Equal(HttpStatusCode.NotFound, (await Set(c, "IN", "scheduling.waitlist", "Off")).StatusCode); // not in the list
        Assert.Equal(HttpStatusCode.NotFound, (await TurnOff(c, "scheduling.waitlist")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Set(c, "GB", Nurse, "Off")).StatusCode); // no database for that country
    }

    /// <summary>The kill switch: every country at once; the clinics' feature is gone and their data and own switches are untouched.</summary>
    [Fact]
    public async Task TurnOffForAllHidesTheFeatureInEveryCountryWithoutDeletingData()
    {
        var clinics = new List<Guid>();
        foreach (var country in new[] { "IN", "AE" })
        {
            var id = Guid.NewGuid();
            await using var db = sql.Context(id);
            db.Tenants.Add(new Tenant { State = OrganisationState.Approved, Id = id, Name = "Synthetic Clinic", CountryCode = country, ResidencyRegion = country == "IN" ? "centralindia" : "uaenorth" });
            db.FeatureFlags.Add(new FeatureFlag { Id = Guid.NewGuid(), TenantId = id, Key = Nurse, Enabled = true, Owner = "practice", Description = "Nurse intake (synthetic test)", ChangedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            clinics.Add(id);
        }
        // A fresh cache per check: the clinic host's at most 60 s old copy, already expired.
        FeatureFlags Clinic(Guid id) => new(sql.Context(id), new PostgresFixture.FixedTenant(id), new FixedUser(), new AuditLog(sql.Audit(id)), new FeatureReleaseCache(TimeProvider.System, new HashSet<Guid>()), TimeProvider.System);
        foreach (var id in clinics) Assert.True(await Clinic(id).IsEnabledAsync(Features.NurseIntake));

        using var c = await Console(PlatformRole.Owner);
        Assert.Equal(HttpStatusCode.NoContent, (await TurnOff(c, Nurse)).StatusCode);
        foreach (var id in clinics)
        {
            Assert.False(await Clinic(id).IsEnabledAsync(Features.NurseIntake));
            await using var db = sql.Context(id);
            Assert.True(await db.FeatureFlags.AnyAsync(f => f.Key == Nurse && f.Enabled)); // the clinic's own switch and data stay
        }
        Assert.Equal(["feature:clinical.nurse-intake:AE", "feature:clinical.nurse-intake:IN"], (await Audit(Nurse)).Select(a => a.ResourceType).Order());
        Assert.All(await Audit(Nurse), a => Assert.Equal(("feature.off", "Incident: intake saves fail for some clinics"), (a.Action, a.Reason)));

        // Back on (with its sign-off): the feature returns as it was, nothing to restore.
        Assert.Equal(HttpStatusCode.NoContent, (await Set(c, "IN", Nurse, "Everyone", signOff: SignOff)).StatusCode);
        Assert.True(await Clinic(clinics[0]).IsEnabledAsync(Features.NurseIntake));
        Assert.False(await Clinic(clinics[1]).IsEnabledAsync(Features.NurseIntake));
    }

    sealed class FixedUser : ICurrentUser { public Guid? UserId => null; }
}

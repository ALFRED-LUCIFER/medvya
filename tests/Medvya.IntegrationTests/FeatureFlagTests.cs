using System.Net;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P0-028 and MDV-P5-041 on real PostgreSQL: a feature is on only when Medvya's release reaches the organisation and the clinic's own switch is on; the
/// clinic switch defaults to off and is audited; gated endpoints answer 404 when off. Releases are written as the console writes them, through the narrow
/// login medvya_features. Mutation checks: drop the release check in FeatureFlags.IsReleasedAsync and the Medvya-off steps fail; drop the clinic-switch
/// check and ClinicSwitchAloneOrReleaseAloneIsNotEnough fails; grant medvya_features another table or medvya_tenancy write on FeatureReleases in
/// table-rights.sql and the login tests fail.
/// </summary>
public class FeatureFlagTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    static readonly Feature Nurse = Features.NurseIntake;
    readonly Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid(), actor = Guid.NewGuid(), operatorId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        foreach (var (tenant, country) in new[] { (tenantA, "IN"), (tenantB, "AE") })
        {
            await using var db = sql.Context(tenant);
            db.Tenants.Add(new Tenant { State = OrganisationState.Approved, Id = tenant, Name = "Synthetic Clinic", CountryCode = country, ResidencyRegion = country == "IN" ? "centralindia" : "uaenorth" });
            await db.SaveChangesAsync();
        }
        // Releases are per database, not per tenant: each test starts from none (every feature at its initial stage).
        await using var admin = new NpgsqlConnection(sql.AdminConnectionString);
        await admin.OpenAsync();
        await using var clear = new NpgsqlCommand("DELETE FROM tenancy.\"FeatureReleases\"", admin);
        await clear.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    FeatureFlags Flags(Guid? tenant, IAuditLog? audit = null, params Guid[] internalOrganisations) =>
        new(sql.Context(tenant), new PostgresFixture.FixedTenant(tenant), new FixedUser(actor), audit ?? new AuditLog(sql.Audit(tenant)),
            new FeatureReleaseCache(TimeProvider.System, internalOrganisations.ToHashSet()));

    FeatureReleaseConsole Console() => new(new Dictionary<string, string> { ["IN"] = sql.As("medvya_features"), ["AE"] = sql.As("medvya_features") }, TimeProvider.System);

    Task<Guid?> Release(string country, Feature feature, ReleaseStage stage, int? percentage = null, Guid[]? organisations = null) =>
        Console().SetAsync(country, feature.Key, new ReleaseChange(stage, percentage, organisations, "Clinical safety officer (synthetic), HL-041"), operatorId);

    [Fact]
    public async Task ClinicSwitchesDefaultToOff()
    {
        Assert.False(await Flags(tenantA).IsEnabledAsync(Nurse));
        Assert.True(await Flags(tenantA).IsReleasedAsync(Nurse)); // an existing feature starts released to everyone: nothing disappeared for clinics
        Assert.False(await Flags(null).IsEnabledAsync(Nurse));
        Assert.False(await Flags(null).IsReleasedAsync(Nurse));
    }

    [Fact]
    public async Task TurningAFlagOffTakesEffectOnTheNextCheck()
    {
        await Flags(tenantA).SetAsync(Nurse, true, "clinical-team", "Nurse intake");
        Assert.True(await Flags(tenantA).IsEnabledAsync(Nurse));

        await Flags(tenantA).SetAsync(Nurse, false, "clinical-team", "Nurse intake");
        Assert.False(await Flags(tenantA).IsEnabledAsync(Nurse));
    }

    [Fact]
    public async Task FlagsAreIsolatedPerTenantEvenWithQueryFiltersOff()
    {
        await Flags(tenantA).SetAsync(Nurse, true, "clinical-team", "Nurse intake");
        Assert.False(await Flags(tenantB).IsEnabledAsync(Nurse));

        await using var db = sql.Context(tenantB);
        Assert.DoesNotContain(await db.FeatureFlags.IgnoreQueryFilters().ToListAsync(), f => f.TenantId == tenantA); // row-level security
    }

    [Fact]
    public async Task EveryChangeIsAuditedWithItsActorAndOwnerIsRecorded()
    {
        await Flags(tenantA).SetAsync(Nurse, true, "clinical-team", "Nurse intake");
        await Flags(tenantA).SetAsync(Nurse, false, "clinical-team", "Nurse intake");

        await using var tenancy = sql.Context(tenantA);
        var flag = await tenancy.FeatureFlags.SingleAsync(f => f.Key == Nurse.Key);
        Assert.Equal(("clinical-team", actor, actor), (flag.Owner, flag.CreatedBy, flag.ChangedBy));

        await using var audit = sql.Audit(tenantA);
        var events = await audit.Events.Where(e => e.ResourceId == flag.Id).OrderBy(e => e.OccurredAt).Select(e => new { e.Action, e.ActorUserId }).ToListAsync();
        Assert.Equal(["flag.enable", "flag.disable"], events.Select(e => e.Action));
        Assert.All(events, e => Assert.Equal(actor, e.ActorUserId));
    }

    [Fact]
    public async Task AChangeThatCannotBeAuditedDoesNotHappen()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Flags(tenantA, new ThrowingLog()).SetAsync(Nurse, true, "clinical-team", "Nurse intake"));
        Assert.False(await Flags(tenantA).IsEnabledAsync(Nurse));
    }

    [Theory]
    [InlineData(" ", "desc")]
    [InlineData("team", "")]
    public async Task EveryFlagNeedsAnOwnerAndDescription(string owner, string description) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Flags(tenantA).SetAsync(Nurse, true, owner, description));

    [Fact]
    public async Task AFeatureWithoutAClinicSwitchCannotBeSwitchedByAClinic() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Flags(tenantA).SetAsync(Features.Find("audit.log")!, false, "practice", "Try to hide the audit log"));

    [Fact]
    public async Task AlwaysOnFeaturesStayOnAndAreLockedInTheConsole()
    {
        var auditLog = Features.Find("audit.log")!;
        Assert.True(await Flags(tenantA).IsEnabledAsync(auditLog));
        var refused = await Assert.ThrowsAsync<SettingsValidationException>(() => Release("IN", auditLog, ReleaseStage.Off));
        Assert.Contains(refused.Errors, e => e is { Field: "stage", Code: "locked" });
        Assert.True(await Flags(tenantA).IsEnabledAsync(auditLog));
        Assert.DoesNotContain(auditLog.Key, await Flags(tenantA).EnabledKeysAsync());
    }

    [Fact]
    public async Task ClinicSwitchAloneOrReleaseAloneIsNotEnough()
    {
        await Flags(tenantA).SetAsync(Nurse, true, "clinical-team", "Nurse intake");
        await Release("IN", Nurse, ReleaseStage.Off);
        Assert.False(await Flags(tenantA).IsEnabledAsync(Nurse)); // Medvya's switch off
        Assert.DoesNotContain(Nurse.Key, await Flags(tenantA).EnabledKeysAsync()); // the clinic app hides it

        await Release("IN", Nurse, ReleaseStage.Everyone);
        Assert.True(await Flags(tenantA).IsEnabledAsync(Nurse));
        Assert.Contains(Nurse.Key, await Flags(tenantA).EnabledKeysAsync());
        await Flags(tenantA).SetAsync(Nurse, false, "clinical-team", "Nurse intake");
        Assert.False(await Flags(tenantA).IsEnabledAsync(Nurse)); // the clinic's switch off
        Assert.True(await Flags(tenantA).IsReleasedAsync(Nurse));
    }

    [Fact]
    public async Task AReleaseReachesOnlyItsCountry()
    {
        await Release("IN", Nurse, ReleaseStage.Off);
        Assert.False(await Flags(tenantA).IsReleasedAsync(Nurse)); // India
        Assert.True(await Flags(tenantB).IsReleasedAsync(Nurse)); // UAE keeps its own release
    }

    [Fact]
    public async Task InternalReachesMedvyasOwnOrganisationsAndBetaAddsTheNamedOnes()
    {
        var named = Guid.NewGuid();
        await Release("IN", Nurse, ReleaseStage.Internal, organisations: [named]);
        Assert.True(await Flags(tenantA, null, tenantA).IsReleasedAsync(Nurse)); // a Medvya test organisation
        Assert.False(await Flags(tenantA).IsReleasedAsync(Nurse));

        await Release("IN", Nurse, ReleaseStage.Beta, organisations: [tenantA]);
        Assert.True(await Flags(tenantA).IsReleasedAsync(Nurse)); // named
        Assert.True(await Flags(tenantB).IsReleasedAsync(Nurse)); // the UAE has its own release, still Everyone
        await Release("AE", Nurse, ReleaseStage.Beta, organisations: [named]);
        Assert.False(await Flags(tenantB).IsReleasedAsync(Nurse)); // not named
        Assert.True(await Flags(tenantB, null, tenantB).IsReleasedAsync(Nurse)); // internal is in every later stage
    }

    [Fact]
    public void APercentageReachesAStableShareThatOnlyGrows()
    {
        var feature = Features.NurseIntake;
        var tenants = Enumerable.Range(0, 400).Select(_ => Guid.NewGuid()).ToList();
        bool On(Guid t, int pct) => FeatureRelease.Reaches(feature, new FeatureRelease { Country = "IN", Key = feature.Key, Stage = ReleaseStage.Percentage, Percentage = pct }, t, "IN", new HashSet<Guid>());
        var half = tenants.Where(t => On(t, 50)).ToList();
        Assert.InRange(half.Count, 120, 280); // about half, never all or none
        Assert.All(half, t => Assert.True(On(t, 50))); // the same answer every time
        Assert.All(half, t => Assert.True(On(t, 90))); // raising the share keeps everyone already in
        Assert.True(tenants.Count(t => On(t, 90)) > half.Count);
    }

    [Fact]
    public void AFeatureIsNeverOnInACountryItIsNotListedFor() =>
        Assert.False(FeatureRelease.Reaches(new Feature("x.india-only", false, false, false, ReleaseStage.Everyone, ["IN"]), null, Guid.NewGuid(), "AE", new HashSet<Guid>()));

    [Fact]
    public async Task TheConsoleRefusesAClinicalFeatureAtEveryoneWithoutASignOffAndKeepsOneOnceRecorded()
    {
        var console = Console();
        Assert.NotNull(await console.SetAsync("IN", Nurse.Key, new(ReleaseStage.Beta, null, [tenantA], null), operatorId));
        var refused = await Assert.ThrowsAsync<SettingsValidationException>(() => console.SetAsync("IN", Nurse.Key, new(ReleaseStage.Everyone, null, null, " "), operatorId));
        Assert.Contains(refused.Errors, e => e is { Field: "clinicalSignOff", Code: "required" });
        Assert.Equal(ReleaseStage.Beta, (await console.ListAsync("IN"))!.Single(f => f.Key == Nurse.Key).Stage);

        await console.SetAsync("IN", Nurse.Key, new(ReleaseStage.Everyone, null, null, "Clinical safety officer (synthetic), HL-041"), operatorId);
        await console.SetAsync("IN", Nurse.Key, new(ReleaseStage.Off, null, null, null), operatorId);
        await console.SetAsync("IN", Nurse.Key, new(ReleaseStage.Everyone, null, null, null), operatorId); // recorded once, kept
        var row = (await console.ListAsync("IN"))!.Single(f => f.Key == Nurse.Key);
        Assert.Equal((ReleaseStage.Everyone, "Clinical safety officer (synthetic), HL-041"), (row.Stage, row.ClinicalSignOff));

        // A feature that is not clinical needs none.
        await console.SetAsync("IN", Features.Billing.Key, new(ReleaseStage.Off, null, null, null), operatorId);
        await console.SetAsync("IN", Features.Billing.Key, new(ReleaseStage.Everyone, null, null, null), operatorId);
    }

    [Theory]
    [InlineData(ReleaseStage.Percentage, 0, "percentage", "range")]
    [InlineData(ReleaseStage.Percentage, 100, "percentage", "range")]
    [InlineData(ReleaseStage.Beta, null, "organisations", "required")]
    public async Task TheConsoleRefusesAnIncompleteStage(ReleaseStage stage, int? percentage, string field, string code)
    {
        var refused = await Assert.ThrowsAsync<SettingsValidationException>(() => Console().SetAsync("IN", Features.Billing.Key, new(stage, percentage, null, null), operatorId));
        Assert.Contains(refused.Errors, e => e.Field == field && e.Code == code);
        Assert.True((await Console().ListAsync("IN"))!.Single(f => f.Key == Features.Billing.Key).IsNew); // nothing saved
    }

    [Fact]
    public async Task TheConsoleKnowsOnlyListedFeaturesAndReachableCountries()
    {
        Assert.Null(await Console().SetAsync("IN", "scheduling.waitlist", new(ReleaseStage.Everyone, null, null, null), operatorId));
        Assert.Null(await Console().SetAsync("GB", Nurse.Key, new(ReleaseStage.Everyone, null, null, null), operatorId));
        Assert.Null(await Console().ListAsync("GB"));
        Assert.Equal(Features.All.Select(f => f.Key), (await Console().ListAsync("AE"))!.Select(f => f.Key));
    }

    [Fact]
    public async Task GatedEndpointIsNotFoundWhileEitherSwitchIsOffAndAReleaseChangeLandsWithinAMinute()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        await using var app = App(tenantA, s => s.AddSingleton<TimeProvider>(clock));
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/intake")).StatusCode); // the clinic's switch is off

        await Flags(tenantA).SetAsync(Nurse, true, "clinical-team", "Nurse intake");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/intake")).StatusCode);

        await Release("IN", Nurse, ReleaseStage.Off); // Medvya turns it off
        clock.Advance(FeatureReleaseCache.MaxAge);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/intake")).StatusCode); // no redeploy, no restart

        await Release("IN", Nurse, ReleaseStage.Everyone);
        clock.Advance(FeatureReleaseCache.MaxAge);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/intake")).StatusCode);
        await Flags(tenantA).SetAsync(Nurse, false, "clinical-team", "Nurse intake");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/intake")).StatusCode); // the clinic's switch: the next request
    }

    [Fact]
    public async Task GateRunsBeforeOtherFiltersAndWithoutATenant()
    {
        // .Audited() is declared first; with the flag off the gate still answers before the (broken) audit runs.
        await using var app = App(tenantA, s => s.AddScoped<IAuditLog, ThrowingLog>());
        Assert.Equal(HttpStatusCode.NotFound, (await app.GetTestClient().GetAsync("/intake")).StatusCode);

        await using var noTenant = App(null, _ => { });
        Assert.Equal(HttpStatusCode.NotFound, (await noTenant.GetTestClient().GetAsync("/intake")).StatusCode);
        Assert.False(handlerRan);
    }

    // The whole grant list of the console's login, table and column rights: a right added anywhere else shows here, whatever a policy would hide.
    [Fact]
    public async Task TheFeaturesLoginHoldsExactlyItsThreeRightsOnTheReleaseTable()
    {
        await using var conn = await sql.OpenAs("dbo");
        await using var cmd = new NpgsqlCommand("""
            SELECT table_schema || '.' || table_name || ' ' || privilege_type FROM information_schema.role_table_grants WHERE grantee = 'medvya_features'
            UNION SELECT table_schema || '.' || table_name || '.' || column_name || ' ' || privilege_type FROM information_schema.column_privileges
            WHERE grantee = 'medvya_features' AND NOT (table_schema = 'tenancy' AND table_name = 'FeatureReleases') ORDER BY 1
            """, conn);
        var rights = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync()) rights.Add(reader.GetString(0));
        Assert.Equal(["tenancy.FeatureReleases INSERT", "tenancy.FeatureReleases SELECT", "tenancy.FeatureReleases UPDATE"], rights);
    }

    // The console's login, whatever the host's code does: tenancy."FeatureReleases" and nothing else, and never a delete.
    [Theory]
    [InlineData("SELECT * FROM tenancy.\"Tenants\" LIMIT 1")]
    [InlineData("SELECT * FROM tenancy.\"FeatureFlags\" LIMIT 1")]
    [InlineData("SELECT * FROM tenancy.\"Memberships\" LIMIT 1")]
    [InlineData("SELECT * FROM patients.\"Patients\" LIMIT 1")]
    [InlineData("SELECT * FROM encounters.\"Encounters\" LIMIT 1")]
    [InlineData("SELECT * FROM audit.\"AuditEvents\" LIMIT 1")]
    [InlineData("SELECT * FROM platform.\"AuditEntries\" LIMIT 1")]
    [InlineData("UPDATE tenancy.\"FeatureFlags\" SET \"Enabled\" = true")]
    [InlineData("DELETE FROM tenancy.\"FeatureReleases\"")]
    public async Task TheFeaturesLoginReachesNothingButTheReleaseTable(string statement)
    {
        await using var conn = await sql.OpenAs("features_user", tenantA);
        await using var cmd = new NpgsqlCommand(statement, conn);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync())).SqlState);
    }

    [Theory]
    [InlineData("app_user")]
    [InlineData("tenancy_user")]
    public async Task TheClinicApplicationReadsReleasesButCannotChangeThem(string user)
    {
        await Release("IN", Nurse, ReleaseStage.Off);
        await using var conn = await sql.OpenAs(user, tenantA);
        await using (var read = new NpgsqlCommand("SELECT count(*) FROM tenancy.\"FeatureReleases\"", conn))
            Assert.Equal(1L, await read.ExecuteScalarAsync());
        foreach (var statement in new[] { "UPDATE tenancy.\"FeatureReleases\" SET \"Stage\" = 'Everyone'", "DELETE FROM tenancy.\"FeatureReleases\"",
            "INSERT INTO tenancy.\"FeatureReleases\" (\"Id\", \"Country\", \"Key\", \"Stage\", \"Percentage\", \"Organisations\", \"ChangedByMemberId\", \"ChangedAt\") VALUES (gen_random_uuid(), 'AE', 'billing.invoices', 'Off', 0, '{}', gen_random_uuid(), now())" })
        {
            await using var cmd = new NpgsqlCommand(statement, conn);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync())).SqlState);
        }
    }

    bool handlerRan;

    WebApplication App(Guid? tenant, Action<IServiceCollection> overrides)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ICurrentTenant>(new PostgresFixture.FixedTenant(tenant));
        builder.Services.AddSingleton<ICurrentUser>(new FixedUser(actor));
        builder.Services.AddAuditModule(sql.ConnectionString).AddTenancyModule(sql.ConnectionString);
        overrides(builder.Services);
        var app = builder.Build();
        app.MapGet("/intake", () => { handlerRan = true; return "synthetic"; }).Audited("intake.read", "intake").RequireFeature(Nurse);
        app.StartAsync().GetAwaiter().GetResult();
        return app;
    }

    sealed class FixedUser(Guid id) : ICurrentUser { public Guid? UserId => id; }

    sealed class ThrowingLog : IAuditLog
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct) => throw new InvalidOperationException("audit store down");
        public Task WriteAsync(AuditEvent auditEvent, System.Data.Common.DbTransaction transaction, CancellationToken ct) => throw new InvalidOperationException("audit store down");
    }
}

using System.Net;
using Medvya.Audit.Api;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-028: per-tenant flags, default off, audited, gated endpoints answer 404 when off.</summary>
public class FeatureFlagTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    const string Key = "scheduling.waitlist";
    readonly Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid(), actor = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        foreach (var (tenant, country) in new[] { (tenantA, "IN"), (tenantB, "AE") })
        {
            await using var db = sql.Context(tenant);
            db.Tenants.Add(new Tenant { Id = tenant, Name = "Synthetic Clinic", CountryCode = country, ResidencyRegion = country == "IN" ? "centralindia" : "uaenorth" });
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    FeatureFlags Flags(Guid? tenant, IAuditLog? audit = null) =>
        new(sql.Context(tenant), new SqlServerFixture.FixedTenant(tenant), new FixedUser(actor), audit ?? new AuditLog(sql.Audit(tenant)));

    [Fact]
    public async Task FlagsDefaultToOff()
    {
        Assert.False(await Flags(tenantA).IsEnabledAsync("never.created"));
        Assert.False(await Flags(null).IsEnabledAsync(Key));
    }

    [Fact]
    public async Task TurningAFlagOffTakesEffectOnTheNextCheck()
    {
        await Flags(tenantA).SetAsync(Key, true, "scheduling-team", "Waitlist for full sessions");
        Assert.True(await Flags(tenantA).IsEnabledAsync(Key));

        await Flags(tenantA).SetAsync(Key, false, "scheduling-team", "Waitlist for full sessions");
        Assert.False(await Flags(tenantA).IsEnabledAsync(Key));
    }

    [Fact]
    public async Task FlagsAreIsolatedPerTenantEvenWithQueryFiltersOff()
    {
        await Flags(tenantA).SetAsync(Key, true, "scheduling-team", "Waitlist for full sessions");
        Assert.False(await Flags(tenantB).IsEnabledAsync(Key));

        await using var db = sql.Context(tenantB);
        Assert.DoesNotContain(await db.FeatureFlags.IgnoreQueryFilters().ToListAsync(), f => f.TenantId == tenantA); // row-level security
    }

    [Fact]
    public async Task EveryChangeIsAuditedWithItsActorAndOwnerIsRecorded()
    {
        await Flags(tenantA).SetAsync(Key, true, "scheduling-team", "Waitlist for full sessions");
        await Flags(tenantA).SetAsync(Key, false, "scheduling-team", "Waitlist for full sessions");

        await using var tenancy = sql.Context(tenantA);
        var flag = await tenancy.FeatureFlags.SingleAsync(f => f.Key == Key);
        Assert.Equal(("scheduling-team", actor, actor), (flag.Owner, flag.CreatedBy, flag.ChangedBy));

        await using var audit = sql.Audit(tenantA);
        var events = await audit.Events.Where(e => e.ResourceId == flag.Id).OrderBy(e => e.OccurredAt).Select(e => new { e.Action, e.ActorUserId }).ToListAsync();
        Assert.Equal(["flag.enable", "flag.disable"], events.Select(e => e.Action));
        Assert.All(events, e => Assert.Equal(actor, e.ActorUserId));
    }

    [Fact]
    public async Task AChangeThatCannotBeAuditedDoesNotHappen()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Flags(tenantA, new ThrowingLog()).SetAsync(Key, true, "scheduling-team", "Waitlist"));
        Assert.False(await Flags(tenantA).IsEnabledAsync(Key));
    }

    [Theory]
    [InlineData("Bad Key", "team", "desc")]
    [InlineData("scheduling.waitlist", " ", "desc")]
    [InlineData("scheduling.waitlist", "team", "")]
    public async Task EveryFlagNeedsAValidKeyOwnerAndDescription(string key, string owner, string description) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Flags(tenantA).SetAsync(key, true, owner, description));

    [Fact]
    public async Task GatedEndpointIsNotFoundWhileTheFlagIsOff()
    {
        await using var app = App(tenantA, _ => { });
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/waitlist")).StatusCode);

        await Flags(tenantA).SetAsync(Key, true, "scheduling-team", "Waitlist for full sessions");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/waitlist")).StatusCode);

        await Flags(tenantA).SetAsync(Key, false, "scheduling-team", "Waitlist for full sessions");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/waitlist")).StatusCode); // no redeploy, no restart
    }

    [Fact]
    public async Task GateRunsBeforeOtherFiltersAndWithoutATenant()
    {
        // .Audited() is declared first; with the flag off the gate still answers before the (broken) audit runs.
        await using var app = App(tenantA, s => s.AddScoped<IAuditLog, ThrowingLog>());
        Assert.Equal(HttpStatusCode.NotFound, (await app.GetTestClient().GetAsync("/waitlist")).StatusCode);

        await using var noTenant = App(null, _ => { });
        Assert.Equal(HttpStatusCode.NotFound, (await noTenant.GetTestClient().GetAsync("/waitlist")).StatusCode);
        Assert.False(handlerRan);
    }

    bool handlerRan;

    WebApplication App(Guid? tenant, Action<IServiceCollection> overrides)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ICurrentTenant>(new SqlServerFixture.FixedTenant(tenant));
        builder.Services.AddSingleton<ICurrentUser>(new FixedUser(actor));
        builder.Services.AddAuditModule(sql.ConnectionString).AddTenancyModule(sql.ConnectionString);
        overrides(builder.Services);
        var app = builder.Build();
        app.MapGet("/waitlist", () => { handlerRan = true; return "synthetic"; }).Audited("waitlist.read", "waitlist").RequireFeature(Key);
        app.StartAsync().GetAwaiter().GetResult();
        return app;
    }

    sealed class FixedUser(Guid id) : ICurrentUser { public Guid? UserId => id; }

    sealed class ThrowingLog : IAuditLog
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct) => throw new InvalidOperationException("audit store down");
    }
}

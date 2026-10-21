using Medvya.Patients.Domain;
using Medvya.Scheduling.Application;
using Medvya.SyntheticData;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-027: the synthetic seed is idempotent, covers every role, is visibly synthetic and never runs in production.</summary>
public class SyntheticSeedTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    async Task<(int Tenants, int Facilities, int Memberships)> Counts(Guid tenantId)
    {
        await using var db = sql.Context(tenantId);
        return (await db.Tenants.CountAsync(), await db.Facilities.CountAsync(), await db.Memberships.CountAsync());
    }

    [Fact]
    public async Task SeedingTwiceGivesTheSameRowsWithEveryRoleMarkedSynthetic()
    {
        await SyntheticSeed.SeedAsync(sql.ConnectionString, "Test", includeUae: true, SqlServerFixture.Keys);
        var india = await Counts(SyntheticSeed.India.Tenant.Id);
        var uae = await Counts(SyntheticSeed.Uae.Tenant.Id);

        await SyntheticSeed.SeedAsync(sql.ConnectionString, "Test", includeUae: true, SqlServerFixture.Keys);
        Assert.Equal(india, await Counts(SyntheticSeed.India.Tenant.Id));
        Assert.Equal(uae, await Counts(SyntheticSeed.Uae.Tenant.Id));
        Assert.Equal((1, 1, Enum.GetValues<Role>().Length), india);

        await using var db = sql.Context(SyntheticSeed.India.Tenant.Id);
        Assert.Equal(Enum.GetValues<Role>().Order(), (await db.Memberships.Select(m => m.Role).ToListAsync()).Order());
        Assert.EndsWith(SyntheticSeed.Marker, (await db.Tenants.SingleAsync()).Name, StringComparison.Ordinal);
        Assert.EndsWith(SyntheticSeed.Marker, (await db.Facilities.SingleAsync()).Name, StringComparison.Ordinal);
        Assert.All(SyntheticSeed.Tenants(includeUae: true).SelectMany(t => t.Members),
            m => Assert.EndsWith(SyntheticSeed.Marker, m.Name, StringComparison.Ordinal));

        // One development sign-in account per cast member, also idempotent.
        await using var identity = sql.Identity();
        var accounts = await identity.Users.ToListAsync();
        Assert.Equal(SyntheticSeed.Tenants(includeUae: true).Sum(t => t.Members.Count), accounts.Count);
        Assert.All(accounts, a => Assert.EndsWith(SyntheticSeed.Marker, a.DisplayName, StringComparison.Ordinal));

        // Schedule templates and the procedure room (MDV-P1-011): valid, seeded once.
        await using var scheduling = sql.Scheduling(SyntheticSeed.India.Tenant.Id);
        Assert.Equal((SyntheticSeed.India.Templates.Count, 1), (await scheduling.Templates.CountAsync(), await scheduling.Resources.CountAsync()));
        Assert.All(SyntheticSeed.Tenants(includeUae: true).SelectMany(t => t.Templates), ScheduleRules.Validate);
        Assert.All(SyntheticSeed.India.Resources, r => Assert.EndsWith(SyntheticSeed.Marker, r.Name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("Prod")]
    [InlineData("")]
    [InlineData(null)]
    public async Task RefusesToRunOutsideDevelopmentStagingOrTest(string? environment)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SyntheticSeed.SeedAsync(sql.ConnectionString, environment, includeUae: true, SqlServerFixture.Keys));
        Assert.Contains("refused", ex.Message, StringComparison.Ordinal);
    }
}

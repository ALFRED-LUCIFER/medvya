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
        await SyntheticSeed.SeedAsync(sql.ConnectionString, "Test", includeUae: true);
        var india = await Counts(SyntheticSeed.India.Tenant.Id);
        var uae = await Counts(SyntheticSeed.Uae.Tenant.Id);

        await SyntheticSeed.SeedAsync(sql.ConnectionString, "Test", includeUae: true);
        Assert.Equal(india, await Counts(SyntheticSeed.India.Tenant.Id));
        Assert.Equal(uae, await Counts(SyntheticSeed.Uae.Tenant.Id));
        Assert.Equal((1, 1, Enum.GetValues<Role>().Length), india);

        await using var db = sql.Context(SyntheticSeed.India.Tenant.Id);
        Assert.Equal(Enum.GetValues<Role>().Order(), (await db.Memberships.Select(m => m.Role).ToListAsync()).Order());
        Assert.EndsWith(SyntheticSeed.Marker, (await db.Tenants.SingleAsync()).Name, StringComparison.Ordinal);
        Assert.EndsWith(SyntheticSeed.Marker, (await db.Facilities.SingleAsync()).Name, StringComparison.Ordinal);
        Assert.All(SyntheticSeed.Tenants(includeUae: true).SelectMany(t => t.Members),
            m => Assert.EndsWith(SyntheticSeed.Marker, m.Name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("Prod")]
    [InlineData("")]
    [InlineData(null)]
    public async Task RefusesToRunOutsideDevelopmentStagingOrTest(string? environment)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SyntheticSeed.SeedAsync(sql.ConnectionString, environment, includeUae: true));
        Assert.Contains("refused", ex.Message, StringComparison.Ordinal);
    }
}

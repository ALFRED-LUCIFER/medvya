using Medvya.Patients.Domain;
using Medvya.Scheduling.Application;
using Medvya.SyntheticData;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-027: the synthetic seed is idempotent, covers every role, is visibly synthetic and never runs in production.</summary>
public class SyntheticSeedTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    async Task<(int Tenants, int Facilities, int Memberships)> Counts(Guid tenantId)
    {
        await using var db = sql.Context(tenantId);
        return (await db.Tenants.CountAsync(), await db.Facilities.CountAsync(), await db.Memberships.CountAsync());
    }

    [Fact]
    public async Task SeedingTwiceGivesTheSameRowsWithEveryRoleMarkedSynthetic()
    {
        await SyntheticSeed.SeedAsync(sql.ConnectionString, "Development", includeUae: true);
        var india = await Counts(SyntheticSeed.India.Tenant.Id);
        var uae = await Counts(SyntheticSeed.Uae.Tenant.Id);

        await SyntheticSeed.SeedAsync(sql.ConnectionString, "Development", includeUae: true);
        Assert.Equal(india, await Counts(SyntheticSeed.India.Tenant.Id));
        Assert.Equal(uae, await Counts(SyntheticSeed.Uae.Tenant.Id));
        Assert.Equal((1, 1, Enum.GetValues<Role>().Length), india);

        await using var db = sql.Context(SyntheticSeed.India.Tenant.Id);
        Assert.Equal(Enum.GetValues<Role>().Order(), (await db.Memberships.Select(m => m.Role).ToListAsync()).Order());
        // #892: each seeded doctor has the licence and profile rows Settings, Doctors lists, attested like a new doctor's.
        var doctors = await db.Memberships.Where(m => m.Role == Role.SoloDoctor || m.Role == Role.HospitalDoctor).Select(m => m.Id).ToListAsync();
        Assert.Equal(doctors.Order(), (await db.DoctorLicences.Select(l => l.MembershipId).ToListAsync()).Order());
        Assert.Equal(doctors.Order(), (await db.DoctorProfiles.Select(p => p.MembershipId).ToListAsync()).Order());
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

    [Fact]
    public async Task SeededIdentifiersAreEncryptedAndReadBackByTheApp()
    {
        await SyntheticSeed.SeedAsync(sql.ConnectionString, "Development", includeUae: true);
        await using var db = sql.Patients(SyntheticSeed.Uae.Tenant.Id);
        Assert.Contains("784-1988-1234567-1", await db.Identifiers.Select(i => i.LookupValue).ToListAsync());
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Test")]
    public async Task OutsideDevelopmentSeedingNeedsKeyVaultAndWritesNothingWithoutIt(string environment)
    {
        var tenant = SyntheticSeed.India.Tenant.Id;
        var before = await Counts(tenant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SyntheticSeed.SeedAsync(sql.ConnectionString, environment, includeUae: true));
        Assert.Equal(before, await Counts(tenant));
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

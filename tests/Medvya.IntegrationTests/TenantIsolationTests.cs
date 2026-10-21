using Medvya.Tenancy.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-010 / P0 exit gate 3: one clinic can never read or write another clinic's rows.</summary>
public class TenantIsolationTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid(), sharedUser = Guid.NewGuid();
    Guid facilityA, facilityB;

    public async Task InitializeAsync()
    {
        facilityA = await Seed(tenantA, "Iyer Family Clinic (synthetic)", "IN");
        facilityB = await Seed(tenantB, "Al Noor Family Clinic (synthetic)", "AE");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    async Task<Guid> Seed(Guid tenant, string name, string country)
    {
        await using var db = sql.Context(tenant); // writing a tenant's rows requires being in that tenant
        var facility = new Facility { Id = Guid.NewGuid(), TenantId = tenant, Name = name, TimeZone = country == "IN" ? "Asia/Kolkata" : "Asia/Dubai" };
        db.Tenants.Add(new Tenant { Id = tenant, Name = name, CountryCode = country, ResidencyRegion = country == "IN" ? "centralindia" : "uaenorth" });
        db.Facilities.Add(facility);
        db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = sharedUser, FacilityId = facility.Id, Role = Role.Receptionist, Status = MembershipStatus.Active });
        await db.SaveChangesAsync();
        return facility.Id;
    }

    [Fact]
    public async Task RowLevelSecurityHidesOtherTenantsEvenWithQueryFiltersOff()
    {
        await using var db = sql.Context(tenantA);
        var facilities = await db.Facilities.IgnoreQueryFilters().ToListAsync();
        var tenants = await db.Tenants.IgnoreQueryFilters().ToListAsync();
        var raw = await db.Database.SqlQuery<Guid>($"SELECT TenantId AS Value FROM tenancy.Facilities").ToListAsync();

        Assert.All(facilities, f => Assert.Equal(tenantA, f.TenantId));
        Assert.Contains(facilities, f => f.Id == facilityA);
        Assert.DoesNotContain(facilities, f => f.Id == facilityB);
        Assert.DoesNotContain(tenants, t => t.Id == tenantB);
        Assert.All(raw, id => Assert.Equal(tenantA, id)); // hand-written SQL is filtered too
    }

    [Fact]
    public async Task WritingIntoAnotherTenantIsBlocked()
    {
        await using var db = sql.Context(tenantA);
        db.Facilities.Add(new Facility { Id = Guid.NewGuid(), TenantId = tenantB, Name = "Injected", TimeZone = "UTC" });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.IsType<SqlException>(ex.InnerException);

        await using var db2 = sql.Context(tenantA);
        var move = await Assert.ThrowsAsync<SqlException>(() =>
            db2.Database.ExecuteSqlAsync($"UPDATE tenancy.Facilities SET TenantId = {tenantB} WHERE Id = {facilityA}"));
        Assert.Contains("block predicate", move.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoTenantMeansNoRows()
    {
        await using var db = sql.Context(null);
        Assert.Empty(await db.Facilities.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.Memberships.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task OnePersonInTwoClinicsSeesOnlyTheActiveClinicsMembership()
    {
        await using var inA = sql.Context(tenantA);
        await using var inB = sql.Context(tenantB);
        var a = await inA.Memberships.Where(m => m.UserId == sharedUser).ToListAsync();
        var b = await inB.Memberships.Where(m => m.UserId == sharedUser).ToListAsync();
        Assert.Equal(tenantA, Assert.Single(a).TenantId);
        Assert.Equal(tenantB, Assert.Single(b).TenantId);
    }

    [Fact]
    public async Task PooledConnectionsNeverLeakTenantContext()
    {
        for (var i = 0; i < 100; i++)
        {
            var tenant = i % 2 == 0 ? tenantA : tenantB;
            await using var db = sql.Context(tenant);
            var seen = await db.Facilities.IgnoreQueryFilters().Select(f => f.TenantId).Distinct().ToListAsync();
            Assert.Equal([tenant], seen);
        }
    }
}

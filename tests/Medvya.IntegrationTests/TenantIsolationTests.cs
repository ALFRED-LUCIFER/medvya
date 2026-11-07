using Medvya.Tenancy.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Tenancy.Domain;
using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-010 / P0 exit gate 3: one clinic can never read or write another clinic's rows.</summary>
public class TenantIsolationTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
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
        var raw = await db.Database.SqlQuery<Guid>($"""SELECT "TenantId" AS "Value" FROM tenancy."Facilities" """).ToListAsync();

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
        Assert.IsType<PostgresException>(ex.InnerException);

        await using var db2 = sql.Context(tenantA);
        var move = await Assert.ThrowsAsync<PostgresException>(() =>
            db2.Database.ExecuteSqlAsync($"""UPDATE tenancy."Facilities" SET "TenantId" = {tenantB} WHERE "Id" = {facilityA}"""));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, move.SqlState); // row-level security refuses the row (42501)
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

    [Fact]
    public async Task TwoSavesOfTheSameSettingsRowConflictInsteadOfOverwriting()
    {
        // #889: xmin is the concurrency token, so the save that read the older row loses loudly.
        var id = Guid.NewGuid();
        await using (var seed = sql.Context(tenantA))
        {
            seed.Profiles.Add(new PracticeProfile { Id = id, TenantId = tenantA, Phone = "+91 90000 00001" });
            await seed.SaveChangesAsync();
        }
        await using var first = sql.Context(tenantA);
        await using var second = sql.Context(tenantA);
        var a = await first.Profiles.SingleAsync(p => p.Id == id);
        var b = await second.Profiles.SingleAsync(p => p.Id == id);
        a.Phone = "+91 90000 00002";
        await first.SaveChangesAsync();
        b.Email = "desk@example.test";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task TheDatabaseRefusesAMembershipAtAnotherTenantsFacility()
    {
        // #890: foreign-key checks bypass RLS, so the key itself carries the tenant.
        await using var db = sql.Context(tenantA);
        db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenantA, UserId = Guid.NewGuid(), FacilityId = facilityB, Role = Role.Receptionist, Status = MembershipStatus.Active });
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(refused.InnerException).SqlState);
    }

    [Fact]
    public async Task AContextWithoutATenantClearsSettingsAConnectionKept()
    {
        // #887: even on a connection that skipped the pool reset (deliberately built past the startup guard), no tenant means no rows.
        var cs = new NpgsqlConnectionStringBuilder(sql.ConnectionString) { NoResetOnClose = true, MaxPoolSize = 1, ApplicationName = $"no-reset-{Guid.NewGuid():N}" };
        await using var source = NpgsqlDataSource.Create(cs.ConnectionString);
        await using (var conn = await source.OpenConnectionAsync())
        await using (var set = new NpgsqlCommand("SELECT set_config('medvya.tenant_id', $1, false)", conn) { Parameters = { new() { Value = tenantA.ToString() } } })
            await set.ExecuteNonQueryAsync();
        var none = new PostgresFixture.FixedTenant(null);
        await using var db = new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>().UseNpgsql(source).AddInterceptors(new TenantSessionInterceptor(none)).Options, none);
        Assert.Empty(await db.Facilities.IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData("No Reset On Close=true")]
    [InlineData("Multiplexing=true")]
    [InlineData("Port=6432")]
    public void AConnectionStringThatSharesSessionsIsRefused(string setting) =>
        Assert.Throws<InvalidOperationException>(() => PostgresDataSources.Create($"Host=localhost;Database=medvya;Username=x;Password=y;{setting}", new Azure.Identity.DefaultAzureCredential()));
}

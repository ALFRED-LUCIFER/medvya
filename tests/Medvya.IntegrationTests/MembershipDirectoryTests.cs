using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P0-012 facility picker: a user's memberships are listed across tenants, and row-level security still shows nothing
/// beyond that user's own active memberships and the tenants and facilities they belong to.
/// </summary>
public class MembershipDirectoryTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    readonly Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid(), tenantC = Guid.NewGuid();
    readonly Guid user = Guid.NewGuid(), colleague = Guid.NewGuid();
    Guid facilityA, otherFacilityA, facilityB, facilityC;

    sealed class FixedUser(Guid id) : ICurrentUser { public Guid? UserId => id; }

    public async Task InitializeAsync()
    {
        (facilityA, otherFacilityA) = await Seed(tenantA, "Iyer Family Clinic (synthetic)", (user, MembershipStatus.Active), (colleague, MembershipStatus.Active));
        (facilityB, _) = await Seed(tenantB, "Lakeside Clinic (synthetic)", (user, MembershipStatus.Active));
        (facilityC, _) = await Seed(tenantC, "Hillview Clinic (synthetic)", (user, MembershipStatus.Suspended));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // Each tenant has two facilities; the user works at the first only; the colleague at the second.
    async Task<(Guid, Guid)> Seed(Guid tenant, string name, params (Guid User, MembershipStatus Status)[] people)
    {
        await using var db = sql.Context(tenant);
        var first = new Facility { Id = Guid.NewGuid(), TenantId = tenant, Name = "Main (synthetic)", TimeZone = "Asia/Kolkata" };
        var second = new Facility { Id = Guid.NewGuid(), TenantId = tenant, Name = "Annexe (synthetic)", TimeZone = "Asia/Kolkata" };
        db.Tenants.Add(new Tenant { Id = tenant, Name = name, CountryCode = "IN", ResidencyRegion = "centralindia", IdleTimeoutMinutes = 20 });
        db.Facilities.AddRange(first, second);
        db.Memberships.AddRange(people.Select((p, i) => new Membership
        {
            Id = Guid.NewGuid(), TenantId = tenant, UserId = p.User, FacilityId = i == 0 ? first.Id : second.Id, Role = Role.Nurse, Status = p.Status,
        }));
        db.FeatureFlags.Add(new FeatureFlag { Id = Guid.NewGuid(), TenantId = tenant, Key = "demo.flag", Enabled = true, Owner = "platform", Description = "test" });
        await db.SaveChangesAsync();
        return (first.Id, second.Id);
    }

    [Fact]
    public async Task DirectoryListsTheUsersActiveMembershipsInEveryTenantAndNothingElse()
    {
        var choices = await new MembershipDirectory(sql.ConnectionString, new FixedUser(user)).ForCurrentUserAsync();
        Assert.Equal(new[] { facilityA, facilityB }.Order(), choices.Select(c => c.FacilityId).Order());
        Assert.Contains(choices, c => c.TenantId == tenantA && c.TenantName == "Iyer Family Clinic (synthetic)" && c.FacilityName == "Main (synthetic)" && c.IdleTimeoutMinutes == 20);
        Assert.DoesNotContain(choices, c => c.TenantId == tenantC); // suspended
    }

    static async Task<List<Guid>> Ids(SqlConnection conn, string sqlText)
    {
        await using var cmd = new SqlCommand(sqlText, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var ids = new List<Guid>();
        while (await r.ReadAsync()) ids.Add(r.GetGuid(0));
        return ids;
    }

    [Fact]
    public async Task UserContextAloneShowsOnlyOwnActiveMembershipsTheirFacilitiesAndTenants()
    {
        // As the application role, hand-written SQL, no EF filters: row-level security alone decides.
        await using var conn = await sql.OpenAs("app_user", userId: user);
        var memberships = await Ids(conn, "SELECT UserId FROM tenancy.Memberships");
        var facilities = await Ids(conn, "SELECT Id FROM tenancy.Facilities");
        var tenants = await Ids(conn, "SELECT Id FROM tenancy.Tenants");

        Assert.Equal(2, memberships.Count);
        Assert.All(memberships, u => Assert.Equal(user, u)); // never a colleague's membership
        Assert.Equal(new[] { facilityA, facilityB }.Order(), facilities.Order()); // not the annexe, not the suspended tenant's facility
        Assert.DoesNotContain(otherFacilityA, facilities);
        Assert.DoesNotContain(facilityC, facilities);
        Assert.Equal(new[] { tenantA, tenantB }.Order(), tenants.Order());
        Assert.Empty(await Ids(conn, "SELECT Id FROM tenancy.FeatureFlags")); // other tables stay tenant-only
    }

    [Fact]
    public async Task UserContextNeverWidensATenantScopedConnection()
    {
        await using var conn = await sql.OpenAs("app_user", tenantB, userId: user);
        Assert.All(await Ids(conn, "SELECT TenantId FROM tenancy.Memberships"), t => Assert.Equal(tenantB, t));
        Assert.All(await Ids(conn, "SELECT TenantId FROM tenancy.Facilities"), t => Assert.Equal(tenantB, t));
        Assert.Equal([tenantB], await Ids(conn, "SELECT Id FROM tenancy.Tenants"));
    }

    [Fact]
    public async Task UserContextCannotWriteAnything()
    {
        await using var conn = await sql.OpenAs("app_user", userId: user);
        await using var insert = new SqlCommand(
            "INSERT INTO tenancy.Memberships (Id, TenantId, UserId, FacilityId, Role, Status) VALUES (NEWID(), @t, @u, @f, N'PracticeAdmin', N'Active')", conn);
        insert.Parameters.AddWithValue("@t", tenantA);
        insert.Parameters.AddWithValue("@u", user);
        insert.Parameters.AddWithValue("@f", otherFacilityA);
        var ex = await Assert.ThrowsAsync<SqlException>(() => insert.ExecuteNonQueryAsync());
        Assert.Contains("block predicate", ex.Message, StringComparison.Ordinal);

        await using var promote = new SqlCommand("UPDATE tenancy.Memberships SET Role = N'PracticeAdmin' WHERE UserId = @u", conn);
        promote.Parameters.AddWithValue("@u", user);
        Assert.ThrowsAny<SqlException>(() => promote.ExecuteNonQuery());
    }
}

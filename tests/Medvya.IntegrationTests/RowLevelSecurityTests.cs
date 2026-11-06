using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-034: the two layers of tenant isolation each hold alone, and no table or role can quietly fall outside them.</summary>
public class RowLevelSecurityTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    // Tables that hold a TenantId yet are read with no tenant on purpose: ids only, no clinic data.
    // Sessions are looked up by token before a tenant is known; ledger.Chains lists which tenant has a hash chain, for the digest job (MDV-P0-036).
    static readonly string[] IdsOnly = ["tenancy.TenantIndex", "scheduling.OutboxTenants", "identity.Sessions", "ledger.Chains"];

    async Task<List<string>> Rows(string query)
    {
        await using var conn = await sql.OpenAs("dbo");
        await using var cmd = new NpgsqlCommand(query, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await r.ReadAsync()) rows.Add(r.GetString(0));
        return rows;
    }

    [Fact]
    public async Task EveryTableWithATenantColumnHasForcedPoliciesForEveryCommand()
    {
        var tenantTables = await Rows("""
            SELECT n.nspname || '.' || c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'r' AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'hangfire', 'public')
              AND (c.relname = 'Tenants' OR EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'TenantId' AND NOT a.attisdropped))
            """);
        Assert.Contains("tenancy.Tenants", tenantTables);
        Assert.Contains("patients.Patients", tenantTables);

        var unprotected = await Rows("""
            SELECT n.nspname || '.' || c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'r' AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'hangfire', 'public')
              AND (c.relname = 'Tenants' OR EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'TenantId' AND NOT a.attisdropped))
              AND (NOT c.relrowsecurity OR NOT c.relforcerowsecurity
                   OR (SELECT string_agg(p.polcmd::text, '' ORDER BY p.polcmd) FROM pg_policy p WHERE p.polrelid = c.oid) IS DISTINCT FROM 'adrw')
            """);
        Assert.Equal(IdsOnly.Order(), unprotected.Order().ToArray()); // exactly the documented exceptions, nothing else
    }

    [Fact]
    public async Task NoMedvyaRoleCanBypassRowLevelSecurityOrChangeTheSchema()
    {
        Assert.Empty(await Rows("SELECT rolname FROM pg_roles WHERE rolname LIKE 'medvya\\_%' AND (rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole)"));
        Assert.Empty(await Rows("""
            SELECT r.rolname || ' ' || s.nspname FROM pg_roles r CROSS JOIN pg_namespace s
            WHERE r.rolname IN ('medvya_app', 'medvya_reporting') AND s.nspname NOT LIKE 'pg\_%' AND s.nspname <> 'information_schema'
              AND has_schema_privilege(r.oid, s.oid, 'CREATE')
            """));
        Assert.Empty(await Rows("SELECT n.nspname || '.' || c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
            "WHERE c.relkind = 'r' AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'public') AND pg_get_userbyid(c.relowner) IN ('medvya_app', 'medvya_reporting')"));
    }

    [Fact]
    public async Task TheQueryFilterAloneStillHidesOtherTenants() // layer one with layer two out of the way (superuser bypasses RLS)
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        foreach (var t in new[] { a, b })
        {
            await using var seed = sql.Context(t);
            seed.Tenants.Add(new Tenant { Id = t, Name = "Iyer Family Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            await seed.SaveChangesAsync();
        }

        var options = new DbContextOptionsBuilder<TenancyDbContext>().UseNpgsql(sql.AdminConnectionString).Options;
        await using var db = new TenancyDbContext(options, new PostgresFixture.FixedTenant(a));
        Assert.Equal([a], await db.Tenants.Select(t => t.Id).ToListAsync()); // filtered by EF
        Assert.Contains(b, await db.Tenants.IgnoreQueryFilters().Select(t => t.Id).ToListAsync()); // proves RLS really was out of the way
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>
/// Layer two of tenant isolation (ADR-005, MDV-P0-034): the one place that writes a table's row-level security. Every migration that
/// adds a table with a tenant column calls <see cref="Tenant"/> for it, so the policies cannot drift between tables; the
/// architecture/integration tests fail if a table with a TenantId has no forced policy.
/// A connection with no medvya.tenant_id sees and writes nothing. FORCE makes the table owner subject to the policies too.
/// </summary>
public static class RowLevelSecurity
{
    /// <summary>The connection's tenant, or NULL (which matches no row). The setting is '' once a pooled connection has been reset.</summary>
    public const string CurrentTenant = "nullif(current_setting('medvya.tenant_id', true), '')::uuid";

    /// <summary>The signed-in user, set only by MembershipDirectory (the facility picker).</summary>
    public const string CurrentUser = "nullif(current_setting('medvya.user_id', true), '')::uuid";

    /// <summary>The facility-picker branch applies only when no tenant is set, so it never widens a tenant-scoped connection.</summary>
    public const string NoTenantSet = "nullif(current_setting('medvya.tenant_id', true), '') IS NULL";

    /// <summary>Forces RLS on <paramref name="schema"/>.<paramref name="table"/> and allows only rows whose <paramref name="column"/> is the connection's tenant.</summary>
    /// <param name="alsoReadable">An extra condition for seeing rows (the facility picker). Every write still has to pass the tenant check,
    /// so an UPDATE of a row seen this way fails loudly, and DELETE sees tenant rows only.</param>
    /// <param name="sharedRows">Rows whose tenant column is NULL are a shared catalogue (the formulary's starter list): every tenant reads them,
    /// and no tenant can write them (the write checks still demand the connection's own tenant).</param>
    public static void Tenant(this MigrationBuilder migration, string schema, string table, string column = "TenantId", string? alsoReadable = null, bool sharedRows = false)
    {
        var t = $"\"{schema}\".\"{table}\"";
        var own = $"\"{column}\" = {CurrentTenant}";
        var read = alsoReadable is null ? own : $"({own}) OR ({NoTenantSet} AND {alsoReadable})";
        if (sharedRows) read = $"({read}) OR \"{column}\" IS NULL";
        migration.Sql($"""
            ALTER TABLE {t} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {t} FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_select ON {t} FOR SELECT USING ({read});
            CREATE POLICY tenant_insert ON {t} FOR INSERT WITH CHECK ({own});
            CREATE POLICY tenant_update ON {t} FOR UPDATE USING ({read}) WITH CHECK ({own});
            CREATE POLICY tenant_delete ON {t} FOR DELETE USING ({own});
            """);
    }
}

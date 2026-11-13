using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MDV-P0-015: with no tenant set, a user also reads their own suspended or offboarded membership rows (never another user's), so a member
    /// removed from their only clinic gets F-05b instead of the setup wizard. Tenants and facilities stay readable through active memberships only.
    /// Forward-only: no Down.
    /// </summary>
    [DbContext(typeof(TenancyDbContext))]
    [Migration("20261113100000_MembershipOwnEnded")]
    public partial class MembershipOwnEnded : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var own = $"\"TenantId\" = {RowLevelSecurity.CurrentTenant}";
            var read = $"({own}) OR ({RowLevelSecurity.NoTenantSet} AND \"UserId\" = {RowLevelSecurity.CurrentUser})";
            migrationBuilder.Sql($"""
                DROP POLICY tenant_select ON tenancy."Memberships";
                DROP POLICY tenant_update ON tenancy."Memberships";
                CREATE POLICY tenant_select ON tenancy."Memberships" FOR SELECT USING ({read});
                CREATE POLICY tenant_update ON tenancy."Memberships" FOR UPDATE USING ({read}) WITH CHECK ({own});
                """);
        }
    }
}

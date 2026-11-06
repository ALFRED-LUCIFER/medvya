using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Tenant("billing", "DocSequences");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY tenant_select ON billing."DocSequences"; DROP POLICY tenant_insert ON billing."DocSequences"; DROP POLICY tenant_update ON billing."DocSequences"; DROP POLICY tenant_delete ON billing."DocSequences"; ALTER TABLE billing."DocSequences" NO FORCE ROW LEVEL SECURITY; ALTER TABLE billing."DocSequences" DISABLE ROW LEVEL SECURITY;""");
        }
    }
}

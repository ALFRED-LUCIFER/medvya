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
            // A BEFORE trigger runs ahead of the row-level-security check, so moving a series to another tenant was refused by the trigger's
            // message instead of by RLS. AFTER keeps the same rule and lets RLS refuse a foreign tenant first.
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_DocSequences_OnlyMoveForward" ON billing."DocSequences";
                CREATE TRIGGER "TR_DocSequences_OnlyMoveForward" AFTER INSERT OR UPDATE ON billing."DocSequences"
                    FOR EACH ROW EXECUTE FUNCTION billing."DocSequences_OnlyMoveForward"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY tenant_select ON billing."DocSequences"; DROP POLICY tenant_insert ON billing."DocSequences"; DROP POLICY tenant_update ON billing."DocSequences"; DROP POLICY tenant_delete ON billing."DocSequences"; ALTER TABLE billing."DocSequences" NO FORCE ROW LEVEL SECURITY; ALTER TABLE billing."DocSequences" DISABLE ROW LEVEL SECURITY;""");
        }
    }
}

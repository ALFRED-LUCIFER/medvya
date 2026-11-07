using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MembershipFacilityTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_Facilities_FacilityId",
                schema: "tenancy",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_FacilityId",
                schema: "tenancy",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Facilities_TenantId",
                schema: "tenancy",
                table: "Facilities");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Facilities_TenantId_Id",
                schema: "tenancy",
                table: "Facilities",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_TenantId_FacilityId",
                schema: "tenancy",
                table: "Memberships",
                columns: new[] { "TenantId", "FacilityId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_Facilities_TenantId_FacilityId",
                schema: "tenancy",
                table: "Memberships",
                columns: new[] { "TenantId", "FacilityId" },
                principalSchema: "tenancy",
                principalTable: "Facilities",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // The facility picker reads a facility through a membership of the same tenant only (#890).
            FacilityPicker(migrationBuilder, "m.\"TenantId\" = \"Facilities\".\"TenantId\" AND ");
        }

        static void FacilityPicker(MigrationBuilder migrationBuilder, string sameTenant)
        {
            var own = $"\"TenantId\" = {RowLevelSecurity.CurrentTenant}";
            var read = $"({own}) OR ({RowLevelSecurity.NoTenantSet} AND EXISTS (SELECT 1 FROM tenancy.\"Memberships\" m WHERE {sameTenant}m.\"FacilityId\" = \"Facilities\".\"Id\" AND m.\"UserId\" = {RowLevelSecurity.CurrentUser} AND m.\"Status\" = 'Active'))";
            migrationBuilder.Sql($"""
                DROP POLICY tenant_select ON tenancy."Facilities";
                DROP POLICY tenant_update ON tenancy."Facilities";
                CREATE POLICY tenant_select ON tenancy."Facilities" FOR SELECT USING ({read});
                CREATE POLICY tenant_update ON tenancy."Facilities" FOR UPDATE USING ({read}) WITH CHECK ({own});
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            FacilityPicker(migrationBuilder, "");

            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_Facilities_TenantId_FacilityId",
                schema: "tenancy",
                table: "Memberships");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_TenantId_FacilityId",
                schema: "tenancy",
                table: "Memberships");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Facilities_TenantId_Id",
                schema: "tenancy",
                table: "Facilities");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_FacilityId",
                schema: "tenancy",
                table: "Memberships",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_TenantId",
                schema: "tenancy",
                table: "Facilities",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_Facilities_FacilityId",
                schema: "tenancy",
                table: "Memberships",
                column: "FacilityId",
                principalSchema: "tenancy",
                principalTable: "Facilities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}

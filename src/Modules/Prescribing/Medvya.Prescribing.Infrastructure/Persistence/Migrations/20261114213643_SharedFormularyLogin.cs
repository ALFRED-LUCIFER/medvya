using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SharedFormularyLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RegisterCode",
                schema: "prescribing",
                table: "FormularyItems",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegisterRelease",
                schema: "prescribing",
                table: "FormularyItems",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormularyItems_Country_RegisterCode",
                schema: "prescribing",
                table: "FormularyItems",
                columns: new[] { "Country", "RegisterCode" },
                unique: true,
                filter: "\"RegisterCode\" IS NOT NULL");

            // MDV-P5-064: the console's medicine-list login (medvya_formulary, roles.sql and table-rights.sql) reads, adds and updates the country's shared
            // list, the rows with no tenant, which the tenant policies let no clinic write. The restrictive policy is ANDed with every permissive one, so
            // even a session of this login that sets medvya.tenant_id never reaches that clinic's own rows. The clinic roles are not members: unchanged.
            migrationBuilder.Sql("""
                CREATE POLICY shared_list_insert ON "prescribing"."FormularyItems" FOR INSERT TO medvya_formulary WITH CHECK ("TenantId" IS NULL);
                CREATE POLICY shared_list_update ON "prescribing"."FormularyItems" FOR UPDATE TO medvya_formulary USING ("TenantId" IS NULL) WITH CHECK ("TenantId" IS NULL);
                CREATE POLICY shared_list_only ON "prescribing"."FormularyItems" AS RESTRICTIVE FOR ALL TO medvya_formulary USING ("TenantId" IS NULL) WITH CHECK ("TenantId" IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY shared_list_only ON "prescribing"."FormularyItems";
                DROP POLICY shared_list_update ON "prescribing"."FormularyItems";
                DROP POLICY shared_list_insert ON "prescribing"."FormularyItems";
                """);

            migrationBuilder.DropIndex(
                name: "IX_FormularyItems_Country_RegisterCode",
                schema: "prescribing",
                table: "FormularyItems");

            migrationBuilder.DropColumn(
                name: "RegisterCode",
                schema: "prescribing",
                table: "FormularyItems");

            migrationBuilder.DropColumn(
                name: "RegisterRelease",
                schema: "prescribing",
                table: "FormularyItems");
        }
    }
}

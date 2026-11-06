using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.CreateTable(
                name: "DocSequences",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FinancialYear = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocSequences", x => x.Id);
                    table.CheckConstraint("CK_DocSequences_NextNumber", "\"NextNumber\" >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocSequences_TenantId_CountryCode_DocumentType_FinancialYea~",
                schema: "billing",
                table: "DocSequences",
                columns: new[] { "TenantId", "CountryCode", "DocumentType", "FinancialYear", "Prefix" },
                unique: true);
            // A legal number series only moves forward, one number at a time. The app role may UPDATE the series (DocumentNumbering's
            // upsert needs it), so without this trigger an UPDATE could rewind NextNumber, skip numbers or move a series to another key.
            // A new series holds 1 (nothing issued) or 2 (DocumentNumbering issued number 1).
            migrationBuilder.Sql("""
                CREATE FUNCTION billing."DocSequences_OnlyMoveForward"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        IF NEW."Id" <> OLD."Id" OR NEW."TenantId" <> OLD."TenantId" OR NEW."CountryCode" <> OLD."CountryCode"
                           OR NEW."DocumentType" <> OLD."DocumentType" OR NEW."FinancialYear" <> OLD."FinancialYear" OR NEW."Prefix" <> OLD."Prefix"
                           OR NEW."NextNumber" <> OLD."NextNumber" + 1 THEN
                            RAISE EXCEPTION 'A document number series only moves forward, one number at a time.';
                        END IF;
                    ELSIF NEW."NextNumber" > 2 THEN
                        RAISE EXCEPTION 'A new document number series starts at 1.';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_DocSequences_OnlyMoveForward" BEFORE INSERT OR UPDATE ON billing."DocSequences"
                    FOR EACH ROW EXECUTE FUNCTION billing."DocSequences_OnlyMoveForward"();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_DocSequences_OnlyMoveForward" ON billing."DocSequences";
                DROP FUNCTION billing."DocSequences_OnlyMoveForward"();
                """);
            migrationBuilder.DropTable(
                name: "DocSequences",
                schema: "billing");
        }
    }
}

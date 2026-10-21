using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBilling : Migration
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountryCode = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FinancialYear = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocSequences", x => x.Id);
                    table.CheckConstraint("CK_DocSequences_NextNumber", "[NextNumber] >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocSequences_TenantId_CountryCode_DocumentType_FinancialYear_Prefix",
                schema: "billing",
                table: "DocSequences",
                columns: new[] { "TenantId", "CountryCode", "DocumentType", "FinancialYear", "Prefix" },
                unique: true);

            // Tenant isolation layer 2 of 2 (ADR-005). Own predicate in the security schema, so this module does not depend on
            // other modules' migrations and the migrator needs no SELECT on billing.
            migrationBuilder.Sql("""
                CREATE FUNCTION security.fn_billing_tenant_access(@tenant_id uniqueidentifier)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS allowed WHERE @tenant_id = CAST(SESSION_CONTEXT(N'tenant_id') AS uniqueidentifier);
                """);
            migrationBuilder.Sql("""
                CREATE SECURITY POLICY security.billing_tenant_isolation
                    ADD FILTER PREDICATE security.fn_billing_tenant_access(TenantId) ON billing.DocSequences,
                    ADD BLOCK PREDICATE security.fn_billing_tenant_access(TenantId) ON billing.DocSequences
                WITH (STATE = ON);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY security.billing_tenant_isolation;");
            migrationBuilder.Sql("DROP FUNCTION security.fn_billing_tenant_access;");

            migrationBuilder.DropTable(
                name: "DocSequences",
                schema: "billing");
        }
    }
}

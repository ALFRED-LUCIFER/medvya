using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TermsAcceptances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Append-only ledger table like patients.ConsentReceipts (ADR-006): UPDATE and DELETE are rejected for every principal.
            // EF Core cannot express ledger tables; the columns match TermsAcceptance.
            migrationBuilder.Sql("""
                CREATE TABLE tenancy.TermsAcceptances (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_TermsAcceptances PRIMARY KEY,
                    TenantId uniqueidentifier NOT NULL CONSTRAINT FK_TermsAcceptances_Tenants_TenantId REFERENCES tenancy.Tenants (Id),
                    TermsVersion nvarchar(32) NOT NULL,
                    AcceptedAt datetimeoffset NOT NULL,
                    AcceptedBy uniqueidentifier NOT NULL
                ) WITH (LEDGER = ON (APPEND_ONLY = ON));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TermsAcceptances_TenantId_TermsVersion",
                schema: "tenancy",
                table: "TermsAcceptances",
                columns: new[] { "TenantId", "TermsVersion" });

            // Tenant isolation layer 2 of 2 (ADR-005): same row-level security policy as the other tenancy tables.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.TermsAcceptances,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.TermsAcceptances;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    DROP FILTER PREDICATE ON tenancy.TermsAcceptances,
                    DROP BLOCK PREDICATE ON tenancy.TermsAcceptances;
                """);
            migrationBuilder.DropTable(
                name: "TermsAcceptances",
                schema: "tenancy");
        }
    }
}

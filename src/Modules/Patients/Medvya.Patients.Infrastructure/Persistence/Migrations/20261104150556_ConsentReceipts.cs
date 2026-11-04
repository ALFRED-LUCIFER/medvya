using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConsentReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Append-only ledger table like audit.AuditEvents (ADR-006): SQL Server rejects UPDATE and DELETE for every principal, so a
            // receipt can only be superseded by a newer one. EF Core cannot express ledger tables; the columns match ConsentReceipt.
            migrationBuilder.Sql("""
                CREATE TABLE patients.ConsentReceipts (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_ConsentReceipts PRIMARY KEY,
                    TenantId uniqueidentifier NOT NULL,
                    PatientId uniqueidentifier NOT NULL CONSTRAINT FK_ConsentReceipts_Patients_PatientId REFERENCES patients.Patients (Id),
                    Type nvarchar(24) NOT NULL,
                    Channel nvarchar(12) NULL,
                    PolicyVersion nvarchar(32) NOT NULL,
                    Jurisdiction nvarchar(8) NOT NULL,
                    Granted bit NOT NULL,
                    DecidedAt datetimeoffset NOT NULL,
                    DecidedBy uniqueidentifier NULL
                ) WITH (LEDGER = ON (APPEND_ONLY = ON));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ConsentReceipts_PatientId",
                schema: "patients",
                table: "ConsentReceipts",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentReceipts_TenantId_PatientId_Type_DecidedAt",
                schema: "patients",
                table: "ConsentReceipts",
                columns: new[] { "TenantId", "PatientId", "Type", "DecidedAt" });

            // Tenant isolation layer 2 of 2 (ADR-005) on the module's existing policy.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.patients_tenant_isolation
                    ADD FILTER PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.ConsentReceipts,
                    ADD BLOCK PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.ConsentReceipts;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.patients_tenant_isolation
                    DROP FILTER PREDICATE ON patients.ConsentReceipts,
                    DROP BLOCK PREDICATE ON patients.ConsentReceipts;
                """);
            migrationBuilder.DropTable(
                name: "ConsentReceipts",
                schema: "patients");
        }
    }
}

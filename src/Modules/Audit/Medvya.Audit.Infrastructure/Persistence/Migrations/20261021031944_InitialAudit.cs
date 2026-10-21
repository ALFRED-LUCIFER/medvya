using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            // Append-only ledger table (ADR-006): SQL Server rejects UPDATE and DELETE for every principal and keeps
            // cryptographic digests, so tampering is impossible through SQL and detectable at the storage level.
            // EF Core cannot express ledger tables, hence raw SQL; the column set matches the AuditEvent model.
            // Ledger verification (sp_verify_database_ledger) needs snapshot isolation; Azure SQL has it on, SQL Server does not.
            migrationBuilder.Sql("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;", suppressTransaction: true);
            migrationBuilder.Sql("""
                CREATE TABLE audit.AuditEvents (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_AuditEvents PRIMARY KEY,
                    TenantId uniqueidentifier NOT NULL,
                    ActorUserId uniqueidentifier NULL,
                    Action nvarchar(64) NOT NULL,
                    ResourceType nvarchar(64) NOT NULL,
                    ResourceId uniqueidentifier NULL,
                    PatientId uniqueidentifier NULL,
                    Purpose nvarchar(32) NOT NULL,
                    Outcome nvarchar(16) NOT NULL,
                    OccurredAt datetimeoffset NOT NULL,
                    ClientIp nvarchar(45) NULL,
                    CorrelationId nvarchar(64) NULL
                ) WITH (LEDGER = ON (APPEND_ONLY = ON));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TenantId_PatientId_OccurredAt",
                schema: "audit",
                table: "AuditEvents",
                columns: new[] { "TenantId", "PatientId", "OccurredAt" });

            // Tenant isolation (ADR-005): each clinic sees and writes only its own audit events.
            // The module keeps its own predicate so this migration does not depend on Tenancy's.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit.fn_tenant_access(@tenant_id uniqueidentifier)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS allowed WHERE @tenant_id = CAST(SESSION_CONTEXT(N'tenant_id') AS uniqueidentifier);
                """);
            migrationBuilder.Sql("""
                CREATE SECURITY POLICY audit.tenant_isolation
                    ADD FILTER PREDICATE audit.fn_tenant_access(TenantId) ON audit.AuditEvents,
                    ADD BLOCK PREDICATE audit.fn_tenant_access(TenantId) ON audit.AuditEvents AFTER INSERT
                WITH (STATE = ON);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY audit.tenant_isolation;");
            migrationBuilder.Sql("DROP FUNCTION audit.fn_tenant_access;");

            migrationBuilder.DropTable(
                name: "AuditEvents",
                schema: "audit");
        }
    }
}

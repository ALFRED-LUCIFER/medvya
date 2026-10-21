using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.CreateTable(
                name: "Resources",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SlotMinutes = table.Column<int>(type: "int", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Breaks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Hours = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VisitLengths = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Resources_TenantId_FacilityId_Name",
                schema: "scheduling",
                table: "Resources",
                columns: new[] { "TenantId", "FacilityId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Templates_TenantId_FacilityId_CalendarId",
                schema: "scheduling",
                table: "Templates",
                columns: new[] { "TenantId", "FacilityId", "CalendarId" },
                unique: true);

            // Tenant isolation layer 2 of 2 (ADR-005), with this module's own predicate in the security schema.
            migrationBuilder.Sql("""
                CREATE FUNCTION security.fn_scheduling_tenant_access(@tenant_id uniqueidentifier)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS allowed WHERE @tenant_id = CAST(SESSION_CONTEXT(N'tenant_id') AS uniqueidentifier);
                """);
            migrationBuilder.Sql("""
                CREATE SECURITY POLICY security.scheduling_tenant_isolation
                    ADD FILTER PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.Resources,
                    ADD BLOCK PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.Resources,
                    ADD FILTER PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.Templates,
                    ADD BLOCK PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.Templates
                WITH (STATE = ON);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY security.scheduling_tenant_isolation;");
            migrationBuilder.Sql("DROP FUNCTION security.fn_scheduling_tenant_access;");

            migrationBuilder.DropTable(
                name: "Resources",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "Templates",
                schema: "scheduling");
        }
    }
}

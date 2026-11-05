using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduleAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduleExceptions",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Start = table.Column<TimeOnly>(type: "time", nullable: true),
                    End = table.Column<TimeOnly>(type: "time", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleExceptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleExceptions_TenantId_FacilityId_CalendarId_FromDate",
                schema: "scheduling",
                table: "ScheduleExceptions",
                columns: new[] { "TenantId", "FacilityId", "CalendarId", "FromDate" });

            // Tenant isolation layer 2 of 2 for the new table, on the module's existing policy (InitialScheduling).
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.scheduling_tenant_isolation
                    ADD FILTER PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.ScheduleExceptions,
                    ADD BLOCK PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.ScheduleExceptions;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.scheduling_tenant_isolation
                    DROP FILTER PREDICATE ON scheduling.ScheduleExceptions, DROP BLOCK PREDICATE ON scheduling.ScheduleExceptions;
                """);

            migrationBuilder.DropTable(
                name: "ScheduleExceptions",
                schema: "scheduling");
        }
    }
}

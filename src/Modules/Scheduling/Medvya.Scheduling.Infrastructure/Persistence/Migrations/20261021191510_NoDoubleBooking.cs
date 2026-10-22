using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NoDoubleBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduleSlots",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleSlots_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "scheduling",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_AppointmentId",
                schema: "scheduling",
                table: "ScheduleSlots",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_CalendarId_FacilityId_SlotStart",
                schema: "scheduling",
                table: "ScheduleSlots",
                columns: new[] { "CalendarId", "FacilityId", "SlotStart" },
                unique: true);

            // Tenant isolation layer 2 of 2. No backfill: the migration role cannot read module data, and no environment holds
            // appointments yet (staging is not deployed, MDV-P0-006).
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.scheduling_tenant_isolation
                    ADD FILTER PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.ScheduleSlots,
                    ADD BLOCK PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.ScheduleSlots;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.scheduling_tenant_isolation
                    DROP FILTER PREDICATE ON scheduling.ScheduleSlots, DROP BLOCK PREDICATE ON scheduling.ScheduleSlots;
                """);

            migrationBuilder.DropTable(
                name: "ScheduleSlots",
                schema: "scheduling");
        }
    }
}

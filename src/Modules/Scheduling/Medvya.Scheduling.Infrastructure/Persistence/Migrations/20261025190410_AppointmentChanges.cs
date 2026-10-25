using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                schema: "scheduling",
                table: "VisitStatusEvents",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                schema: "scheduling",
                table: "VisitStatusEvents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppointmentReschedules",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromCalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FromEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ToCalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ToEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentReschedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentReschedules_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "scheduling",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReschedules_AppointmentId",
                schema: "scheduling",
                table: "AppointmentReschedules",
                column: "AppointmentId");

            // Tenant isolation layer 2 of 2 for the new table, on the module's existing policy (InitialScheduling).
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.scheduling_tenant_isolation
                    ADD FILTER PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.AppointmentReschedules,
                    ADD BLOCK PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.AppointmentReschedules;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.scheduling_tenant_isolation
                    DROP FILTER PREDICATE ON scheduling.AppointmentReschedules, DROP BLOCK PREDICATE ON scheduling.AppointmentReschedules;
                """);

            migrationBuilder.DropTable(
                name: "AppointmentReschedules",
                schema: "scheduling");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                schema: "scheduling",
                table: "VisitStatusEvents");

            migrationBuilder.DropColumn(
                name: "Note",
                schema: "scheduling",
                table: "VisitStatusEvents");
        }
    }
}

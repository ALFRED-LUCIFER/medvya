using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Appointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appointments",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VisitType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    End = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReasonCategory = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReasonText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BookedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BookedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEvents",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SlotHolds",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VisitType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    End = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    HeldBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotHolds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VisitStatusEvents",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitStatusEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitStatusEvents_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "scheduling",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_FacilityId_CalendarId_Start",
                schema: "scheduling",
                table: "Appointments",
                columns: new[] { "TenantId", "FacilityId", "CalendarId", "Start" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_PatientId",
                schema: "scheduling",
                table: "Appointments",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_PublishedAt_OccurredAt",
                schema: "scheduling",
                table: "OutboxEvents",
                columns: new[] { "PublishedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SlotHolds_TenantId_FacilityId_CalendarId_Start",
                schema: "scheduling",
                table: "SlotHolds",
                columns: new[] { "TenantId", "FacilityId", "CalendarId", "Start" });

            migrationBuilder.CreateIndex(
                name: "IX_SlotHolds_TenantId_HeldBy",
                schema: "scheduling",
                table: "SlotHolds",
                columns: new[] { "TenantId", "HeldBy" });

            migrationBuilder.CreateIndex(
                name: "IX_VisitStatusEvents_AppointmentId",
                schema: "scheduling",
                table: "VisitStatusEvents",
                column: "AppointmentId");

            // Tenant isolation layer 2 of 2 for the new tables, on the module's existing policy (InitialScheduling).
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.scheduling_tenant_isolation
                    ADD FILTER PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.Appointments,
                    ADD BLOCK PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.Appointments,
                    ADD FILTER PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.SlotHolds,
                    ADD BLOCK PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.SlotHolds,
                    ADD FILTER PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.VisitStatusEvents,
                    ADD BLOCK PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.VisitStatusEvents,
                    ADD FILTER PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.OutboxEvents,
                    ADD BLOCK PREDICATE security.fn_scheduling_tenant_access(TenantId) ON scheduling.OutboxEvents;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.scheduling_tenant_isolation
                    DROP FILTER PREDICATE ON scheduling.Appointments, DROP BLOCK PREDICATE ON scheduling.Appointments,
                    DROP FILTER PREDICATE ON scheduling.SlotHolds, DROP BLOCK PREDICATE ON scheduling.SlotHolds,
                    DROP FILTER PREDICATE ON scheduling.VisitStatusEvents, DROP BLOCK PREDICATE ON scheduling.VisitStatusEvents,
                    DROP FILTER PREDICATE ON scheduling.OutboxEvents, DROP BLOCK PREDICATE ON scheduling.OutboxEvents;
                """);

            migrationBuilder.DropTable(
                name: "OutboxEvents",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "SlotHolds",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "VisitStatusEvents",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "Appointments",
                schema: "scheduling");
        }
    }
}

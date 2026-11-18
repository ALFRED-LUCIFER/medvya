using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChairsAndProcedureLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_FacilityId_Start",
                schema: "scheduling",
                table: "Appointments");

            migrationBuilder.AddColumn<Guid>(
                name: "ResourceId",
                schema: "scheduling",
                table: "SlotHolds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResourceId",
                schema: "scheduling",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_FacilityId_ResourceId_Start",
                schema: "scheduling",
                table: "Appointments",
                columns: new[] { "TenantId", "FacilityId", "ResourceId", "Start" },
                filter: "\"ResourceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_FacilityId_Start",
                schema: "scheduling",
                table: "Appointments",
                columns: new[] { "TenantId", "FacilityId", "Start" })
                .Annotation("Npgsql:IndexInclude", new[] { "End", "CalendarId", "State", "PatientId", "VisitType", "ResourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_FacilityId_ResourceId_Start",
                schema: "scheduling",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_FacilityId_Start",
                schema: "scheduling",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ResourceId",
                schema: "scheduling",
                table: "SlotHolds");

            migrationBuilder.DropColumn(
                name: "ResourceId",
                schema: "scheduling",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_FacilityId_Start",
                schema: "scheduling",
                table: "Appointments",
                columns: new[] { "TenantId", "FacilityId", "Start" })
                .Annotation("Npgsql:IndexInclude", new[] { "End", "CalendarId", "State", "PatientId", "VisitType" });
        }
    }
}

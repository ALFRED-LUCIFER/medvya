using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NurseIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentId",
                schema: "encounters",
                table: "Observations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FacilityId",
                schema: "encounters",
                table: "Observations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Flag",
                schema: "encounters",
                table: "Observations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FlagKept",
                schema: "encounters",
                table: "Observations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "MergedFromPatientId",
                schema: "encounters",
                table: "Observations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PreviousAt",
                schema: "encounters",
                table: "Observations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousTenths",
                schema: "encounters",
                table: "Observations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedBy",
                schema: "encounters",
                table: "Observations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerifiedBy",
                schema: "encounters",
                table: "Observations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IntakeEscalations",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EscalatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    EscalatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeEscalations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Intakes",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChiefComplaint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    HandedOffBy = table.Column<Guid>(type: "uuid", nullable: true),
                    HandedOffAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Intakes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Observations_TenantId_AppointmentId_Code",
                schema: "encounters",
                table: "Observations",
                columns: new[] { "TenantId", "AppointmentId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeEscalations_TenantId_AppointmentId",
                schema: "encounters",
                table: "IntakeEscalations",
                columns: new[] { "TenantId", "AppointmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeEscalations_TenantId_FacilityId_EscalatedAt",
                schema: "encounters",
                table: "IntakeEscalations",
                columns: new[] { "TenantId", "FacilityId", "EscalatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Intakes_TenantId_AppointmentId",
                schema: "encounters",
                table: "Intakes",
                columns: new[] { "TenantId", "AppointmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Intakes_TenantId_PatientId",
                schema: "encounters",
                table: "Intakes",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.Tenant("encounters", "Intakes");
            migrationBuilder.Tenant("encounters", "IntakeEscalations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntakeEscalations",
                schema: "encounters");

            migrationBuilder.DropTable(
                name: "Intakes",
                schema: "encounters");

            migrationBuilder.DropIndex(
                name: "IX_Observations_TenantId_AppointmentId_Code",
                schema: "encounters",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                schema: "encounters",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "FacilityId",
                schema: "encounters",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "Flag",
                schema: "encounters",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "FlagKept",
                schema: "encounters",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "MergedFromPatientId",
                schema: "encounters",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "PreviousAt",
                schema: "encounters",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "PreviousTenths",
                schema: "encounters",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "RecordedBy",
                schema: "encounters",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "VerifiedBy",
                schema: "encounters",
                table: "Observations");
        }
    }
}

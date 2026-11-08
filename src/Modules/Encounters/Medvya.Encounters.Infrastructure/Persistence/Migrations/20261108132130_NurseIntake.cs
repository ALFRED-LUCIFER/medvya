using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P2-007: nurse intake, vital sign observations and escalations, row-level secured like every tenant table. Forward-only: no Down.</summary>
    public partial class NurseIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateTable(
                name: "Observations",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ValueTenths = table.Column<int>(type: "integer", nullable: false),
                    Flag = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PreviousTenths = table.Column<int>(type: "integer", nullable: true),
                    PreviousAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FlagKept = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    VerifiedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Observations", x => x.Id);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_Observations_TenantId_AppointmentId_Kind",
                schema: "encounters",
                table: "Observations",
                columns: new[] { "TenantId", "AppointmentId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_Observations_TenantId_PatientId_Kind_RecordedAt",
                schema: "encounters",
                table: "Observations",
                columns: new[] { "TenantId", "PatientId", "Kind", "RecordedAt" });

            migrationBuilder.Tenant("encounters", "Intakes");
            migrationBuilder.Tenant("encounters", "Observations");
            migrationBuilder.Tenant("encounters", "IntakeEscalations");
        }
    }
}

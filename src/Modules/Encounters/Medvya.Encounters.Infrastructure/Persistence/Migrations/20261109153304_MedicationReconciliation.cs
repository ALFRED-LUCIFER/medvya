using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MedicationReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                schema: "encounters",
                table: "MedicationStatements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedBy",
                schema: "encounters",
                table: "MedicationStatements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StoppedAt",
                schema: "encounters",
                table: "MedicationStatements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MedicationDecisions",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicineKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FromDose = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NewDose = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationDecisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MedicationReconciliations",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationReconciliations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedicationDecisions_TenantId_EncounterId_MedicineKey_Decide~",
                schema: "encounters",
                table: "MedicationDecisions",
                columns: new[] { "TenantId", "EncounterId", "MedicineKey", "DecidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicationDecisions_TenantId_PatientId",
                schema: "encounters",
                table: "MedicationDecisions",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicationReconciliations_TenantId_EncounterId",
                schema: "encounters",
                table: "MedicationReconciliations",
                columns: new[] { "TenantId", "EncounterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicationReconciliations_TenantId_PatientId",
                schema: "encounters",
                table: "MedicationReconciliations",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.Tenant("encounters", "MedicationDecisions");
            migrationBuilder.Tenant("encounters", "MedicationReconciliations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MedicationDecisions",
                schema: "encounters");

            migrationBuilder.DropTable(
                name: "MedicationReconciliations",
                schema: "encounters");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                schema: "encounters",
                table: "MedicationStatements");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                schema: "encounters",
                table: "MedicationStatements");

            migrationBuilder.DropColumn(
                name: "StoppedAt",
                schema: "encounters",
                table: "MedicationStatements");
        }
    }
}

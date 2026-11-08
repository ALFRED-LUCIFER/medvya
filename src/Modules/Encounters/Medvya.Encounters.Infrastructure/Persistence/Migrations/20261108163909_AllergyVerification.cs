using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllergyVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentId",
                schema: "encounters",
                table: "MedicationStatements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FacilityId",
                schema: "encounters",
                table: "MedicationStatements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Frequency",
                schema: "encounters",
                table: "MedicationStatements",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MergedFromPatientId",
                schema: "encounters",
                table: "MedicationStatements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsClarification",
                schema: "encounters",
                table: "MedicationStatements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedBy",
                schema: "encounters",
                table: "MedicationStatements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NoKnownAllergiesAt",
                schema: "encounters",
                table: "Intakes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NoKnownAllergiesBy",
                schema: "encounters",
                table: "Intakes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FacilityId",
                schema: "encounters",
                table: "Allergies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MergedFromPatientId",
                schema: "encounters",
                table: "Allergies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedBy",
                schema: "encounters",
                table: "Allergies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StatusSetAt",
                schema: "encounters",
                table: "Allergies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StatusSetBy",
                schema: "encounters",
                table: "Allergies",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicationStatements_TenantId_AppointmentId",
                schema: "encounters",
                table: "MedicationStatements",
                columns: new[] { "TenantId", "AppointmentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MedicationStatements_TenantId_AppointmentId",
                schema: "encounters",
                table: "MedicationStatements");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                schema: "encounters",
                table: "MedicationStatements");

            migrationBuilder.DropColumn(
                name: "FacilityId",
                schema: "encounters",
                table: "MedicationStatements");

            migrationBuilder.DropColumn(
                name: "Frequency",
                schema: "encounters",
                table: "MedicationStatements");

            migrationBuilder.DropColumn(
                name: "MergedFromPatientId",
                schema: "encounters",
                table: "MedicationStatements");

            migrationBuilder.DropColumn(
                name: "NeedsClarification",
                schema: "encounters",
                table: "MedicationStatements");

            migrationBuilder.DropColumn(
                name: "RecordedBy",
                schema: "encounters",
                table: "MedicationStatements");

            migrationBuilder.DropColumn(
                name: "NoKnownAllergiesAt",
                schema: "encounters",
                table: "Intakes");

            migrationBuilder.DropColumn(
                name: "NoKnownAllergiesBy",
                schema: "encounters",
                table: "Intakes");

            migrationBuilder.DropColumn(
                name: "FacilityId",
                schema: "encounters",
                table: "Allergies");

            migrationBuilder.DropColumn(
                name: "MergedFromPatientId",
                schema: "encounters",
                table: "Allergies");

            migrationBuilder.DropColumn(
                name: "RecordedBy",
                schema: "encounters",
                table: "Allergies");

            migrationBuilder.DropColumn(
                name: "StatusSetAt",
                schema: "encounters",
                table: "Allergies");

            migrationBuilder.DropColumn(
                name: "StatusSetBy",
                schema: "encounters",
                table: "Allergies");
        }
    }
}

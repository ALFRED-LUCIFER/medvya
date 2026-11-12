using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrescribedMedicinesOnChart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Dose",
                schema: "encounters",
                table: "MedicationStatements",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EncounterId",
                schema: "encounters",
                table: "MedicationStatements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrescriptionId",
                schema: "encounters",
                table: "MedicationStatements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicationStatements_TenantId_EncounterId",
                schema: "encounters",
                table: "MedicationStatements",
                columns: new[] { "TenantId", "EncounterId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MedicationStatements_TenantId_EncounterId",
                schema: "encounters",
                table: "MedicationStatements");

            migrationBuilder.DropColumn(
                name: "EncounterId",
                schema: "encounters",
                table: "MedicationStatements");

            migrationBuilder.DropColumn(
                name: "PrescriptionId",
                schema: "encounters",
                table: "MedicationStatements");

            migrationBuilder.AlterColumn<string>(
                name: "Dose",
                schema: "encounters",
                table: "MedicationStatements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}

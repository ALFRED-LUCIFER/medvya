using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProblemCoding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodeSystem",
                schema: "encounters",
                table: "Problems",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodeSystemVersion",
                schema: "encounters",
                table: "Problems",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EncounterId",
                schema: "encounters",
                table: "Problems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MergedFromPatientId",
                schema: "encounters",
                table: "Problems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedBy",
                schema: "encounters",
                table: "Problems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "encounters",
                table: "Problems",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active"); // every problem before MDV-P2-010 is on the active list

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "encounters",
                table: "Problems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedBy",
                schema: "encounters",
                table: "Problems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Problems_TenantId_EncounterId",
                schema: "encounters",
                table: "Problems",
                columns: new[] { "TenantId", "EncounterId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Problems_CodeHasSystem",
                schema: "encounters",
                table: "Problems",
                sql: "(\"CodeSystem\" IS NULL) = (\"CodeSystemVersion\" IS NULL) AND (\"CodeSystem\" IS NULL OR \"Icd10Code\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Problems_TenantId_EncounterId",
                schema: "encounters",
                table: "Problems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Problems_CodeHasSystem",
                schema: "encounters",
                table: "Problems");

            migrationBuilder.DropColumn(
                name: "CodeSystem",
                schema: "encounters",
                table: "Problems");

            migrationBuilder.DropColumn(
                name: "CodeSystemVersion",
                schema: "encounters",
                table: "Problems");

            migrationBuilder.DropColumn(
                name: "EncounterId",
                schema: "encounters",
                table: "Problems");

            migrationBuilder.DropColumn(
                name: "MergedFromPatientId",
                schema: "encounters",
                table: "Problems");

            migrationBuilder.DropColumn(
                name: "RecordedBy",
                schema: "encounters",
                table: "Problems");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "encounters",
                table: "Problems");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "encounters",
                table: "Problems");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                schema: "encounters",
                table: "Problems");
        }
    }
}

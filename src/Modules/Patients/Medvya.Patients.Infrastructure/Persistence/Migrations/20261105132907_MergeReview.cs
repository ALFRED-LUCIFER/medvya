using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MergeReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoPatientId",
                schema: "patients",
                table: "Patients",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FirstApprovedBy",
                schema: "patients",
                table: "MergeRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MergedAt",
                schema: "patients",
                table: "MergeRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SecondApprovedBy",
                schema: "patients",
                table: "MergeRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UnmergedAt",
                schema: "patients",
                table: "MergeRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UnmergedBy",
                schema: "patients",
                table: "MergeRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MovedFromPatientId",
                schema: "patients",
                table: "Identifiers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_MergedIntoPatientId",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "MergedIntoPatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_Identifiers_TenantId_MovedFromPatientId",
                schema: "patients",
                table: "Identifiers",
                columns: new[] { "TenantId", "MovedFromPatientId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Patients_TenantId_MergedIntoPatientId",
                schema: "patients",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Identifiers_TenantId_MovedFromPatientId",
                schema: "patients",
                table: "Identifiers");

            migrationBuilder.DropColumn(
                name: "MergedIntoPatientId",
                schema: "patients",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "FirstApprovedBy",
                schema: "patients",
                table: "MergeRequests");

            migrationBuilder.DropColumn(
                name: "MergedAt",
                schema: "patients",
                table: "MergeRequests");

            migrationBuilder.DropColumn(
                name: "SecondApprovedBy",
                schema: "patients",
                table: "MergeRequests");

            migrationBuilder.DropColumn(
                name: "UnmergedAt",
                schema: "patients",
                table: "MergeRequests");

            migrationBuilder.DropColumn(
                name: "UnmergedBy",
                schema: "patients",
                table: "MergeRequests");

            migrationBuilder.DropColumn(
                name: "MovedFromPatientId",
                schema: "patients",
                table: "Identifiers");
        }
    }
}

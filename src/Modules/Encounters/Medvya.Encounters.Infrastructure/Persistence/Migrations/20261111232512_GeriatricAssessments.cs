using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GeriatricAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeriatricAssessments",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    FallsLast12Months = table.Column<int>(type: "integer", nullable: true),
                    TimedUpAndGoTenths = table.Column<int>(type: "integer", nullable: true),
                    LyingSystolic = table.Column<int>(type: "integer", nullable: true),
                    LyingDiastolic = table.Column<int>(type: "integer", nullable: true),
                    StandingSystolic = table.Column<int>(type: "integer", nullable: true),
                    StandingDiastolic = table.Column<int>(type: "integer", nullable: true),
                    ClinicalFrailtyScale = table.Column<int>(type: "integer", nullable: true),
                    MiniCog = table.Column<int>(type: "integer", nullable: true),
                    Adl = table.Column<int>(type: "integer", nullable: true),
                    Iadl = table.Column<int>(type: "integer", nullable: true),
                    Phq2 = table.Column<int>(type: "integer", nullable: true),
                    NutritionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeriatricAssessments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeriatricAssessments_TenantId_PatientId_AssessedAt",
                schema: "encounters",
                table: "GeriatricAssessments",
                columns: new[] { "TenantId", "PatientId", "AssessedAt" });

            migrationBuilder.Tenant("encounters", "GeriatricAssessments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeriatricAssessments",
                schema: "encounters");
        }
    }
}

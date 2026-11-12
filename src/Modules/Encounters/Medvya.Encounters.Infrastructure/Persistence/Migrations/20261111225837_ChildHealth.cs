using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChildHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Immunisations",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Vaccine = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Dose = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GivenOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Batch = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GivenBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeferReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Immunisations", x => x.Id);
                    table.CheckConstraint("CK_Immunisations_Outcome", "(\"Outcome\" = 'Given' AND \"GivenOn\" IS NOT NULL AND \"DeferReason\" IS NULL) OR (\"Outcome\" = 'Deferred' AND \"DeferReason\" IS NOT NULL AND \"GivenOn\" IS NULL AND \"Batch\" IS NULL AND \"GivenBy\" IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "MilestoneResponses",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Band = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Milestone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Answer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AnsweredBy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MilestoneResponses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Immunisations_TenantId_PatientId",
                schema: "encounters",
                table: "Immunisations",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_MilestoneResponses_TenantId_PatientId_Band",
                schema: "encounters",
                table: "MilestoneResponses",
                columns: new[] { "TenantId", "PatientId", "Band" });

            migrationBuilder.Tenant("encounters", "Immunisations");
            migrationBuilder.Tenant("encounters", "MilestoneResponses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Immunisations",
                schema: "encounters");

            migrationBuilder.DropTable(
                name: "MilestoneResponses",
                schema: "encounters");
        }
    }
}

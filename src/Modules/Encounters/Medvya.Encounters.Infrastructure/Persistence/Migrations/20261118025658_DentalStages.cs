using Medvya.BuildingBlocks.Infrastructure;
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DentalStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StagesKey",
                schema: "encounters",
                table: "DentalPlanItems",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DentalStagesDone",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    StageName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoneBy = table.Column<Guid>(type: "uuid", nullable: false),
                    DoneAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DentalStagesDone", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DentalStagesDone_TenantId_PatientId",
                schema: "encounters",
                table: "DentalStagesDone",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_DentalStagesDone_TenantId_PlanItemId_Stage",
                schema: "encounters",
                table: "DentalStagesDone",
                columns: new[] { "TenantId", "PlanItemId", "Stage" },
                unique: true);

            migrationBuilder.Tenant("encounters", "DentalStagesDone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DentalStagesDone",
                schema: "encounters");

            migrationBuilder.DropColumn(
                name: "StagesKey",
                schema: "encounters",
                table: "DentalPlanItems");
        }
    }
}

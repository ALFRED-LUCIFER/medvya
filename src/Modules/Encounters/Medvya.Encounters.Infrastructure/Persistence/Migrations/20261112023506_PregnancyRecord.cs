using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PregnancyRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PregnancyEpisodes",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    Lmp = table.Column<DateOnly>(type: "date", nullable: true),
                    ScanDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ScanGestationDays = table.Column<int>(type: "integer", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DatedBy = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DatingReason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PreviousDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Gravida = table.Column<int>(type: "integer", nullable: true),
                    Para = table.Column<int>(type: "integer", nullable: true),
                    Abortions = table.Column<int>(type: "integer", nullable: true),
                    Living = table.Column<int>(type: "integer", nullable: true),
                    StartedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DangerSignsCounselledBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DangerSignsCounselledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PregnancyEpisodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PregnancyRuledOuts",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PregnancyRuledOuts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PregnancyEpisodes_TenantId_PatientId",
                schema: "encounters",
                table: "PregnancyEpisodes",
                columns: new[] { "TenantId", "PatientId" },
                unique: true,
                filter: "\"Outcome\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PregnancyRuledOuts_TenantId_PatientId_RecordedAt",
                schema: "encounters",
                table: "PregnancyRuledOuts",
                columns: new[] { "TenantId", "PatientId", "RecordedAt" });

            migrationBuilder.Tenant("encounters", "PregnancyEpisodes");
            migrationBuilder.Tenant("encounters", "PregnancyRuledOuts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PregnancyEpisodes",
                schema: "encounters");

            migrationBuilder.DropTable(
                name: "PregnancyRuledOuts",
                schema: "encounters");
        }
    }
}

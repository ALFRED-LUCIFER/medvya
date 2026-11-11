using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChronicCare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CareChecks",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DoneOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CareGoals",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    Text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Target = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareGoals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CareTargets",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LimitHundredths = table.Column<int>(type: "integer", nullable: false),
                    SetBy = table.Column<Guid>(type: "uuid", nullable: false),
                    SetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareTargets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareChecks_TenantId_PatientId_Kind_DoneOn",
                schema: "encounters",
                table: "CareChecks",
                columns: new[] { "TenantId", "PatientId", "Kind", "DoneOn" });

            migrationBuilder.CreateIndex(
                name: "IX_CareGoals_TenantId_PatientId_CreatedAt",
                schema: "encounters",
                table: "CareGoals",
                columns: new[] { "TenantId", "PatientId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CareTargets_TenantId_PatientId_Code_SetAt",
                schema: "encounters",
                table: "CareTargets",
                columns: new[] { "TenantId", "PatientId", "Code", "SetAt" });

            migrationBuilder.Tenant("encounters", "CareTargets");
            migrationBuilder.Tenant("encounters", "CareChecks");
            migrationBuilder.Tenant("encounters", "CareGoals");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CareChecks",
                schema: "encounters");

            migrationBuilder.DropTable(
                name: "CareGoals",
                schema: "encounters");

            migrationBuilder.DropTable(
                name: "CareTargets",
                schema: "encounters");
        }
    }
}

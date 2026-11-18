using Medvya.BuildingBlocks.Infrastructure;
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DentalHistoryCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DentalHistoryChecks",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Yes = table.Column<string[]>(type: "text[]", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DentalHistoryChecks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DentalHistoryChecks_TenantId_EncounterId",
                schema: "encounters",
                table: "DentalHistoryChecks",
                columns: new[] { "TenantId", "EncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_DentalHistoryChecks_TenantId_PatientId_ConfirmedAt",
                schema: "encounters",
                table: "DentalHistoryChecks",
                columns: new[] { "TenantId", "PatientId", "ConfirmedAt" });

            migrationBuilder.Tenant("encounters", "DentalHistoryChecks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DentalHistoryChecks",
                schema: "encounters");
        }
    }
}

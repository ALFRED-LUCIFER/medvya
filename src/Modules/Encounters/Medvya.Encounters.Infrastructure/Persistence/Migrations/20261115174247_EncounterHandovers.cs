using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EncounterHandovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EncounterHandovers",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromClinicianId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToClinicianId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncounterHandovers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EncounterHandovers_TenantId_EncounterId",
                schema: "encounters",
                table: "EncounterHandovers",
                columns: new[] { "TenantId", "EncounterId" });

            // Row-level security on the tenant (ADR-012), then append-only and hash-chained for every role (ADR-006): a hand-over is never edited or removed.
            migrationBuilder.Tenant("encounters", "EncounterHandovers");
            migrationBuilder.AddLedger("encounters", "EncounterHandovers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only in production: a ledger table cannot be dropped by design; this is for a throwaway database.
            migrationBuilder.DropTable(
                name: "EncounterHandovers",
                schema: "encounters");
        }
    }
}

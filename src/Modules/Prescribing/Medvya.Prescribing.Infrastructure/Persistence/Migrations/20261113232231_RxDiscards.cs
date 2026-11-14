using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RxDiscards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrescriptionDiscards",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DiscardedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrescriptionDiscards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionDiscards_TenantId_EncounterId",
                schema: "prescribing",
                table: "PrescriptionDiscards",
                columns: new[] { "TenantId", "EncounterId" },
                unique: true);

            // Row-level security on the tenant (ADR-012), then append-only and hash-chained for every role (ADR-006): a discard is never edited or undone.
            migrationBuilder.Tenant("prescribing", "PrescriptionDiscards");
            migrationBuilder.AddLedger("prescribing", "PrescriptionDiscards");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only in production: a ledger table cannot be dropped by design; this is for a throwaway database.
            migrationBuilder.DropTable(
                name: "PrescriptionDiscards",
                schema: "prescribing");
        }
    }
}

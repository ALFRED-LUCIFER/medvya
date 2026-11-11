using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RxCancellations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrescriptionCancellations",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CancelledBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrescriptionCancellations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionCancellations_TenantId_PrescriptionId",
                schema: "prescribing",
                table: "PrescriptionCancellations",
                columns: new[] { "TenantId", "PrescriptionId" },
                unique: true);

            // Row-level security on the tenant (ADR-012), then append-only and hash-chained for every role (ADR-006): a cancellation is never edited or undone.
            migrationBuilder.Tenant("prescribing", "PrescriptionCancellations");
            migrationBuilder.AddLedger("prescribing", "PrescriptionCancellations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only in production: a ledger table cannot be dropped by design; this is for a throwaway database.
            migrationBuilder.DropTable(
                name: "PrescriptionCancellations",
                schema: "prescribing");
        }
    }
}

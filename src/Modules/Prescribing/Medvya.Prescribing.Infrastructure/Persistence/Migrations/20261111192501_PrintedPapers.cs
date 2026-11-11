using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrintedPapers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrintedPapers",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Items = table.Column<int>(type: "integer", nullable: false),
                    VerifyCode = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BlobKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RenderedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RenderedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintedPapers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintedPapers_TenantId_SourceId",
                schema: "prescribing",
                table: "PrintedPapers",
                columns: new[] { "TenantId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrintedPapers_TenantId_VerifyCode",
                schema: "prescribing",
                table: "PrintedPapers",
                columns: new[] { "TenantId", "VerifyCode" },
                unique: true);

            // Row-level security on the tenant (ADR-012), then append-only and hash-chained (ADR-006): a paper handed out is never edited.
            migrationBuilder.Tenant("prescribing", "PrintedPapers");
            migrationBuilder.AddLedger("prescribing", "PrintedPapers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only in production: a ledger table cannot be dropped by design; this is for a throwaway database.
            migrationBuilder.DropTable(
                name: "PrintedPapers",
                schema: "prescribing");
        }
    }
}

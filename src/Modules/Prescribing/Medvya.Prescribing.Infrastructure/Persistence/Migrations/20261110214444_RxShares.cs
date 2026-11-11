using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RxShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PharmacyDispatchEvents",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PharmacyRef = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DispensedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PharmacyDispatchEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RxShareCodes",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RxShareCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RxShares",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PdfId = table.Column<Guid>(type: "uuid", nullable: false),
                    Recipient = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PharmacyName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PharmacyEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    LinkHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SharedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    SharedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RxShares", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PharmacyDispatchEvents_TenantId_ShareId",
                schema: "prescribing",
                table: "PharmacyDispatchEvents",
                columns: new[] { "TenantId", "ShareId" });

            migrationBuilder.CreateIndex(
                name: "IX_RxShareCodes_TenantId_ShareId",
                schema: "prescribing",
                table: "RxShareCodes",
                columns: new[] { "TenantId", "ShareId" });

            migrationBuilder.CreateIndex(
                name: "IX_RxShares_TenantId_EncounterId",
                schema: "prescribing",
                table: "RxShares",
                columns: new[] { "TenantId", "EncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_RxShares_TenantId_LinkHash",
                schema: "prescribing",
                table: "RxShares",
                columns: new[] { "TenantId", "LinkHash" },
                unique: true);

            // Row-level security on the tenant (ADR-012) for all three; a share and a dispatch state are append-only and hash-chained (ADR-006).
            // A code is not: its wrong tries are counted and it is marked used.
            foreach (var table in new[] { "RxShares", "RxShareCodes", "PharmacyDispatchEvents" }) migrationBuilder.Tenant("prescribing", table);
            migrationBuilder.AddLedger("prescribing", "RxShares");
            migrationBuilder.AddLedger("prescribing", "PharmacyDispatchEvents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PharmacyDispatchEvents",
                schema: "prescribing");

            migrationBuilder.DropTable(
                name: "RxShareCodes",
                schema: "prescribing");

            migrationBuilder.DropTable(
                name: "RxShares",
                schema: "prescribing");
        }
    }
}

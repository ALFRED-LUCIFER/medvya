using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RxAuthorisation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthorisedPrescriptions",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false),
                    Lines = table.Column<string>(type: "jsonb", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleSetVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    VerifyCode = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    AuthorisedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorisedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorisedPrescriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrescriptionPdfs",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Paper = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RenderedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RenderedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrescriptionPdfs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RxLayoutApprovals",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LayoutVersion = table.Column<int>(type: "integer", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RxLayoutApprovals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorisedPrescriptions_TenantId_EncounterId_VersionNo",
                schema: "prescribing",
                table: "AuthorisedPrescriptions",
                columns: new[] { "TenantId", "EncounterId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthorisedPrescriptions_TenantId_VerifyCode",
                schema: "prescribing",
                table: "AuthorisedPrescriptions",
                columns: new[] { "TenantId", "VerifyCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionPdfs_TenantId_PrescriptionId",
                schema: "prescribing",
                table: "PrescriptionPdfs",
                columns: new[] { "TenantId", "PrescriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_RxLayoutApprovals_TenantId_UserId_LayoutVersion",
                schema: "prescribing",
                table: "RxLayoutApprovals",
                columns: new[] { "TenantId", "UserId", "LayoutVersion" },
                unique: true);

            // Row-level security on the tenant (ADR-012), then append-only and hash-chained for every role (ADR-006): an authorised prescription,
            // a PDF handed out and a doctor's layout approval are never edited.
            foreach (var table in new[] { "AuthorisedPrescriptions", "PrescriptionPdfs", "RxLayoutApprovals" })
            {
                migrationBuilder.Tenant("prescribing", table);
                migrationBuilder.AddLedger("prescribing", table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only in production: a ledger table cannot be dropped by design; this is for a throwaway database.
            migrationBuilder.DropTable(
                name: "AuthorisedPrescriptions",
                schema: "prescribing");

            migrationBuilder.DropTable(
                name: "PrescriptionPdfs",
                schema: "prescribing");

            migrationBuilder.DropTable(
                name: "RxLayoutApprovals",
                schema: "prescribing");
        }
    }
}

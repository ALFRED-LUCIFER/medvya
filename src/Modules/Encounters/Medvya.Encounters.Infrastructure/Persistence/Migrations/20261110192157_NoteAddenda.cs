using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NoteAddenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AmendsVersionId",
                schema: "encounters",
                table: "SignedNoteVersions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                schema: "encounters",
                table: "SignedNoteVersions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SignedNotePdfs",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RenderedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RenderedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignedNotePdfs", x => x.Id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SignedNoteVersions_Addendum",
                schema: "encounters",
                table: "SignedNoteVersions",
                sql: "(\"VersionNo\" = 1 AND \"AmendsVersionId\" IS NULL AND \"Reason\" IS NULL) OR (\"VersionNo\" > 1 AND \"AmendsVersionId\" IS NOT NULL AND \"Reason\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_SignedNotePdfs_TenantId_NoteId",
                schema: "encounters",
                table: "SignedNotePdfs",
                columns: new[] { "TenantId", "NoteId" });

            migrationBuilder.Tenant("encounters", "SignedNotePdfs");
            // ADR-006: every PDF handed out is kept as it was, hash-chained and append-only for every role.
            migrationBuilder.AddLedger("encounters", "SignedNotePdfs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only in production: a ledger table cannot be dropped by design; this is for a throwaway database.
            migrationBuilder.DropTable(
                name: "SignedNotePdfs",
                schema: "encounters");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SignedNoteVersions_Addendum",
                schema: "encounters",
                table: "SignedNoteVersions");

            migrationBuilder.DropColumn(
                name: "AmendsVersionId",
                schema: "encounters",
                table: "SignedNoteVersions");

            migrationBuilder.DropColumn(
                name: "Reason",
                schema: "encounters",
                table: "SignedNoteVersions");
        }
    }
}

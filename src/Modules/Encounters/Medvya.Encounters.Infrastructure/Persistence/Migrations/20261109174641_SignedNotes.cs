using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SignedNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignedNoteVersions",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    NoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    Manual = table.Column<bool>(type: "boolean", nullable: false),
                    Sections = table.Column<string>(type: "jsonb", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SignedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignedNoteVersions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignedNoteVersions_TenantId_NoteId_VersionNo",
                schema: "encounters",
                table: "SignedNoteVersions",
                columns: new[] { "TenantId", "NoteId", "VersionNo" },
                unique: true);

            migrationBuilder.Tenant("encounters", "SignedNoteVersions");
            // ADR-006: the signature is hash-chained and append-only for every role.
            migrationBuilder.AddLedger("encounters", "SignedNoteVersions");

            // A signed note row is frozen: only the Draft to Signed update passes, for every role. Delete and every later update are refused.
            migrationBuilder.Sql("""
                CREATE FUNCTION encounters.refuse_signed_note() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD."Status" = 'Signed' THEN
                        RAISE EXCEPTION 'a signed note is immutable: % is refused', TG_OP USING ERRCODE = 'restrict_violation';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_ConsultNotes_SignedImmutable" BEFORE UPDATE OR DELETE ON encounters."ConsultNotes" FOR EACH ROW EXECUTE FUNCTION encounters.refuse_signed_note();
                ALTER TABLE encounters."ConsultNotes" ENABLE ALWAYS TRIGGER "TR_ConsultNotes_SignedImmutable";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only in production: a ledger table cannot be dropped by design; this is for a throwaway database.
            migrationBuilder.Sql("DROP TRIGGER \"TR_ConsultNotes_SignedImmutable\" ON encounters.\"ConsultNotes\"; DROP FUNCTION encounters.refuse_signed_note();");
            migrationBuilder.DropTable(
                name: "SignedNoteVersions",
                schema: "encounters");
        }
    }
}

using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HistoryEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HistoryEntries",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    Section = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoryEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_TenantId_PatientId_Section_RecordedAt",
                schema: "encounters",
                table: "HistoryEntries",
                columns: new[] { "TenantId", "PatientId", "Section", "RecordedAt" });

            migrationBuilder.Tenant("encounters", "HistoryEntries");
            // Append-only (MDV-P2-032): a saved version is never deleted or rewritten. A merge may still move it to another patient.
            migrationBuilder.Sql("""
                CREATE FUNCTION encounters.refuse_history_rewrite() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' OR NEW."Text" IS DISTINCT FROM OLD."Text" OR NEW."Section" IS DISTINCT FROM OLD."Section"
                        OR NEW."RecordedBy" IS DISTINCT FROM OLD."RecordedBy" OR NEW."RecordedAt" IS DISTINCT FROM OLD."RecordedAt" THEN
                        RAISE EXCEPTION 'a history version is append-only: % is refused', TG_OP USING ERRCODE = 'restrict_violation';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER "TR_HistoryEntries_AppendOnly" BEFORE UPDATE OR DELETE ON encounters."HistoryEntries" FOR EACH ROW EXECUTE FUNCTION encounters.refuse_history_rewrite();
                ALTER TABLE encounters."HistoryEntries" ENABLE ALWAYS TRIGGER "TR_HistoryEntries_AppendOnly";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER \"TR_HistoryEntries_AppendOnly\" ON encounters.\"HistoryEntries\"; DROP FUNCTION encounters.refuse_history_rewrite();");
            migrationBuilder.DropTable(
                name: "HistoryEntries",
                schema: "encounters");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NoteRecordAtSigning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MDV-P2-049: the visit record at signing on version 1. Added after the ledger was set up, so ContentHash covers it, not the chain.
            migrationBuilder.AddColumn<string>(
                name: "AtSigning",
                schema: "encounters",
                table: "SignedNoteVersions",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AtSigning",
                schema: "encounters",
                table: "SignedNoteVersions");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NoteChecklistGates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecordingConsent",
                schema: "encounters",
                table: "ConsultNotes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "UndecidedAi",
                schema: "encounters",
                table: "ConsultNotes",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecordingConsent",
                schema: "encounters",
                table: "ConsultNotes");

            migrationBuilder.DropColumn(
                name: "UndecidedAi",
                schema: "encounters",
                table: "ConsultNotes");
        }
    }
}

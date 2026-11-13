using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NoteComplaintSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ChiefComplaintAt",
                schema: "encounters",
                table: "SignedNoteVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChiefComplaintBy",
                schema: "encounters",
                table: "SignedNoteVersions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChiefComplaintSource",
                schema: "encounters",
                table: "SignedNoteVersions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ChiefComplaintAt",
                schema: "encounters",
                table: "Intakes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChiefComplaintBy",
                schema: "encounters",
                table: "Intakes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ChiefComplaintAt",
                schema: "encounters",
                table: "ConsultNotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChiefComplaintBy",
                schema: "encounters",
                table: "ConsultNotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChiefComplaintSource",
                schema: "encounters",
                table: "ConsultNotes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChiefComplaintAt",
                schema: "encounters",
                table: "SignedNoteVersions");

            migrationBuilder.DropColumn(
                name: "ChiefComplaintBy",
                schema: "encounters",
                table: "SignedNoteVersions");

            migrationBuilder.DropColumn(
                name: "ChiefComplaintSource",
                schema: "encounters",
                table: "SignedNoteVersions");

            migrationBuilder.DropColumn(
                name: "ChiefComplaintAt",
                schema: "encounters",
                table: "Intakes");

            migrationBuilder.DropColumn(
                name: "ChiefComplaintBy",
                schema: "encounters",
                table: "Intakes");

            migrationBuilder.DropColumn(
                name: "ChiefComplaintAt",
                schema: "encounters",
                table: "ConsultNotes");

            migrationBuilder.DropColumn(
                name: "ChiefComplaintBy",
                schema: "encounters",
                table: "ConsultNotes");

            migrationBuilder.DropColumn(
                name: "ChiefComplaintSource",
                schema: "encounters",
                table: "ConsultNotes");
        }
    }
}

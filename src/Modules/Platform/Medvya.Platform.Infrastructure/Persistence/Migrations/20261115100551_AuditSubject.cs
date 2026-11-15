using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Detail",
                schema: "platform",
                table: "AuditEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectName",
                schema: "platform",
                table: "AuditEntries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Detail",
                schema: "platform",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "SubjectName",
                schema: "platform",
                table: "AuditEntries");
        }
    }
}

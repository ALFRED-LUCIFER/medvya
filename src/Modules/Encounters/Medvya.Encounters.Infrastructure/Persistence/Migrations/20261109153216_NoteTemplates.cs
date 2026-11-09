using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NoteTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DoctorNoteTemplates",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateKeys = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorNoteTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorNoteTemplates_TenantId_UserId",
                schema: "encounters",
                table: "DoctorNoteTemplates",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.Tenant("encounters", "DoctorNoteTemplates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorNoteTemplates",
                schema: "encounters");
        }
    }
}

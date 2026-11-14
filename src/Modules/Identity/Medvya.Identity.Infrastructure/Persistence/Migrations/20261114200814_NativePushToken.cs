using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativePushToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PushToken",
                schema: "identity",
                table: "Sessions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_PushToken",
                schema: "identity",
                table: "Sessions",
                column: "PushToken",
                unique: true,
                filter: "\"PushToken\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_PushToken",
                schema: "identity",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PushToken",
                schema: "identity",
                table: "Sessions");
        }
    }
}

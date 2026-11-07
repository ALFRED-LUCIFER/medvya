using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NativeRefreshPrevious : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "PreviousRefreshTokenHash",
                schema: "identity",
                table: "Sessions",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousRefreshTokenHash",
                schema: "identity",
                table: "Sessions");
        }
    }
}

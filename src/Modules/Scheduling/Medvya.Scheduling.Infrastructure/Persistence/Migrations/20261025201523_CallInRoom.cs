using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CallInRoom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Room",
                schema: "scheduling",
                table: "Appointments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Room",
                schema: "scheduling",
                table: "Appointments");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReassignReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReassignReason",
                schema: "scheduling",
                table: "AppointmentReschedules",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReassignReason",
                schema: "scheduling",
                table: "AppointmentReschedules");
        }
    }
}

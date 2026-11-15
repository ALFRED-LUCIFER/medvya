using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NurseRoomAndGuestWifi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NurseRoom",
                schema: "tenancy",
                table: "Facilities",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WifiDaily",
                schema: "tenancy",
                table: "Facilities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WifiNetwork",
                schema: "tenancy",
                table: "Facilities",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WifiPassword",
                schema: "tenancy",
                table: "Facilities",
                type: "character varying(63)",
                maxLength: 63,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NurseRoom",
                schema: "tenancy",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "WifiDaily",
                schema: "tenancy",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "WifiNetwork",
                schema: "tenancy",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "WifiPassword",
                schema: "tenancy",
                table: "Facilities");
        }
    }
}

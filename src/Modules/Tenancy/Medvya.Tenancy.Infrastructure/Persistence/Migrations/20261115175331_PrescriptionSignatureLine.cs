using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrescriptionSignatureLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SignatureLine",
                schema: "tenancy",
                table: "Profiles",
                type: "boolean",
                nullable: false,
                defaultValue: true); // existing practices keep the signing space they print today (#1365)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureLine",
                schema: "tenancy",
                table: "Profiles");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MDV-P1-103: the clinic's queue rule. Existing clinics get arrival order with walk-ins not held back, the order the board and the clinic
    /// overview already used. Forward-only: no Down.
    /// </summary>
    public partial class QueueRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BookedFirst",
                schema: "tenancy",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "QueueOrder",
                schema: "tenancy",
                table: "Tenants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Arrival");
        }
    }
}

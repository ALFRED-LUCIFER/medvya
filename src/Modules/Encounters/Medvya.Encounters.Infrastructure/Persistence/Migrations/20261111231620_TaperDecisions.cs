using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaperDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EndsOn",
                schema: "encounters",
                table: "MedicationDecisions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Steps",
                schema: "encounters",
                table: "MedicationDecisions",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndsOn",
                schema: "encounters",
                table: "MedicationDecisions");

            migrationBuilder.DropColumn(
                name: "Steps",
                schema: "encounters",
                table: "MedicationDecisions");
        }
    }
}

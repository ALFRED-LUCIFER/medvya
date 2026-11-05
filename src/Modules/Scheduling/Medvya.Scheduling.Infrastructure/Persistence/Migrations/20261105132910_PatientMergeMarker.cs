using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PatientMergeMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MergedFromPatientId",
                schema: "scheduling",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MergedFromPatientId",
                schema: "scheduling",
                table: "Appointments");
        }
    }
}

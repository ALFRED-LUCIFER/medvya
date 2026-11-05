using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecurringSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SeriesId",
                schema: "scheduling",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_SeriesId",
                schema: "scheduling",
                table: "Appointments",
                columns: new[] { "TenantId", "SeriesId" },
                filter: "[SeriesId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_SeriesId",
                schema: "scheduling",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                schema: "scheduling",
                table: "Appointments");
        }
    }
}

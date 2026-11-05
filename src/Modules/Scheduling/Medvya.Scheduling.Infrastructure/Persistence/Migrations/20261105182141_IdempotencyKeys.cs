using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKeyHash",
                schema: "scheduling",
                table: "Appointments",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyRequestHash",
                schema: "scheduling",
                table: "Appointments",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_BookedBy_IdempotencyKeyHash",
                schema: "scheduling",
                table: "Appointments",
                columns: new[] { "TenantId", "BookedBy", "IdempotencyKeyHash" },
                unique: true,
                filter: "[IdempotencyKeyHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_BookedBy_IdempotencyKeyHash",
                schema: "scheduling",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "IdempotencyKeyHash",
                schema: "scheduling",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "IdempotencyRequestHash",
                schema: "scheduling",
                table: "Appointments");
        }
    }
}

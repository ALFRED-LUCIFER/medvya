using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKeyHash",
                schema: "patients",
                table: "Patients",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyRequestHash",
                schema: "patients",
                table: "Patients",
                type: "nchar(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_RegisteredBy_IdempotencyKeyHash",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "RegisteredBy", "IdempotencyKeyHash" },
                unique: true,
                filter: "[IdempotencyKeyHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Patients_TenantId_RegisteredBy_IdempotencyKeyHash",
                schema: "patients",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "IdempotencyKeyHash",
                schema: "patients",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "IdempotencyRequestHash",
                schema: "patients",
                table: "Patients");
        }
    }
}

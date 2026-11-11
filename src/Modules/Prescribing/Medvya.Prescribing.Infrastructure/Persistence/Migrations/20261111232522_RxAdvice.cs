using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RxAdvice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Advice",
                schema: "prescribing",
                table: "AuthorisedPrescriptions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RxAdvice",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RxAdvice", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RxAdvice_TenantId_EncounterId",
                schema: "prescribing",
                table: "RxAdvice",
                columns: new[] { "TenantId", "EncounterId" },
                unique: true);

            migrationBuilder.Tenant("prescribing", "RxAdvice");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RxAdvice",
                schema: "prescribing");

            migrationBuilder.DropColumn(
                name: "Advice",
                schema: "prescribing",
                table: "AuthorisedPrescriptions");
        }
    }
}

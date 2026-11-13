using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FormularyBrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormularyBrands",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormularyItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormularyBrands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormularyBrands_FormularyItems_FormularyItemId",
                        column: x => x.FormularyItemId,
                        principalSchema: "prescribing",
                        principalTable: "FormularyItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormularyBrands_FormularyItemId",
                schema: "prescribing",
                table: "FormularyBrands",
                column: "FormularyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FormularyBrands_TenantId_FormularyItemId_Name",
                schema: "prescribing",
                table: "FormularyBrands",
                columns: new[] { "TenantId", "FormularyItemId", "Name" },
                unique: true);

            migrationBuilder.Tenant("prescribing", "FormularyBrands");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormularyBrands",
                schema: "prescribing");
        }
    }
}

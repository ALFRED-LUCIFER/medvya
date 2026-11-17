using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceLineSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                schema: "billing",
                table: "InvoiceLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_TenantId_InvoiceId_SourceId",
                schema: "billing",
                table: "InvoiceLines",
                columns: new[] { "TenantId", "InvoiceId", "SourceId" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_TenantId_SourceId",
                schema: "billing",
                table: "InvoiceLines",
                columns: new[] { "TenantId", "SourceId" },
                filter: "\"SourceId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceLines_TenantId_InvoiceId_SourceId",
                schema: "billing",
                table: "InvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLines_TenantId_SourceId",
                schema: "billing",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "SourceId",
                schema: "billing",
                table: "InvoiceLines");
        }
    }
}

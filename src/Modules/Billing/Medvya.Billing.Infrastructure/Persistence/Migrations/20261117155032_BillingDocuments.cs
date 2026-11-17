using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BillingDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingDocuments",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Layout = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PaidMinor = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    BlobKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IssuedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingDocuments", x => x.Id);
                    table.CheckConstraint("CK_BillingDocuments_Paid", "\"PaidMinor\" >= 0");
                    table.ForeignKey(
                        name: "FK_BillingDocuments_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "billing",
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_InvoiceId",
                schema: "billing",
                table: "BillingDocuments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_TenantId_InvoiceId_Kind_Layout_PaidMinor",
                schema: "billing",
                table: "BillingDocuments",
                columns: new[] { "TenantId", "InvoiceId", "Kind", "Layout", "PaidMinor" },
                unique: true);

            // Tenant isolation layer 2 (ADR-012), as for the other billing tables. Append-only: table-rights.sql revokes UPDATE (billing has no DELETE).
            migrationBuilder.Tenant("billing", "BillingDocuments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingDocuments",
                schema: "billing");
        }
    }
}

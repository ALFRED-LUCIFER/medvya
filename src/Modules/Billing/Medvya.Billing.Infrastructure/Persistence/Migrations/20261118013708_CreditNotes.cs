using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillingDocuments_TenantId_InvoiceId_Kind_Layout_PaidMinor",
                schema: "billing",
                table: "BillingDocuments");

            migrationBuilder.AddColumn<long>(
                name: "RefundApprovalAboveMinor",
                schema: "billing",
                table: "Settings",
                type: "bigint",
                nullable: false,
                defaultValue: 100_000L); // the model's default (₹1,000) for a clinic that saved its settings before refunds existed

            migrationBuilder.AddColumn<long>(
                name: "RefundedMinor",
                schema: "billing",
                table: "Invoices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "CreditNoteId",
                schema: "billing",
                table: "BillingDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CreditNotes",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CashSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotes", x => x.Id);
                    table.CheckConstraint("CK_CreditNotes_Amount", "\"AmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_CreditNotes_CashSessions_CashSessionId",
                        column: x => x.CashSessionId,
                        principalSchema: "billing",
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "billing",
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Refunded",
                schema: "billing",
                table: "Invoices",
                sql: "\"RefundedMinor\" >= 0 AND \"RefundedMinor\" <= \"PaidMinor\"");

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_CreditNoteId",
                schema: "billing",
                table: "BillingDocuments",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_TenantId_CreditNoteId_Layout",
                schema: "billing",
                table: "BillingDocuments",
                columns: new[] { "TenantId", "CreditNoteId", "Layout" },
                unique: true,
                filter: "\"CreditNoteId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_TenantId_InvoiceId_Kind_Layout_PaidMinor",
                schema: "billing",
                table: "BillingDocuments",
                columns: new[] { "TenantId", "InvoiceId", "Kind", "Layout", "PaidMinor" },
                unique: true,
                filter: "\"CreditNoteId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CashSessionId",
                schema: "billing",
                table: "CreditNotes",
                column: "CashSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_InvoiceId",
                schema: "billing",
                table: "CreditNotes",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_TenantId_Number",
                schema: "billing",
                table: "CreditNotes",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BillingDocuments_CreditNotes_CreditNoteId",
                schema: "billing",
                table: "BillingDocuments",
                column: "CreditNoteId",
                principalSchema: "billing",
                principalTable: "CreditNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Tenant isolation layer 2 (ADR-012), as for the other billing tables.
            migrationBuilder.Tenant("billing", "CreditNotes");
            // MDV-P4-006: a credit note is never changed or deleted, for every role (ENABLE ALWAYS: session_replication_role does not skip it).
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.refuse_credit_note_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'a credit note is append-only: % is refused', TG_OP USING ERRCODE = 'restrict_violation';
                END $$;
                CREATE TRIGGER "TR_CreditNotes_AppendOnly" BEFORE UPDATE OR DELETE ON billing."CreditNotes" FOR EACH ROW EXECUTE FUNCTION billing.refuse_credit_note_change();
                ALTER TABLE billing."CreditNotes" ENABLE ALWAYS TRIGGER "TR_CreditNotes_AppendOnly";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only in production; this is for a throwaway database.
            migrationBuilder.Sql("""DROP TRIGGER "TR_CreditNotes_AppendOnly" ON billing."CreditNotes"; DROP FUNCTION billing.refuse_credit_note_change();""");
            migrationBuilder.DropForeignKey(
                name: "FK_BillingDocuments_CreditNotes_CreditNoteId",
                schema: "billing",
                table: "BillingDocuments");

            migrationBuilder.DropTable(
                name: "CreditNotes",
                schema: "billing");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Refunded",
                schema: "billing",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_BillingDocuments_CreditNoteId",
                schema: "billing",
                table: "BillingDocuments");

            migrationBuilder.DropIndex(
                name: "IX_BillingDocuments_TenantId_CreditNoteId_Layout",
                schema: "billing",
                table: "BillingDocuments");

            migrationBuilder.DropIndex(
                name: "IX_BillingDocuments_TenantId_InvoiceId_Kind_Layout_PaidMinor",
                schema: "billing",
                table: "BillingDocuments");

            migrationBuilder.DropColumn(
                name: "RefundApprovalAboveMinor",
                schema: "billing",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "RefundedMinor",
                schema: "billing",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CreditNoteId",
                schema: "billing",
                table: "BillingDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_TenantId_InvoiceId_Kind_Layout_PaidMinor",
                schema: "billing",
                table: "BillingDocuments",
                columns: new[] { "TenantId", "InvoiceId", "Kind", "Layout", "PaidMinor" },
                unique: true);
        }
    }
}

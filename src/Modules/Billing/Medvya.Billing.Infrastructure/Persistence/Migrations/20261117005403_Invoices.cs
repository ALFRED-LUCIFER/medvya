using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Invoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invoices",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    SubtotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    DiscountMinor = table.Column<long>(type: "bigint", nullable: false),
                    DiscountReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TaxMinor = table.Column<long>(type: "bigint", nullable: false),
                    RoundOffMinor = table.Column<long>(type: "bigint", nullable: false),
                    TotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    PaidMinor = table.Column<long>(type: "bigint", nullable: false),
                    CancelReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.CheckConstraint("CK_Invoices_Amounts", "\"SubtotalMinor\" >= 0 AND \"DiscountMinor\" >= 0 AND \"TaxMinor\" >= 0");
                    table.CheckConstraint("CK_Invoices_Number", "\"State\" IN ('Draft', 'Cancelled') OR \"Number\" IS NOT NULL");
                    table.CheckConstraint("CK_Invoices_Paid", "\"PaidMinor\" >= 0 AND \"PaidMinor\" <= GREATEST(\"TotalMinor\", 0)");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sac = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitMinor = table.Column<long>(type: "bigint", nullable: false),
                    TaxTreatment = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    GstRateBasisPoints = table.Column<int>(type: "integer", nullable: true),
                    TaxMinor = table.Column<long>(type: "bigint", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Suggested = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.CheckConstraint("CK_InvoiceLines_Quantity", "\"Quantity\" BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "billing",
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_PriceVersions_PriceVersionId",
                        column: x => x.PriceVersionId,
                        principalSchema: "billing",
                        principalTable: "PriceVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "billing",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ProviderPaymentId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ProviderLinkId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    LinkUrl = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Payments_Amount", "\"AmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_Payments_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "billing",
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId",
                schema: "billing",
                table: "InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_PriceVersionId",
                schema: "billing",
                table: "InvoiceLines",
                column: "PriceVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_ServiceId",
                schema: "billing",
                table: "InvoiceLines",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_FacilityId_CreatedAt",
                schema: "billing",
                table: "Invoices",
                columns: new[] { "FacilityId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PatientId",
                schema: "billing",
                table: "Invoices",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_AppointmentId",
                schema: "billing",
                table: "Invoices",
                columns: new[] { "TenantId", "AppointmentId" },
                unique: true,
                filter: "\"AppointmentId\" IS NOT NULL AND \"State\" <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_Number",
                schema: "billing",
                table: "Invoices",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "\"Number\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_InvoiceId",
                schema: "billing",
                table: "Payments",
                column: "InvoiceId",
                unique: true,
                filter: "\"State\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_ProviderLinkId",
                schema: "billing",
                table: "Payments",
                columns: new[] { "TenantId", "ProviderLinkId" },
                unique: true,
                filter: "\"ProviderLinkId\" IS NOT NULL");

            // Tenant isolation layer 2 (ADR-012), as for the catalogue.
            migrationBuilder.Tenant("billing", "Invoices");
            migrationBuilder.Tenant("billing", "InvoiceLines");
            migrationBuilder.Tenant("billing", "Payments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceLines",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "Payments",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "Invoices",
                schema: "billing");
        }
    }
}

using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DentalChartAndPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DentalEstimateAcceptances",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedByName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TotalMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ItemsKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DentalEstimateAcceptances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DentalFindings",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tooth = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Surfaces = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    Condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DentalFindings", x => x.Id);
                    table.CheckConstraint("CK_DentalFindings_Surfaces", "(\"Condition\" IN ('Caries', 'Filling')) = (\"Surfaces\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "DentalPlanItems",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tooth = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Surfaces = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    EstimateMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DoneAppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DoneAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DentalPlanItems", x => x.Id);
                    table.CheckConstraint("CK_DentalPlanItems_DeclinedHasReason", "\"Status\" <> 'Declined' OR \"Reason\" IS NOT NULL");
                    table.CheckConstraint("CK_DentalPlanItems_DoneHasVisit", "(\"Status\" = 'Done') = (\"DoneAppointmentId\" IS NOT NULL AND \"DoneAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_DentalPlanItems_Estimate", "\"EstimateMinor\" >= 0");
                    table.CheckConstraint("CK_DentalPlanItems_Phase", "\"Phase\" BETWEEN 1 AND 9");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DentalEstimateAcceptances_TenantId_PatientId_AcceptedAt",
                schema: "encounters",
                table: "DentalEstimateAcceptances",
                columns: new[] { "TenantId", "PatientId", "AcceptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DentalFindings_TenantId_PatientId_RecordedAt",
                schema: "encounters",
                table: "DentalFindings",
                columns: new[] { "TenantId", "PatientId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DentalPlanItems_TenantId_DoneAppointmentId",
                schema: "encounters",
                table: "DentalPlanItems",
                columns: new[] { "TenantId", "DoneAppointmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_DentalPlanItems_TenantId_PatientId",
                schema: "encounters",
                table: "DentalPlanItems",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.Tenant("encounters", "DentalFindings");
            migrationBuilder.Tenant("encounters", "DentalPlanItems");
            migrationBuilder.Tenant("encounters", "DentalEstimateAcceptances");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DentalEstimateAcceptances",
                schema: "encounters");

            migrationBuilder.DropTable(
                name: "DentalFindings",
                schema: "encounters");

            migrationBuilder.DropTable(
                name: "DentalPlanItems",
                schema: "encounters");
        }
    }
}

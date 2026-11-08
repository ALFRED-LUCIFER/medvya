using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P1-041: timed check-ins, the database's double-booking refusals and clinic baselines (row-level secured like every tenant table). Forward-only: no Down.</summary>
    public partial class PilotMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CheckInMs",
                schema: "scheduling",
                table: "VisitStatusEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BookingConflicts",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingConflicts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetricBaselines",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Metric = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricBaselines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingConflicts_TenantId_FacilityId_OccurredAt",
                schema: "scheduling",
                table: "BookingConflicts",
                columns: new[] { "TenantId", "FacilityId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MetricBaselines_TenantId_FacilityId_Metric",
                schema: "scheduling",
                table: "MetricBaselines",
                columns: new[] { "TenantId", "FacilityId", "Metric" });

            migrationBuilder.Tenant("scheduling", "BookingConflicts");
            migrationBuilder.Tenant("scheduling", "MetricBaselines");
        }
    }
}

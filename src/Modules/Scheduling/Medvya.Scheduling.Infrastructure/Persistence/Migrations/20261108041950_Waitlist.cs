using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P1-027: the waitlist, row-level secured like every tenant table. Forward-only: no Down.</summary>
    public partial class Waitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Waitlist",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: true),
                    VisitType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReasonCategory = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: false),
                    From = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Until = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    OfferCalendarId = table.Column<Guid>(type: "uuid", nullable: true),
                    OfferStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OfferExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Waitlist", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Waitlist_TenantId_FacilityId_State_RequestedAt",
                schema: "scheduling",
                table: "Waitlist",
                columns: new[] { "TenantId", "FacilityId", "State", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Waitlist_TenantId_PatientId",
                schema: "scheduling",
                table: "Waitlist",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.Tenant("scheduling", "Waitlist"); // row-level security like every tenant table
        }
    }
}

using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P2-024: the clinic's care-relationship policy and access requests (row-level secured like every tenant table). Forward-only: no Down.</summary>
    public partial class ClinicalAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClinicalAccess",
                schema: "tenancy",
                table: "Tenants",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "AllFacilityClinicians");

            migrationBuilder.CreateTable(
                name: "AccessRequests",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessRequests", x => x.Id);
                    table.CheckConstraint("CK_AccessRequests_GrantExpires", "\"Status\" <> 'Granted' OR \"ExpiresAt\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_AccessRequests_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "tenancy",
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessRequests_FacilityId_Status_RequestedAt",
                schema: "tenancy",
                table: "AccessRequests",
                columns: new[] { "FacilityId", "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessRequests_PatientId_RequestedBy",
                schema: "tenancy",
                table: "AccessRequests",
                columns: new[] { "PatientId", "RequestedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessRequests_TenantId_FacilityId_PatientId_RequestedBy",
                schema: "tenancy",
                table: "AccessRequests",
                columns: new[] { "TenantId", "FacilityId", "PatientId", "RequestedBy" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.Tenant("tenancy", "AccessRequests");
        }
    }
}

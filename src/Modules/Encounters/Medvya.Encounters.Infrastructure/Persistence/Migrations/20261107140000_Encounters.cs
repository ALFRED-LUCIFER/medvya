using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <summary>MDV-P2-001: the encounter of a visit, row-level secured like every tenant table. Forward-only: no Down.</summary>
    public partial class Encounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "encounters");

            migrationBuilder.CreateTable(
                name: "Encounters",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadClinicianId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AuthorityMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Encounters", x => x.Id);
                    table.CheckConstraint("CK_Encounters_EndedHasTime", "(\"Status\" = 'Ended') = (\"EndedAt\" IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_TenantId_AppointmentId",
                schema: "encounters",
                table: "Encounters",
                columns: new[] { "TenantId", "AppointmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_TenantId_LeadClinicianId_Status",
                schema: "encounters",
                table: "Encounters",
                columns: new[] { "TenantId", "LeadClinicianId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_TenantId_PatientId",
                schema: "encounters",
                table: "Encounters",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.Tenant("encounters", "Encounters");
        }
    }
}

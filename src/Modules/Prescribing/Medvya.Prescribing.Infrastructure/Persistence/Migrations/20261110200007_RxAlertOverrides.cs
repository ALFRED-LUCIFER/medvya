using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RxAlertOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RxAlertOverrides",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RuleSetVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OverriddenBy = table.Column<Guid>(type: "uuid", nullable: false),
                    OverriddenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RxAlertOverrides", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RxAlertOverrides_TenantId_EncounterId",
                schema: "prescribing",
                table: "RxAlertOverrides",
                columns: new[] { "TenantId", "EncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_RxAlertOverrides_TenantId_PatientId",
                schema: "prescribing",
                table: "RxAlertOverrides",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.Tenant("prescribing", "RxAlertOverrides");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RxAlertOverrides",
                schema: "prescribing");
        }
    }
}

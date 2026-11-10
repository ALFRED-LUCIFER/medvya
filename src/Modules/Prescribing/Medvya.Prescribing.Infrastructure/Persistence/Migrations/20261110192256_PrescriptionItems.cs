using System;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrescriptionItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrescriptionItems",
                schema: "prescribing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormularyItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Medicine = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Strength = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Form = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Dose = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    DoseUnit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Route = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Timing = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    Instructions = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Included = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrescriptionItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionItems_TenantId_EncounterId",
                schema: "prescribing",
                table: "PrescriptionItems",
                columns: new[] { "TenantId", "EncounterId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionItems_TenantId_PatientId",
                schema: "prescribing",
                table: "PrescriptionItems",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.Tenant("prescribing", "PrescriptionItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrescriptionItems",
                schema: "prescribing");
        }
    }
}

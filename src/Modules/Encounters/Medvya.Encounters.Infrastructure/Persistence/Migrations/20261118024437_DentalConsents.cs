using Medvya.BuildingBlocks.Infrastructure;
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Encounters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DentalConsents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConsentKey",
                schema: "encounters",
                table: "DentalPlanItems",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DentalConsents",
                schema: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    Language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    SignedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    BlobKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    WithdrawnAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WithdrawnBy = table.Column<Guid>(type: "uuid", nullable: true),
                    WithdrawalReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DentalConsents", x => x.Id);
                    table.CheckConstraint("CK_DentalConsents_Withdrawal", "(\"WithdrawnAt\" IS NULL) = (\"WithdrawalReason\" IS NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DentalConsents_TenantId_PatientId",
                schema: "encounters",
                table: "DentalConsents",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_DentalConsents_TenantId_PlanItemId",
                schema: "encounters",
                table: "DentalConsents",
                columns: new[] { "TenantId", "PlanItemId" });

            migrationBuilder.Tenant("encounters", "DentalConsents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DentalConsents",
                schema: "encounters");

            migrationBuilder.DropColumn(
                name: "ConsentKey",
                schema: "encounters",
                table: "DentalPlanItems");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Prescribing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrescriptionItemsByFacility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionItems_TenantId_FacilityId_EncounterId",
                schema: "prescribing",
                table: "PrescriptionItems",
                columns: new[] { "TenantId", "FacilityId", "EncounterId" })
                .Annotation("Npgsql:IndexInclude", new[] { "Included", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PrescriptionItems_TenantId_FacilityId_EncounterId",
                schema: "prescribing",
                table: "PrescriptionItems");
        }
    }
}

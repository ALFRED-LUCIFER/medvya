using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PatientDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_FacilityId_Mobile",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "FacilityId", "Mobile" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_FacilityId_RegisteredAt",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "FacilityId", "RegisteredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Patients_TenantId_FacilityId_Mobile",
                schema: "patients",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_TenantId_FacilityId_RegisteredAt",
                schema: "patients",
                table: "Patients");
        }
    }
}

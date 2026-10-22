using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentStartIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_FacilityId_Start",
                schema: "scheduling",
                table: "Appointments",
                columns: new[] { "TenantId", "FacilityId", "Start" })
                .Annotation("SqlServer:Include", new[] { "End", "CalendarId", "State", "PatientId", "VisitType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_FacilityId_Start",
                schema: "scheduling",
                table: "Appointments");
        }
    }
}

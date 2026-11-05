using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditTimeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TenantId_OccurredAt",
                schema: "audit",
                table: "AuditEvents",
                columns: new[] { "TenantId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_TenantId_OccurredAt",
                schema: "audit",
                table: "AuditEvents");
        }
    }
}

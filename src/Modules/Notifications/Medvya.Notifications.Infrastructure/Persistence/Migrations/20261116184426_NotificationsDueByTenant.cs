using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationsDueByTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_Status_SendAt",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_Status_SendAt",
                schema: "notifications",
                table: "Notifications",
                columns: new[] { "TenantId", "Status", "SendAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_TenantId_Status_SendAt",
                schema: "notifications",
                table: "Notifications");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Status_SendAt",
                schema: "notifications",
                table: "Notifications",
                columns: new[] { "Status", "SendAt" });
        }
    }
}

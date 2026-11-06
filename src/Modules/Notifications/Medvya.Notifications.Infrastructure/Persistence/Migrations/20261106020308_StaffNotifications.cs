using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffNotificationReads",
                schema: "notifications",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffNotificationReads", x => new { x.NotificationId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "StaffNotifications",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RefId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DedupeKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffNotifications_TenantId_DedupeKey",
                schema: "notifications",
                table: "StaffNotifications",
                columns: new[] { "TenantId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffNotifications_TenantId_FacilityId_CreatedAt",
                schema: "notifications",
                table: "StaffNotifications",
                columns: new[] { "TenantId", "FacilityId", "CreatedAt" });

            // Tenant isolation layer 2 of 2 (ADR-005) on the module's existing policy.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.notifications_tenant_isolation
                    ADD FILTER PREDICATE security.fn_notifications_tenant_access(TenantId) ON notifications.StaffNotifications,
                    ADD BLOCK PREDICATE security.fn_notifications_tenant_access(TenantId) ON notifications.StaffNotifications,
                    ADD FILTER PREDICATE security.fn_notifications_tenant_access(TenantId) ON notifications.StaffNotificationReads,
                    ADD BLOCK PREDICATE security.fn_notifications_tenant_access(TenantId) ON notifications.StaffNotificationReads;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.notifications_tenant_isolation
                    DROP FILTER PREDICATE ON notifications.StaffNotifications,
                    DROP BLOCK PREDICATE ON notifications.StaffNotifications,
                    DROP FILTER PREDICATE ON notifications.StaffNotificationReads,
                    DROP BLOCK PREDICATE ON notifications.StaffNotificationReads;
                """);
            migrationBuilder.DropTable(
                name: "StaffNotificationReads",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "StaffNotifications",
                schema: "notifications");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Preferences",
                schema: "notifications",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Confirmation = table.Column<bool>(type: "bit", nullable: false),
                    Reminder24h = table.Column<bool>(type: "bit", nullable: false),
                    Reminder2h = table.Column<bool>(type: "bit", nullable: false),
                    Changes = table.Column<bool>(type: "bit", nullable: false),
                    Email = table.Column<bool>(type: "bit", nullable: false),
                    Sms = table.Column<bool>(type: "bit", nullable: false),
                    QuietStart = table.Column<TimeOnly>(type: "time", nullable: false),
                    QuietEnd = table.Column<TimeOnly>(type: "time", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Preferences", x => x.TenantId);
                });

            // Tenant isolation layer 2 of 2 (ADR-005) on the module's existing policy.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.notifications_tenant_isolation
                    ADD FILTER PREDICATE security.fn_notifications_tenant_access(TenantId) ON notifications.Preferences,
                    ADD BLOCK PREDICATE security.fn_notifications_tenant_access(TenantId) ON notifications.Preferences;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.notifications_tenant_isolation
                    DROP FILTER PREDICATE ON notifications.Preferences,
                    DROP BLOCK PREDICATE ON notifications.Preferences;
                """);
            migrationBuilder.DropTable(
                name: "Preferences",
                schema: "notifications");
        }
    }
}

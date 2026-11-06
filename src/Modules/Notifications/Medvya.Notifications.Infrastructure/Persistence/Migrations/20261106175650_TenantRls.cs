using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Tenant("notifications", "Attempts");
            migrationBuilder.Tenant("notifications", "Notifications");
            migrationBuilder.Tenant("notifications", "Preferences");
            migrationBuilder.Tenant("notifications", "StaffNotificationReads");
            migrationBuilder.Tenant("notifications", "StaffNotifications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY tenant_select ON notifications."Attempts"; DROP POLICY tenant_insert ON notifications."Attempts"; DROP POLICY tenant_update ON notifications."Attempts"; DROP POLICY tenant_delete ON notifications."Attempts"; ALTER TABLE notifications."Attempts" NO FORCE ROW LEVEL SECURITY; ALTER TABLE notifications."Attempts" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON notifications."Notifications"; DROP POLICY tenant_insert ON notifications."Notifications"; DROP POLICY tenant_update ON notifications."Notifications"; DROP POLICY tenant_delete ON notifications."Notifications"; ALTER TABLE notifications."Notifications" NO FORCE ROW LEVEL SECURITY; ALTER TABLE notifications."Notifications" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON notifications."Preferences"; DROP POLICY tenant_insert ON notifications."Preferences"; DROP POLICY tenant_update ON notifications."Preferences"; DROP POLICY tenant_delete ON notifications."Preferences"; ALTER TABLE notifications."Preferences" NO FORCE ROW LEVEL SECURITY; ALTER TABLE notifications."Preferences" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON notifications."StaffNotificationReads"; DROP POLICY tenant_insert ON notifications."StaffNotificationReads"; DROP POLICY tenant_update ON notifications."StaffNotificationReads"; DROP POLICY tenant_delete ON notifications."StaffNotificationReads"; ALTER TABLE notifications."StaffNotificationReads" NO FORCE ROW LEVEL SECURITY; ALTER TABLE notifications."StaffNotificationReads" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY tenant_select ON notifications."StaffNotifications"; DROP POLICY tenant_insert ON notifications."StaffNotifications"; DROP POLICY tenant_update ON notifications."StaffNotifications"; DROP POLICY tenant_delete ON notifications."StaffNotifications"; ALTER TABLE notifications."StaffNotifications" NO FORCE ROW LEVEL SECURITY; ALTER TABLE notifications."StaffNotifications" DISABLE ROW LEVEL SECURITY;""");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Notifications.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MDV-P5-038: when the push job claimed a staff notification. Existing items count as already pushed: the column is added with a now()
    /// default, which fills existing rows as part of the DDL (row-level security does not apply to it), then the default is dropped so new
    /// items start unpushed. Without this, the first run would push every old item. Forward-only: no Down.
    /// </summary>
    public partial class StaffPush : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE notifications."StaffNotifications" ADD COLUMN "PushedAt" timestamp with time zone DEFAULT now();
                ALTER TABLE notifications."StaffNotifications" ALTER COLUMN "PushedAt" DROP DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_StaffNotifications_Unpushed",
                schema: "notifications",
                table: "StaffNotifications",
                columns: new[] { "TenantId", "CreatedAt" },
                filter: "\"PushedAt\" IS NULL");
        }
    }
}

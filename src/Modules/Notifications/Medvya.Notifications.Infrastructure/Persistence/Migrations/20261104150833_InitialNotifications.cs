using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "Attempts",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AppointmentStart = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SendAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderRef = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_NotificationId",
                schema: "notifications",
                table: "Attempts",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Status_SendAt",
                schema: "notifications",
                table: "Notifications",
                columns: new[] { "Status", "SendAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_AppointmentId",
                schema: "notifications",
                table: "Notifications",
                columns: new[] { "TenantId", "AppointmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_FacilityId_CreatedAt",
                schema: "notifications",
                table: "Notifications",
                columns: new[] { "TenantId", "FacilityId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_SourceEventId_Kind_Channel",
                schema: "notifications",
                table: "Notifications",
                columns: new[] { "TenantId", "SourceEventId", "Kind", "Channel" },
                unique: true);

            // Tenant isolation layer 2 of 2 (ADR-005), with this module's own predicate in the security schema.
            migrationBuilder.Sql("""
                CREATE FUNCTION security.fn_notifications_tenant_access(@tenant_id uniqueidentifier)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS allowed WHERE @tenant_id = CAST(SESSION_CONTEXT(N'tenant_id') AS uniqueidentifier);
                """);
            migrationBuilder.Sql("""
                CREATE SECURITY POLICY security.notifications_tenant_isolation
                    ADD FILTER PREDICATE security.fn_notifications_tenant_access(TenantId) ON notifications.Notifications,
                    ADD BLOCK PREDICATE security.fn_notifications_tenant_access(TenantId) ON notifications.Notifications,
                    ADD FILTER PREDICATE security.fn_notifications_tenant_access(TenantId) ON notifications.Attempts,
                    ADD BLOCK PREDICATE security.fn_notifications_tenant_access(TenantId) ON notifications.Attempts
                WITH (STATE = ON);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY security.notifications_tenant_isolation;");
            migrationBuilder.Sql("DROP FUNCTION security.fn_notifications_tenant_access;");

            migrationBuilder.DropTable(
                name: "Attempts",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "notifications");
        }
    }
}

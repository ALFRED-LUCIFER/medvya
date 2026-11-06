using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AppointmentStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SendAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderRef = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Preferences",
                schema: "notifications",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Confirmation = table.Column<bool>(type: "boolean", nullable: false),
                    Reminder24h = table.Column<bool>(type: "boolean", nullable: false),
                    Reminder2h = table.Column<bool>(type: "boolean", nullable: false),
                    Changes = table.Column<bool>(type: "boolean", nullable: false),
                    Email = table.Column<bool>(type: "boolean", nullable: false),
                    Sms = table.Column<bool>(type: "boolean", nullable: false),
                    QuietStart = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    QuietEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Preferences", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "StaffNotificationReads",
                schema: "notifications",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RefId = table.Column<Guid>(type: "uuid", nullable: true),
                    Detail = table.Column<int>(type: "integer", nullable: true),
                    DedupeKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffNotifications", x => x.Id);
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attempts",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "Preferences",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "StaffNotificationReads",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "StaffNotifications",
                schema: "notifications");
        }
    }
}

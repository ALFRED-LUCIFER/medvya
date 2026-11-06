using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.CreateTable(
                name: "Appointments",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    VisitType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    End = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Room = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ReasonCategory = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReasonText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WalkIn = table.Column<bool>(type: "boolean", nullable: false),
                    Unscheduled = table.Column<bool>(type: "boolean", nullable: false),
                    SeriesId = table.Column<Guid>(type: "uuid", nullable: true),
                    BookedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    BookedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    IdempotencyRequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEvents",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxTenants",
                schema: "scheduling",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxTenants", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "QueueDisplays",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueDisplays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Resources",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleExceptions",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Start = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    End = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleExceptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SlotHolds",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    End = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HeldBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotHolds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SlotMinutes = table.Column<int>(type: "integer", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Breaks = table.Column<string>(type: "jsonb", nullable: true),
                    Hours = table.Column<string>(type: "jsonb", nullable: true),
                    VisitLengths = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentReschedules",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromCalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FromEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ToCalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ToEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReassignReason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentReschedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentReschedules_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "scheduling",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QueueTokens",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    Prefix = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueTokens_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "scheduling",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleSlots",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleSlots_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "scheduling",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VisitStatusEvents",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelReason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitStatusEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitStatusEvents_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "scheduling",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReschedules_AppointmentId",
                schema: "scheduling",
                table: "AppointmentReschedules",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_BookedBy_IdempotencyKeyHash",
                schema: "scheduling",
                table: "Appointments",
                columns: new[] { "TenantId", "BookedBy", "IdempotencyKeyHash" },
                unique: true,
                filter: "\"IdempotencyKeyHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_FacilityId_CalendarId_Start",
                schema: "scheduling",
                table: "Appointments",
                columns: new[] { "TenantId", "FacilityId", "CalendarId", "Start" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_FacilityId_Start",
                schema: "scheduling",
                table: "Appointments",
                columns: new[] { "TenantId", "FacilityId", "Start" })
                .Annotation("Npgsql:IndexInclude", new[] { "End", "CalendarId", "State", "PatientId", "VisitType" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_PatientId",
                schema: "scheduling",
                table: "Appointments",
                columns: new[] { "TenantId", "PatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_SeriesId",
                schema: "scheduling",
                table: "Appointments",
                columns: new[] { "TenantId", "SeriesId" },
                filter: "\"SeriesId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_PublishedAt_OccurredAt",
                schema: "scheduling",
                table: "OutboxEvents",
                columns: new[] { "PublishedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueDisplays_TenantId_FacilityId",
                schema: "scheduling",
                table: "QueueDisplays",
                columns: new[] { "TenantId", "FacilityId" });

            migrationBuilder.CreateIndex(
                name: "IX_QueueDisplays_TokenHash",
                schema: "scheduling",
                table: "QueueDisplays",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueTokens_AppointmentId",
                schema: "scheduling",
                table: "QueueTokens",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QueueTokens_TenantId_FacilityId_Date_Prefix_Number",
                schema: "scheduling",
                table: "QueueTokens",
                columns: new[] { "TenantId", "FacilityId", "Date", "Prefix", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Resources_TenantId_FacilityId_Name",
                schema: "scheduling",
                table: "Resources",
                columns: new[] { "TenantId", "FacilityId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleExceptions_TenantId_FacilityId_CalendarId_FromDate",
                schema: "scheduling",
                table: "ScheduleExceptions",
                columns: new[] { "TenantId", "FacilityId", "CalendarId", "FromDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_AppointmentId",
                schema: "scheduling",
                table: "ScheduleSlots",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_CalendarId_FacilityId_SlotStart",
                schema: "scheduling",
                table: "ScheduleSlots",
                columns: new[] { "CalendarId", "FacilityId", "SlotStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlotHolds_TenantId_FacilityId_CalendarId_Start",
                schema: "scheduling",
                table: "SlotHolds",
                columns: new[] { "TenantId", "FacilityId", "CalendarId", "Start" });

            migrationBuilder.CreateIndex(
                name: "IX_SlotHolds_TenantId_HeldBy",
                schema: "scheduling",
                table: "SlotHolds",
                columns: new[] { "TenantId", "HeldBy" });

            migrationBuilder.CreateIndex(
                name: "IX_Templates_TenantId_FacilityId_CalendarId",
                schema: "scheduling",
                table: "Templates",
                columns: new[] { "TenantId", "FacilityId", "CalendarId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisitStatusEvents_AppointmentId",
                schema: "scheduling",
                table: "VisitStatusEvents",
                column: "AppointmentId");
            // Not under row-level security (tenant ids only): the job that publishes events finds whose scope to enter here.
            // ON CONFLICT makes two bookings of a new tenant at once insert the id once instead of failing on the key.
            migrationBuilder.Sql("""
                CREATE FUNCTION scheduling."OutboxEvents_Tenant"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO scheduling."OutboxTenants" ("TenantId") VALUES (NEW."TenantId") ON CONFLICT DO NOTHING;
                    RETURN NULL;
                END $$;
                CREATE TRIGGER "TR_OutboxEvents_Tenant" AFTER INSERT ON scheduling."OutboxEvents"
                    FOR EACH ROW EXECUTE FUNCTION scheduling."OutboxEvents_Tenant"();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_OutboxEvents_Tenant" ON scheduling."OutboxEvents";
                DROP FUNCTION scheduling."OutboxEvents_Tenant"();
                """);
            migrationBuilder.DropTable(
                name: "AppointmentReschedules",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "OutboxEvents",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "OutboxTenants",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "QueueDisplays",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "QueueTokens",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "Resources",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "ScheduleExceptions",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "ScheduleSlots",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "SlotHolds",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "Templates",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "VisitStatusEvents",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "Appointments",
                schema: "scheduling");
        }
    }
}

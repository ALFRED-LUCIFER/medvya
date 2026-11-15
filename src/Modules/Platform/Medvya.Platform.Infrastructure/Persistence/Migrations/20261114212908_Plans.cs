using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Platform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Plans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanVersions",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    PerDoctor = table.Column<bool>(type: "boolean", nullable: false),
                    Custom = table.Column<bool>(type: "boolean", nullable: false),
                    Doctors = table.Column<int>(type: "integer", nullable: true),
                    Staff = table.Column<int>(type: "integer", nullable: true),
                    Locations = table.Column<int>(type: "integer", nullable: true),
                    AssistVisitsPerDoctorMonth = table.Column<int>(type: "integer", nullable: true),
                    TrialDays = table.Column<int>(type: "integer", nullable: false),
                    Features = table.Column<string[]>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanVersions_Members_CreatedByMemberId",
                        column: x => x.CreatedByMemberId,
                        principalSchema: "platform",
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanPrices",
                schema: "platform",
                columns: table => new
                {
                    PlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Monthly = table.Column<long>(type: "bigint", nullable: true),
                    Yearly = table.Column<long>(type: "bigint", nullable: true),
                    AssistMonthly = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanPrices", x => new { x.PlanVersionId, x.Country });
                    table.ForeignKey(
                        name: "FK_PlanPrices_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalSchema: "platform",
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_Code_Version",
                schema: "platform",
                table: "PlanVersions",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_CreatedByMemberId",
                schema: "platform",
                table: "PlanVersions",
                column: "CreatedByMemberId");

            // Version 1 of Solo, Clinic and Hospital (ADR-019).
            // shortcut: reads the current seed (and pricing.json), so editing either does not change what an applied migration did; a new seed is a new version, saved in the console.
            foreach (var v in PlanSeed.Versions)
            {
                migrationBuilder.InsertData(schema: "platform", table: "PlanVersions",
                    columns: ["Id", "Code", "Version", "PerDoctor", "Custom", "Doctors", "Staff", "Locations", "AssistVisitsPerDoctorMonth", "TrialDays", "Features", "CreatedAt", "Reason"],
                    values: [v.Id, v.Code, v.Version, v.PerDoctor, v.Custom, v.Doctors, v.Staff, v.Locations, v.AssistVisitsPerDoctorMonth, v.TrialDays, v.Features, v.CreatedAt, v.Reason]);
                foreach (var p in v.Prices)
                    migrationBuilder.InsertData(schema: "platform", table: "PlanPrices",
                        columns: ["PlanVersionId", "Country", "Currency", "Monthly", "Yearly", "AssistMonthly"],
                        values: [p.PlanVersionId, p.Country, p.Currency, p.Monthly, p.Yearly, p.AssistMonthly]);
            }

            // A saved version is what subscribers agreed to: never changed or removed, for every role (ENABLE ALWAYS, as the audit log in Baseline).
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_PlanVersions_AppendOnly" BEFORE UPDATE OR DELETE ON platform."PlanVersions" FOR EACH ROW EXECUTE FUNCTION platform.refuse();
                CREATE TRIGGER "TR_PlanVersions_NoTruncate" BEFORE TRUNCATE ON platform."PlanVersions" FOR EACH STATEMENT EXECUTE FUNCTION platform.refuse();
                ALTER TABLE platform."PlanVersions" ENABLE ALWAYS TRIGGER "TR_PlanVersions_AppendOnly", ENABLE ALWAYS TRIGGER "TR_PlanVersions_NoTruncate";
                CREATE TRIGGER "TR_PlanPrices_AppendOnly" BEFORE UPDATE OR DELETE ON platform."PlanPrices" FOR EACH ROW EXECUTE FUNCTION platform.refuse();
                CREATE TRIGGER "TR_PlanPrices_NoTruncate" BEFORE TRUNCATE ON platform."PlanPrices" FOR EACH STATEMENT EXECUTE FUNCTION platform.refuse();
                ALTER TABLE platform."PlanPrices" ENABLE ALWAYS TRIGGER "TR_PlanPrices_AppendOnly", ENABLE ALWAYS TRIGGER "TR_PlanPrices_NoTruncate";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanPrices",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "PlanVersions",
                schema: "platform");
        }
    }
}

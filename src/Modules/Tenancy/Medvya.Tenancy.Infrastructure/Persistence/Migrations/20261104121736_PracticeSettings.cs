using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PracticeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                schema: "tenancy",
                table: "Facilities",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Closures",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Closures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Closures_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "tenancy",
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperatingHours",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Weekday = table.Column<int>(type: "int", nullable: false),
                    Opens = table.Column<TimeOnly>(type: "time", nullable: false),
                    Closes = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatingHours", x => x.Id);
                    table.CheckConstraint("CK_OperatingHours_OpensBeforeCloses", "[Opens] < [Closes]");
                    table.ForeignKey(
                        name: "FK_OperatingHours_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "tenancy",
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RegistrationNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TaxNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    LogoKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Profiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SettingsChanges",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Section = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Before = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    After = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingsChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettingsChanges_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Closures_FacilityId_Date",
                schema: "tenancy",
                table: "Closures",
                columns: new[] { "FacilityId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatingHours_FacilityId_Weekday",
                schema: "tenancy",
                table: "OperatingHours",
                columns: new[] { "FacilityId", "Weekday" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_TenantId",
                schema: "tenancy",
                table: "Profiles",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettingsChanges_TenantId_OccurredAt",
                schema: "tenancy",
                table: "SettingsChanges",
                columns: new[] { "TenantId", "OccurredAt" });

            // Tenant isolation layer 2 of 2 (ADR-005): same row-level security policy as the other tenancy tables.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.Profiles,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.Profiles,
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.OperatingHours,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.OperatingHours,
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.Closures,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.Closures,
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.SettingsChanges,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.SettingsChanges;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    DROP FILTER PREDICATE ON tenancy.Profiles,
                    DROP BLOCK PREDICATE ON tenancy.Profiles,
                    DROP FILTER PREDICATE ON tenancy.OperatingHours,
                    DROP BLOCK PREDICATE ON tenancy.OperatingHours,
                    DROP FILTER PREDICATE ON tenancy.Closures,
                    DROP BLOCK PREDICATE ON tenancy.Closures,
                    DROP FILTER PREDICATE ON tenancy.SettingsChanges,
                    DROP BLOCK PREDICATE ON tenancy.SettingsChanges;
                """);

            migrationBuilder.DropTable(
                name: "Closures",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "OperatingHours",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Profiles",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "SettingsChanges",
                schema: "tenancy");

            migrationBuilder.DropColumn(
                name: "Address",
                schema: "tenancy",
                table: "Facilities");
        }
    }
}

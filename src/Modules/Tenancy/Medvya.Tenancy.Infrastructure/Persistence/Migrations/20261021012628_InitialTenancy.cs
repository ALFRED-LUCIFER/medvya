using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenancy");

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CountryCode = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    ResidencyRegion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Facilities",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Facilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Facilities_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Memberships",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Memberships_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "tenancy",
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_TenantId",
                schema: "tenancy",
                table: "Facilities",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_FacilityId",
                schema: "tenancy",
                table: "Memberships",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_TenantId_UserId_FacilityId",
                schema: "tenancy",
                table: "Memberships",
                columns: new[] { "TenantId", "UserId", "FacilityId" },
                unique: true);

            // Tenant isolation layer 2 of 2 (ADR-005): row-level security on SESSION_CONTEXT('tenant_id'),
            // set read-only per connection by TenantSessionInterceptor. Holds even when an EF query filter is bypassed.
            migrationBuilder.EnsureSchema(name: "security");
            migrationBuilder.Sql("""
                CREATE FUNCTION security.fn_tenant_access(@tenant_id uniqueidentifier)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS allowed WHERE @tenant_id = CAST(SESSION_CONTEXT(N'tenant_id') AS uniqueidentifier);
                """);
            migrationBuilder.Sql("""
                CREATE SECURITY POLICY security.tenant_isolation
                    ADD FILTER PREDICATE security.fn_tenant_access(Id) ON tenancy.Tenants,
                    ADD BLOCK PREDICATE security.fn_tenant_access(Id) ON tenancy.Tenants,
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.Facilities,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.Facilities,
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.Memberships,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.Memberships
                WITH (STATE = ON);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY security.tenant_isolation;");
            migrationBuilder.Sql("DROP FUNCTION security.fn_tenant_access;");

            migrationBuilder.DropTable(
                name: "Memberships",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Facilities",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "tenancy");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomRoleId",
                schema: "tenancy",
                table: "Memberships",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomRoles",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    BaseRole = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Capabilities = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomRoles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_CustomRoleId",
                schema: "tenancy",
                table: "Memberships",
                column: "CustomRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomRoles_TenantId_Name",
                schema: "tenancy",
                table: "CustomRoles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Memberships_CustomRoles_CustomRoleId",
                schema: "tenancy",
                table: "Memberships",
                column: "CustomRoleId",
                principalSchema: "tenancy",
                principalTable: "CustomRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Tenant isolation layer 2 of 2 (ADR-005): same row-level security policy as the other tenancy tables.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.CustomRoles,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.CustomRoles;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    DROP FILTER PREDICATE ON tenancy.CustomRoles,
                    DROP BLOCK PREDICATE ON tenancy.CustomRoles;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_CustomRoles_CustomRoleId",
                schema: "tenancy",
                table: "Memberships");

            migrationBuilder.DropTable(
                name: "CustomRoles",
                schema: "tenancy");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_CustomRoleId",
                schema: "tenancy",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "CustomRoleId",
                schema: "tenancy",
                table: "Memberships");
        }
    }
}

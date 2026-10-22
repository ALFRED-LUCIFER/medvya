using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Invitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Memberships_TenantId_UserId_FacilityId",
                schema: "tenancy",
                table: "Memberships");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                schema: "tenancy",
                table: "Memberships",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateTable(
                name: "Invitations",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    InvitedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invitations_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalSchema: "tenancy",
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_TenantId_UserId_FacilityId",
                schema: "tenancy",
                table: "Memberships",
                columns: new[] { "TenantId", "UserId", "FacilityId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Memberships_UserUnlessInvited",
                schema: "tenancy",
                table: "Memberships",
                sql: "[Status] = 'Invited' OR [UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_MembershipId",
                schema: "tenancy",
                table: "Invitations",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TokenHash",
                schema: "tenancy",
                table: "Invitations",
                column: "TokenHash",
                unique: true);

            // Tenant isolation layer 2 of 2 (ADR-005): same row-level security policy as the other tenancy tables.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.Invitations,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.Invitations;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    DROP FILTER PREDICATE ON tenancy.Invitations,
                    DROP BLOCK PREDICATE ON tenancy.Invitations;
                """);

            migrationBuilder.DropTable(
                name: "Invitations",
                schema: "tenancy");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_TenantId_UserId_FacilityId",
                schema: "tenancy",
                table: "Memberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Memberships_UserUnlessInvited",
                schema: "tenancy",
                table: "Memberships");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                schema: "tenancy",
                table: "Memberships",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_TenantId_UserId_FacilityId",
                schema: "tenancy",
                table: "Memberships",
                columns: new[] { "TenantId", "UserId", "FacilityId" },
                unique: true);
        }
    }
}

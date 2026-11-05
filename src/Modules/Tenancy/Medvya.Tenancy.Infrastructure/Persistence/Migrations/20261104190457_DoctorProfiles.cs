using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DoctorProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "tenancy",
                table: "DoctorLicences",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpiresOn",
                schema: "tenancy",
                table: "DoctorLicences",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAt",
                schema: "tenancy",
                table: "DoctorLicences",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "tenancy",
                table: "DoctorLicences",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerifiedBy",
                schema: "tenancy",
                table: "DoctorLicences",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DoctorProfiles",
                schema: "tenancy",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Specialty = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Qualifications = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Languages = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NewVisitFeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    FollowUpFeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    SignatureKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorProfiles", x => x.MembershipId);
                    table.ForeignKey(
                        name: "FK_DoctorProfiles_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalSchema: "tenancy",
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Layer 2 of tenant isolation (database-security.md): the new table joins the tenant policy.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.DoctorProfiles,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.DoctorProfiles;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    DROP FILTER PREDICATE ON tenancy.DoctorProfiles,
                    DROP BLOCK PREDICATE ON tenancy.DoctorProfiles;
                """);

            migrationBuilder.DropTable(
                name: "DoctorProfiles",
                schema: "tenancy");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "tenancy",
                table: "DoctorLicences");

            migrationBuilder.DropColumn(
                name: "ExpiresOn",
                schema: "tenancy",
                table: "DoctorLicences");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                schema: "tenancy",
                table: "DoctorLicences");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "tenancy",
                table: "DoctorLicences");

            migrationBuilder.DropColumn(
                name: "VerifiedBy",
                schema: "tenancy",
                table: "DoctorLicences");
        }
    }
}

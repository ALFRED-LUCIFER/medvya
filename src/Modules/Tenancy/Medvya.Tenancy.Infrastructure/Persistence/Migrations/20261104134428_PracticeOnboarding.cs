using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PracticeOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DoctorLicences",
                schema: "tenancy",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Authority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CertificateName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorLicences", x => x.MembershipId);
                    table.ForeignKey(
                        name: "FK_DoctorLicences_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalSchema: "tenancy",
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PracticeDefaults",
                schema: "tenancy",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Specialty = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Qualifications = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Languages = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SlotMinutes = table.Column<int>(type: "int", nullable: false),
                    NewVisitFeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    FollowUpFeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    WeekdayHours = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SaturdayHours = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NoteTemplates = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeDefaults", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_PracticeDefaults_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Tenant isolation layer 2 of 2 (ADR-005): same row-level security policy as the other tenancy tables.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.PracticeDefaults,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.PracticeDefaults,
                    ADD FILTER PREDICATE security.fn_tenant_access(TenantId) ON tenancy.DoctorLicences,
                    ADD BLOCK PREDICATE security.fn_tenant_access(TenantId) ON tenancy.DoctorLicences;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.tenant_isolation
                    DROP FILTER PREDICATE ON tenancy.PracticeDefaults,
                    DROP BLOCK PREDICATE ON tenancy.PracticeDefaults,
                    DROP FILTER PREDICATE ON tenancy.DoctorLicences,
                    DROP BLOCK PREDICATE ON tenancy.DoctorLicences;
                """);

            migrationBuilder.DropTable(
                name: "DoctorLicences",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "PracticeDefaults",
                schema: "tenancy");
        }
    }
}

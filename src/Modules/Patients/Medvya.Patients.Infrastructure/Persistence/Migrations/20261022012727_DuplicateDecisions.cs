using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DuplicateDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MergeRequests",
                schema: "patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidatePatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MergeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MergeRequests_Patients_CandidatePatientId",
                        column: x => x.CandidatePatientId,
                        principalSchema: "patients",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MergeRequests_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "patients",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_FacilityId_DateOfBirth",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "FacilityId", "DateOfBirth" });

            migrationBuilder.CreateIndex(
                name: "IX_MergeRequests_CandidatePatientId",
                schema: "patients",
                table: "MergeRequests",
                column: "CandidatePatientId");

            migrationBuilder.CreateIndex(
                name: "IX_MergeRequests_PatientId",
                schema: "patients",
                table: "MergeRequests",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_MergeRequests_TenantId_PatientId_CandidatePatientId",
                schema: "patients",
                table: "MergeRequests",
                columns: new[] { "TenantId", "PatientId", "CandidatePatientId" },
                unique: true);

            // Tenant isolation layer 2 of 2 (ADR-005) for the new table, on the module's existing policy.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.patients_tenant_isolation
                    ADD FILTER PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.MergeRequests,
                    ADD BLOCK PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.MergeRequests;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.patients_tenant_isolation
                    DROP FILTER PREDICATE ON patients.MergeRequests,
                    DROP BLOCK PREDICATE ON patients.MergeRequests;
                """);
            migrationBuilder.DropTable(
                name: "MergeRequests",
                schema: "patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_TenantId_FacilityId_DateOfBirth",
                schema: "patients",
                table: "Patients");
        }
    }
}

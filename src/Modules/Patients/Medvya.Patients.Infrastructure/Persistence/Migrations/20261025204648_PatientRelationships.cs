using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PatientRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Relationships",
                schema: "patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Relationship = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsEmergencyContact = table.Column<bool>(type: "bit", nullable: false),
                    IsGuardian = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Relationships_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "patients",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_PatientId",
                schema: "patients",
                table: "Relationships",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Relationships_TenantId_PatientId",
                schema: "patients",
                table: "Relationships",
                columns: new[] { "TenantId", "PatientId" });

            // Dynamic data masking like the patient's own name and phone (MDV-P0-009): roles without UNMASK see default().
            migrationBuilder.Sql("ALTER TABLE patients.Relationships ALTER COLUMN [Name] ADD MASKED WITH (FUNCTION = 'default()');");
            migrationBuilder.Sql("ALTER TABLE patients.Relationships ALTER COLUMN [Phone] ADD MASKED WITH (FUNCTION = 'default()');");

            // Tenant isolation layer 2 of 2 (ADR-005) for the new table, on the module's existing policy.
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.patients_tenant_isolation
                    ADD FILTER PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.Relationships,
                    ADD BLOCK PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.Relationships;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY security.patients_tenant_isolation
                    DROP FILTER PREDICATE ON patients.Relationships,
                    DROP BLOCK PREDICATE ON patients.Relationships;
                """);
            migrationBuilder.DropTable(
                name: "Relationships",
                schema: "patients");
        }
    }
}

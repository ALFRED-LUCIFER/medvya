using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "patients");

            migrationBuilder.CreateTable(
                name: "Patients",
                schema: "patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mrn = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ArabicName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    DateOfBirthEstimated = table.Column<bool>(type: "boolean", nullable: false),
                    SexAtBirth = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GenderIdentity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PreferredLanguage = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: true),
                    Nationality = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    Mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AlternatePhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RegisteredBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    IdempotencyRequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MergedIntoPatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    Address_City = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Address_District = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Address_Line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Address_Line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Address_Makani = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Address_PoBox = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Address_PostalCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Address_Region = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsentReceipts",
                schema: "patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Channel = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    PolicyVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Jurisdiction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Granted = table.Column<bool>(type: "boolean", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentReceipts_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "patients",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Identifiers",
                schema: "patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scheme = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LookupValue = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DisplayValue = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    MovedFromPatientId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Identifiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Identifiers_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "patients",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MergeRequests",
                schema: "patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidatePatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FirstApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    SecondApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    MergedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UnmergedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UnmergedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "PayerCoverages",
                schema: "patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PayerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Plan = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MemberNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Network = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayerCoverages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayerCoverages_Patients_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "patients",
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Relationships",
                schema: "patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Relationship = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsEmergencyContact = table.Column<bool>(type: "boolean", nullable: false),
                    IsGuardian = table.Column<bool>(type: "boolean", nullable: false)
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
                name: "IX_ConsentReceipts_PatientId",
                schema: "patients",
                table: "ConsentReceipts",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentReceipts_TenantId_PatientId_Type_DecidedAt",
                schema: "patients",
                table: "ConsentReceipts",
                columns: new[] { "TenantId", "PatientId", "Type", "DecidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Identifiers_PatientId",
                schema: "patients",
                table: "Identifiers",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Identifiers_TenantId_MovedFromPatientId",
                schema: "patients",
                table: "Identifiers",
                columns: new[] { "TenantId", "MovedFromPatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_Identifiers_TenantId_Scheme_LookupValue",
                schema: "patients",
                table: "Identifiers",
                columns: new[] { "TenantId", "Scheme", "LookupValue" });

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

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_FacilityId_DateOfBirth",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "FacilityId", "DateOfBirth" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_FacilityId_FullName",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "FacilityId", "FullName" })
                .Annotation("Npgsql:IndexInclude", new[] { "ArabicName", "DateOfBirth", "DateOfBirthEstimated", "SexAtBirth", "Mrn", "Status", "CountryCode", "RegisteredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_FacilityId_Mobile",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "FacilityId", "Mobile" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_FacilityId_Mrn",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "FacilityId", "Mrn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_FacilityId_RegisteredAt",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "FacilityId", "RegisteredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_MergedIntoPatientId",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "MergedIntoPatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_RegisteredBy_IdempotencyKeyHash",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "RegisteredBy", "IdempotencyKeyHash" },
                unique: true,
                filter: "\"IdempotencyKeyHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayerCoverages_PatientId",
                schema: "patients",
                table: "PayerCoverages",
                column: "PatientId",
                unique: true);

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
            // Facility-scoped MRN counters, used only by PatientRegistry's upsert; not part of the EF model.
            migrationBuilder.Sql("""
                CREATE TABLE patients."MrnCounters" (
                    "TenantId" uuid NOT NULL,
                    "FacilityId" uuid NOT NULL,
                    "LastNumber" integer NOT NULL,
                    CONSTRAINT "PK_MrnCounters" PRIMARY KEY ("TenantId", "FacilityId")
                );
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE patients.\"MrnCounters\";");
            migrationBuilder.DropTable(
                name: "ConsentReceipts",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "Identifiers",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "MergeRequests",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "PayerCoverages",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "Relationships",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "Patients",
                schema: "patients");
        }
    }
}

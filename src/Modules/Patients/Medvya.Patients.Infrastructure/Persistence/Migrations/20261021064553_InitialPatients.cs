using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPatients : Migration
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mrn = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CountryCode = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ArabicName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    DateOfBirthEstimated = table.Column<bool>(type: "bit", nullable: false),
                    SexAtBirth = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GenderIdentity = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PreferredLanguage = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    Nationality = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: true),
                    Mobile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AlternatePhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RegisteredBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Address_City = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address_District = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address_Line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address_Line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Address_Makani = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Address_PoBox = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Address_PostalCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Address_Region = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patients", x => x.Id);
                });

            // Always Encrypted (MDV-P0-009) on CEK_Patients, created beforehand by ColumnEncryption.EnsureKeysAsync. EF Core cannot
            // express ENCRYPTED WITH, hence raw SQL; the columns match the model. Deterministic where the desk searches by exact
            // value (ABHA, Emirates ID, insurance member number; BIN2 collation required), randomized for display-only values.
            migrationBuilder.Sql("""
                CREATE TABLE patients.Identifiers (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_Identifiers PRIMARY KEY,
                    TenantId uniqueidentifier NOT NULL,
                    PatientId uniqueidentifier NOT NULL CONSTRAINT FK_Identifiers_Patients_PatientId REFERENCES patients.Patients (Id),
                    Scheme nvarchar(20) NOT NULL,
                    LookupValue nvarchar(64) COLLATE Latin1_General_BIN2
                        ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = CEK_Patients, ENCRYPTION_TYPE = DETERMINISTIC, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NULL,
                    DisplayValue nvarchar(64)
                        ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = CEK_Patients, ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NULL,
                    ExpiresOn date NULL
                );
                """);
            migrationBuilder.Sql("""
                CREATE TABLE patients.PayerCoverages (
                    Id uniqueidentifier NOT NULL CONSTRAINT PK_PayerCoverages PRIMARY KEY,
                    TenantId uniqueidentifier NOT NULL,
                    PatientId uniqueidentifier NOT NULL CONSTRAINT FK_PayerCoverages_Patients_PatientId REFERENCES patients.Patients (Id),
                    Kind nvarchar(20) NOT NULL,
                    PayerName nvarchar(200) NULL,
                    [Plan] nvarchar(100) NULL,
                    MemberNumber nvarchar(50) COLLATE Latin1_General_BIN2
                        ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = CEK_Patients, ENCRYPTION_TYPE = DETERMINISTIC, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NULL,
                    ValidTo date NULL,
                    Network nvarchar(100) NULL
                );
                """);

            // Facility-scoped MRN counters, used only by PatientRegistry's MERGE; not part of the EF model.
            migrationBuilder.Sql("""
                CREATE TABLE patients.MrnCounters (
                    TenantId uniqueidentifier NOT NULL,
                    FacilityId uniqueidentifier NOT NULL,
                    LastNumber int NOT NULL,
                    CONSTRAINT PK_MrnCounters PRIMARY KEY (TenantId, FacilityId)
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Identifiers_PatientId",
                schema: "patients",
                table: "Identifiers",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Identifiers_TenantId_Scheme_LookupValue",
                schema: "patients",
                table: "Identifiers",
                columns: new[] { "TenantId", "Scheme", "LookupValue" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_FacilityId_Mrn",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "FacilityId", "Mrn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayerCoverages_PatientId",
                schema: "patients",
                table: "PayerCoverages",
                column: "PatientId",
                unique: true);

            // Dynamic data masking (MDV-P0-009): roles without UNMASK (reporting, support) see default()/email() masks.
            // The app's module role holds UNMASK on this schema only (infra/sql/roles.sql). A later ALTER COLUMN must keep the mask.
            foreach (var (column, function) in new[]
            {
                ("FullName", "default()"), ("ArabicName", "default()"), ("DateOfBirth", "default()"), ("GenderIdentity", "default()"),
                ("Mobile", "default()"), ("AlternatePhone", "default()"), ("Email", "email()"),
                ("Address_Line1", "default()"), ("Address_Line2", "default()"), ("Address_City", "default()"), ("Address_District", "default()"),
                ("Address_Region", "default()"), ("Address_PostalCode", "default()"), ("Address_Makani", "default()"), ("Address_PoBox", "default()"),
            })
            {
                migrationBuilder.Sql($"ALTER TABLE patients.Patients ALTER COLUMN [{column}] ADD MASKED WITH (FUNCTION = '{function}');");
            }

            // Tenant isolation layer 2 of 2 (ADR-005). Own predicate in the security schema, so this module does not depend on
            // Tenancy's migrations and the migrator needs no SELECT on patients.
            migrationBuilder.Sql("""
                CREATE FUNCTION security.fn_patients_tenant_access(@tenant_id uniqueidentifier)
                RETURNS TABLE WITH SCHEMABINDING AS
                RETURN SELECT 1 AS allowed WHERE @tenant_id = CAST(SESSION_CONTEXT(N'tenant_id') AS uniqueidentifier);
                """);
            migrationBuilder.Sql("""
                CREATE SECURITY POLICY security.patients_tenant_isolation
                    ADD FILTER PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.Patients,
                    ADD BLOCK PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.Patients,
                    ADD FILTER PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.Identifiers,
                    ADD BLOCK PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.Identifiers,
                    ADD FILTER PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.PayerCoverages,
                    ADD BLOCK PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.PayerCoverages,
                    ADD FILTER PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.MrnCounters,
                    ADD BLOCK PREDICATE security.fn_patients_tenant_access(TenantId) ON patients.MrnCounters
                WITH (STATE = ON);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY security.patients_tenant_isolation;");
            migrationBuilder.Sql("DROP FUNCTION security.fn_patients_tenant_access;");
            migrationBuilder.Sql("DROP TABLE patients.MrnCounters;");

            migrationBuilder.DropTable(
                name: "Identifiers",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "PayerCoverages",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "Patients",
                schema: "patients");
        }
    }
}

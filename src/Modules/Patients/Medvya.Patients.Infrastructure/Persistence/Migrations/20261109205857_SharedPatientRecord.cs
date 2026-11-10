using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SharedPatientRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MDV-P1-072: the tenant is the organisation and its locations share one MRN sequence. Existing MRNs stay valid; only where two
            // facilities of one tenant issued the same MRN does one holder change. Deterministic rule: the earliest registration (RegisteredAt,
            // then Id) keeps the number; each later holder gets the next free number after the tenant's highest, in registration order.
            // Nothing is deleted. Row-level security binds the owner, so this runs once per tenant with the tenant set for the transaction
            // (tenancy."TenantIndex" lists every tenant). The per-facility counters collapse into one counter per tenant, at or above every MRN in use.
            migrationBuilder.Sql("""
                ALTER TABLE patients."MrnCounters" DROP CONSTRAINT "PK_MrnCounters";
                ALTER TABLE patients."MrnCounters" DROP COLUMN "FacilityId";
                DO $$
                DECLARE t uuid; counter integer; highest integer;
                BEGIN
                    FOR t IN SELECT "TenantId" FROM tenancy."TenantIndex" LOOP
                        PERFORM set_config('medvya.tenant_id', t::text, true);
                        SELECT coalesce(max("LastNumber"), 0) INTO counter FROM patients."MrnCounters";
                        SELECT coalesce(max(CASE WHEN "Mrn" ~ '^[0-9]+$' THEN "Mrn"::integer END), 0) INTO highest FROM patients."Patients";
                        highest := GREATEST(highest, counter);
                        UPDATE patients."Patients" p SET "Mrn" = lpad((highest + x.n)::text, 6, '0')
                          FROM (SELECT "Id", row_number() OVER (ORDER BY "RegisteredAt", "Id") AS n
                                  FROM (SELECT "Id", "RegisteredAt", row_number() OVER (PARTITION BY "Mrn" ORDER BY "RegisteredAt", "Id") AS rank
                                          FROM patients."Patients") ranked
                                 WHERE rank > 1) x
                         WHERE p."Id" = x."Id";
                        SELECT GREATEST(highest, coalesce(max(CASE WHEN "Mrn" ~ '^[0-9]+$' THEN "Mrn"::integer END), 0)) INTO highest FROM patients."Patients";
                        DELETE FROM patients."MrnCounters";
                        IF highest > 0 THEN INSERT INTO patients."MrnCounters" ("TenantId", "LastNumber") VALUES (t, highest); END IF;
                    END LOOP;
                    PERFORM set_config('medvya.tenant_id', '', true);
                END $$;
                ALTER TABLE patients."MrnCounters" ADD CONSTRAINT "PK_MrnCounters" PRIMARY KEY ("TenantId");
                """);

            migrationBuilder.DropIndex(
                name: "IX_Patients_TenantId_FacilityId_DateOfBirth",
                schema: "patients",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_TenantId_FacilityId_FullName",
                schema: "patients",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_TenantId_FacilityId_Mobile",
                schema: "patients",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_TenantId_FacilityId_Mrn",
                schema: "patients",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_TenantId_FacilityId_RegisteredAt",
                schema: "patients",
                table: "Patients");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_DateOfBirth",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "DateOfBirth" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_FullName",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "FullName" })
                .Annotation("Npgsql:IndexInclude", new[] { "ArabicName", "DateOfBirth", "DateOfBirthEstimated", "SexAtBirth", "Mrn", "Status", "CountryCode", "RegisteredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_Mobile",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "Mobile" });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_Mrn",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "Mrn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_TenantId_RegisteredAt",
                schema: "patients",
                table: "Patients",
                columns: new[] { "TenantId", "RegisteredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Forward-only: renumbered MRNs and merged counters cannot be restored.");
    }
}

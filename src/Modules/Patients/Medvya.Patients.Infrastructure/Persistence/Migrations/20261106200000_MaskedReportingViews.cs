using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MDV-P0-037: support and reporting read only these masked views (schema reporting, from infra/postgres/roles.sql), never the patients
    /// tables. Initials and a ten-year age band instead of name and date of birth; no contact, address or identifier value (an identifier
    /// shows its scheme and length only). The views are owned by medvya_owner and the base tables FORCE row-level security, so a reporting
    /// session still sees only its own tenant's rows. security_barrier keeps a caller's predicates from running before the view's.
    /// Forward-only: no Down.
    /// </summary>
    [DbContext(typeof(PatientsDbContext))]
    [Migration("20261106200000_MaskedReportingViews")]
    public partial class MaskedReportingViews : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIEW reporting."Patients" WITH (security_barrier = true) AS
                SELECT p."Id", p."TenantId", p."FacilityId", p."Mrn", p."CountryCode",
                       (SELECT string_agg(upper(left(w, 1)), '') FROM regexp_split_to_table(btrim(p."FullName"), '\s+') AS w WHERE w ~ '^[[:alpha:]]') AS "Initials",
                       CASE WHEN p."DateOfBirth" IS NULL THEN NULL
                            ELSE (date_part('year', age(current_date, p."DateOfBirth"))::int / 10 * 10)::text || '-' || (date_part('year', age(current_date, p."DateOfBirth"))::int / 10 * 10 + 9)::text
                       END AS "AgeBand",
                       p."SexAtBirth", p."Status", p."RegisteredAt", p."MergedIntoPatientId"
                FROM patients."Patients" p;

                CREATE VIEW reporting."Identifiers" WITH (security_barrier = true) AS
                SELECT i."Id", i."TenantId", i."PatientId", i."Scheme",
                       -- Plaintext length: the ciphertext adds a version byte, a 12-byte nonce and a 16-byte tag (NationalIdProtector).
                       octet_length(coalesce(i."LookupValue", i."DisplayValue")) - 29 AS "ValueLength"
                FROM patients."Identifiers" i;
                """);
        }
    }
}

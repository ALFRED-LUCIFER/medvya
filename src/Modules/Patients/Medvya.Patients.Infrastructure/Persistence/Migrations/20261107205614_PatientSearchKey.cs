using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Patients.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PatientSearchKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // pg_trgm is created by the administrator step (infra/postgres/roles.sql): the owner role cannot CREATE EXTENSION. Fail early and plainly if it is missing.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm') THEN
                        RAISE EXCEPTION 'pg_trgm is missing: run db-migrate admin first (and on Azure add PG_TRGM to azure.extensions)';
                    END IF;
                END $$;
                """);

            // Folds a name so spellings meet: Mohamed = Muhammad, Krishnann = Krishnan, Shree = Sree, D'Souza = Dsouza, Laxmi = Lakshmi, Fathima = Fatima.
            // Letters outside the Latin rules (Devanagari, Arabic) pass through lower-cased. IMMUTABLE so a generated column can hold it.
            // Changing these rules later means a table rewrite: ship the change as a new function name and column, never an edit in place.
            migrationBuilder.Sql("""
                CREATE FUNCTION patients.fold_name(t text) RETURNS text LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE AS $f$
                DECLARE s text := lower(coalesce(t, ''));
                BEGIN
                    s := translate(s, '.''', '');
                    s := translate(s, '-,', '  ');
                    s := replace(s, 'x', 'ks');
                    s := replace(s, 'sh', 's'); s := replace(s, 'ph', 'f'); s := replace(s, 'ck', 'k'); s := replace(s, 'kh', 'k'); s := replace(s, 'gh', 'g');
                    s := replace(s, 'th', 't'); s := replace(s, 'dh', 'd'); s := replace(s, 'bh', 'b'); s := replace(s, 'w', 'v');
                    s := replace(s, 'ee', 'i'); s := replace(s, 'ea', 'i'); s := replace(s, 'oo', 'u'); s := replace(s, 'ou', 'u');
                    s := regexp_replace(s, '(.)\1', '\1', 'g');
                    s := translate(s, 'oey', 'uai');
                    RETURN btrim(regexp_replace(s, '\s+', ' ', 'g'));
                END $f$;
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION patients.search_key(full_name text, arabic_name text) RETURNS text LANGUAGE sql IMMUTABLE PARALLEL SAFE AS
                $$ SELECT patients.fold_name(full_name) || coalesce(' ' || nullif(patients.fold_name(arabic_name), ''), '') $$;
                """);

            migrationBuilder.AddColumn<string>(
                name: "SearchKey",
                schema: "patients",
                table: "Patients",
                type: "text",
                nullable: true,
                computedColumnSql: "patients.search_key(\"FullName\", \"ArabicName\")",
                stored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchKey",
                schema: "patients",
                table: "Patients");
            migrationBuilder.Sql("DROP FUNCTION patients.search_key(text, text); DROP FUNCTION patients.fold_name(text);");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>
/// Tamper-evident append-only tables without SQL Server ledger (ADR-012, MDV-P0-036). A ledgered table has "Id" uuid and "TenantId" uuid;
/// <c>migrationBuilder.AddLedger("schema", "Table")</c> is the whole opt-in (one line in a forward-only migration).
///
/// Canonical encoding, defined once in ledger.row_hash: SHA-256 over UTF-8 of
///   hex(PrevHash) || '|' || (jsonb object of the table's hashed columns)::text
/// The hashed columns are listed per table in ledger."Tables"."Columns" (every column but PrevHash and RowHash when the table was
/// ledgered), so a column added later does not change any hash and old chains keep verifying (MDV-P0-041). shortcut: a column added
/// later is not covered by the hash; covering one needs a new hash version per row, add it when a ledgered table gains a column that matters.
/// jsonb text has a fixed key order and the functions pin TimeZone to UTC, so every session produces the same bytes. The row
/// includes its own "ChainSeq" (1, 2, 3 ... per tenant and table), so reordering or removing a middle row breaks the chain.
/// PrevHash of the first row is 32 zero bytes. ledger."Tables" is written only by migrations (the module roles may read it), and the
/// functions act only on a listed table that really carries the chain trigger (ledger.is_ledger), so a stray name is ignored.
///
/// Database-side: a BEFORE INSERT trigger takes a per-tenant-and-table advisory lock until commit, reads the chain head and fills
/// ChainSeq, PrevHash and RowHash (anything the client sent is overwritten), so concurrent inserts for one tenant queue and never fork
/// the chain; other tenants do not wait. BEFORE UPDATE/DELETE/TRUNCATE triggers refuse for every role, and are ENABLE ALWAYS so
/// session_replication_role = replica does not skip them. shortcut: a superuser can still DISABLE TRIGGER and rewrite rows; that is
/// not prevented, it is detected: ledger.verify_chain finds an altered or removed middle row, and the daily signed digest in immutable
/// Blob catches a rewrite of the tail or a deleted last row. Deadlock risk: one transaction inserting into two ledgered tables in the
/// opposite order to another; upgrade to a fixed lock order if it shows up.
/// </summary>
public static class LedgerSql
{
    const string Zero = "decode(repeat('00', 32), 'hex')";

    const string Install = $$"""
        -- Schema ledger comes from infra/postgres/roles.sql (the owner may not create schemas; Postgres checks that even for IF NOT EXISTS).
        CREATE TABLE IF NOT EXISTS ledger."Tables" ("Name" text PRIMARY KEY);
        -- Which tenant has a chain in which table: ids only, no row-level security, so the digest job can list chains and then read each
        -- one inside its own tenant (it never bypasses RLS).
        CREATE TABLE IF NOT EXISTS ledger."Chains" ("Table" text NOT NULL REFERENCES ledger."Tables", "TenantId" uuid NOT NULL, PRIMARY KEY ("Table", "TenantId"));

        -- The columns a table hashes: all but the chain's own hash columns, in column order.
        CREATE OR REPLACE FUNCTION ledger.columns_of(tbl text) RETURNS text[] LANGUAGE sql STABLE AS $$
            SELECT array_agg(a.attname::text ORDER BY a.attnum) FROM pg_attribute a
            WHERE a.attrelid = format('%I.%I', split_part(tbl, '.', 1), split_part(tbl, '.', 2))::regclass
              AND a.attnum > 0 AND NOT a.attisdropped AND a.attname NOT IN ('PrevHash', 'RowHash')
        $$;
        -- MDV-P0-041: tables ledgered before the list existed hash every column they had then, which is exactly their current list.
        ALTER TABLE ledger."Tables" ADD COLUMN IF NOT EXISTS "Columns" text[];
        UPDATE ledger."Tables" SET "Columns" = ledger.columns_of("Name") WHERE "Columns" IS NULL;
        ALTER TABLE ledger."Tables" ALTER COLUMN "Columns" SET NOT NULL;
        DROP FUNCTION IF EXISTS ledger.row_hash(jsonb, bytea);

        CREATE OR REPLACE FUNCTION ledger.row_hash(row_json jsonb, prev bytea, cols text[]) RETURNS bytea LANGUAGE sql IMMUTABLE AS $$
            SELECT sha256(convert_to(encode(prev, 'hex') || '|' || coalesce((SELECT jsonb_object_agg(c, row_json -> c) FROM unnest(cols) AS c), '{}'::jsonb)::text, 'UTF8'))
        $$;

        -- enable_sort = off (MDV-P0-039): with statistics from a small table the planner reads the head by sorting every tenant row, so a
        -- bulk insert turns quadratic; without a sort the only plan left is the (TenantId, ChainSeq) index, backwards, one row.
        CREATE OR REPLACE FUNCTION ledger.chain() RETURNS trigger LANGUAGE plpgsql SET timezone = 'UTC' SET enable_sort = off AS $$
        DECLARE head_seq bigint; head_hash bytea; cols text[];
        BEGIN
            SELECT "Columns" INTO cols FROM ledger."Tables" WHERE "Name" = TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME;
            IF cols IS NULL THEN RAISE EXCEPTION '%.% is not a ledger table', TG_TABLE_SCHEMA, TG_TABLE_NAME; END IF;
            PERFORM pg_advisory_xact_lock(hashtextextended(TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME || ':' || NEW."TenantId"::text, 0));
            EXECUTE format('SELECT "ChainSeq", "RowHash" FROM %I.%I WHERE "TenantId" = $1 ORDER BY "ChainSeq" DESC LIMIT 1', TG_TABLE_SCHEMA, TG_TABLE_NAME)
                INTO head_seq, head_hash USING NEW."TenantId";
            NEW."ChainSeq" := coalesce(head_seq, 0) + 1;
            NEW."PrevHash" := coalesce(head_hash, {{Zero}});
            NEW."RowHash" := ledger.row_hash(to_jsonb(NEW), NEW."PrevHash", cols);
            IF head_seq IS NULL THEN
                INSERT INTO ledger."Chains" VALUES (TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME, NEW."TenantId") ON CONFLICT DO NOTHING;
            END IF;
            RETURN NEW;
        END $$;

        -- A name is a ledger table only when it is listed AND that table carries the chain trigger; anything else is ignored or refused.
        CREATE OR REPLACE FUNCTION ledger.is_ledger(tbl text) RETURNS boolean LANGUAGE sql STABLE AS $$
            SELECT EXISTS (SELECT 1 FROM ledger."Tables" t JOIN pg_trigger g ON g.tgrelid = to_regclass(format('%I.%I', split_part(t."Name", '.', 1), split_part(t."Name", '.', 2)))
                           WHERE t."Name" = tbl AND g.tgfoid = 'ledger.chain'::regproc)
        $$;

        CREATE OR REPLACE FUNCTION ledger.refuse() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION '% is append only: % is refused', TG_TABLE_NAME, TG_OP USING ERRCODE = 'restrict_violation';
        END $$;

        -- Id of the first row whose sequence, link or hash does not match, or NULL when the chain is intact.
        CREATE OR REPLACE FUNCTION ledger.verify_chain(tbl text, tenant uuid) RETURNS uuid LANGUAGE plpgsql SET timezone = 'UTC' AS $$
        DECLARE r record; prev bytea := {{Zero}}; n bigint := 0; cols text[];
        BEGIN
            IF NOT ledger.is_ledger(tbl) THEN RAISE EXCEPTION 'not a ledger table'; END IF;
            SELECT "Columns" INTO cols FROM ledger."Tables" WHERE "Name" = tbl;
            FOR r IN EXECUTE format('SELECT t."Id", t."ChainSeq", t."PrevHash", t."RowHash", ledger.row_hash(to_jsonb(t), t."PrevHash", $2) AS calc FROM %I.%I t WHERE t."TenantId" = $1 ORDER BY t."ChainSeq"',
                                    split_part(tbl, '.', 1), split_part(tbl, '.', 2)) USING tenant, cols LOOP
                n := n + 1;
                IF r."ChainSeq" <> n OR r."PrevHash" <> prev OR r.calc <> r."RowHash" THEN RETURN r."Id"; END IF;
                prev := r."RowHash";
            END LOOP;
            RETURN NULL;
        END $$;

        -- The head (last sequence and hash) of every chain the session can see (row-level security: the session's tenant), for the daily digest.
        CREATE OR REPLACE FUNCTION ledger.heads() RETURNS TABLE (tbl text, tenant uuid, seq bigint, hash bytea) LANGUAGE plpgsql AS $$
        DECLARE t text;
        BEGIN
            FOR t IN SELECT "Name" FROM ledger."Tables" WHERE ledger.is_ledger("Name") ORDER BY 1 LOOP
                RETURN QUERY EXECUTE format('SELECT %L::text, "TenantId", max("ChainSeq"), (array_agg("RowHash" ORDER BY "ChainSeq" DESC))[1] FROM %I.%I GROUP BY "TenantId"',
                                            t, split_part(t, '.', 1), split_part(t, '.', 2));
            END LOOP;
        END $$;

        -- The stored hash at one position of a chain (NULL when the row is gone), to compare with a digest.
        CREATE OR REPLACE FUNCTION ledger.hash_at(tbl text, tenant uuid, seq bigint) RETURNS bytea LANGUAGE plpgsql AS $$
        DECLARE h bytea;
        BEGIN
            IF NOT ledger.is_ledger(tbl) THEN RAISE EXCEPTION 'not a ledger table'; END IF;
            EXECUTE format('SELECT "RowHash" FROM %I.%I WHERE "TenantId" = $1 AND "ChainSeq" = $2', split_part(tbl, '.', 1), split_part(tbl, '.', 2)) INTO h USING tenant, seq;
            RETURN h;
        END $$;
        """;

    /// <summary>(Re)installs the shared ledger tables and functions, idempotently; <see cref="AddLedger"/> does it on the way.</summary>
    public static MigrationBuilder InstallLedger(this MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Install);
        return migrationBuilder;
    }

    /// <summary>Makes <paramref name="schema"/>.<paramref name="table"/> a hash-chained, append-only table. The shared functions are (re)installed on the way, idempotently.</summary>
    public static MigrationBuilder AddLedger(this MigrationBuilder migrationBuilder, string schema, string table)
    {
        migrationBuilder.InstallLedger();
        migrationBuilder.Sql($"""
            ALTER TABLE {schema}."{table}" ADD COLUMN "ChainSeq" bigint NOT NULL, ADD COLUMN "PrevHash" bytea NOT NULL, ADD COLUMN "RowHash" bytea NOT NULL;
            CREATE UNIQUE INDEX "UX_{table}_Chain" ON {schema}."{table}" ("TenantId", "ChainSeq");
            CREATE TRIGGER "TR_{table}_Chain" BEFORE INSERT ON {schema}."{table}" FOR EACH ROW EXECUTE FUNCTION ledger.chain();
            CREATE TRIGGER "TR_{table}_AppendOnly" BEFORE UPDATE OR DELETE ON {schema}."{table}" FOR EACH ROW EXECUTE FUNCTION ledger.refuse();
            CREATE TRIGGER "TR_{table}_NoTruncate" BEFORE TRUNCATE ON {schema}."{table}" FOR EACH STATEMENT EXECUTE FUNCTION ledger.refuse();
            ALTER TABLE {schema}."{table}" ENABLE ALWAYS TRIGGER "TR_{table}_Chain", ENABLE ALWAYS TRIGGER "TR_{table}_AppendOnly", ENABLE ALWAYS TRIGGER "TR_{table}_NoTruncate";
            INSERT INTO ledger."Tables" ("Name", "Columns") VALUES ('{schema}.{table}', ledger.columns_of('{schema}.{table}'));
            """);
        return migrationBuilder;
    }
}

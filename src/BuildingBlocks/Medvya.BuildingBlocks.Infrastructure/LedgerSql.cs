using Microsoft.EntityFrameworkCore.Migrations;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>
/// Tamper-evident append-only tables without SQL Server ledger (ADR-012, MDV-P0-036). A ledgered table has "Id" uuid and "TenantId" uuid;
/// <c>migrationBuilder.AddLedger("schema", "Table")</c> is the whole opt-in (one line in a forward-only migration).
///
/// Canonical encoding, defined once in ledger.row_hash: SHA-256 over UTF-8 of
///   hex(PrevHash) || '|' || (to_jsonb(row) minus "PrevHash" and "RowHash")::text
/// jsonb text has a fixed key order and the functions pin TimeZone to UTC, so every session produces the same bytes. The row
/// includes its own "ChainSeq" (1, 2, 3 ... per tenant and table), so reordering or removing a middle row breaks the chain.
/// PrevHash of the first row is 32 zero bytes.
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

        CREATE OR REPLACE FUNCTION ledger.row_hash(row_json jsonb, prev bytea) RETURNS bytea LANGUAGE sql IMMUTABLE AS $$
            SELECT sha256(convert_to(encode(prev, 'hex') || '|' || (row_json - 'PrevHash' - 'RowHash')::text, 'UTF8'))
        $$;

        CREATE OR REPLACE FUNCTION ledger.chain() RETURNS trigger LANGUAGE plpgsql SET timezone = 'UTC' AS $$
        DECLARE head_seq bigint; head_hash bytea;
        BEGIN
            PERFORM pg_advisory_xact_lock(hashtextextended(TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME || ':' || NEW."TenantId"::text, 0));
            EXECUTE format('SELECT "ChainSeq", "RowHash" FROM %I.%I WHERE "TenantId" = $1 ORDER BY "ChainSeq" DESC LIMIT 1', TG_TABLE_SCHEMA, TG_TABLE_NAME)
                INTO head_seq, head_hash USING NEW."TenantId";
            NEW."ChainSeq" := coalesce(head_seq, 0) + 1;
            NEW."PrevHash" := coalesce(head_hash, {{Zero}});
            NEW."RowHash" := ledger.row_hash(to_jsonb(NEW), NEW."PrevHash");
            IF head_seq IS NULL THEN
                INSERT INTO ledger."Chains" VALUES (TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME, NEW."TenantId") ON CONFLICT DO NOTHING;
            END IF;
            RETURN NEW;
        END $$;

        CREATE OR REPLACE FUNCTION ledger.refuse() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION '% is append only: % is refused', TG_TABLE_NAME, TG_OP USING ERRCODE = 'restrict_violation';
        END $$;

        -- Id of the first row whose sequence, link or hash does not match, or NULL when the chain is intact.
        CREATE OR REPLACE FUNCTION ledger.verify_chain(tbl text, tenant uuid) RETURNS uuid LANGUAGE plpgsql SET timezone = 'UTC' AS $$
        DECLARE r record; prev bytea := {{Zero}}; n bigint := 0;
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM ledger."Tables" WHERE "Name" = tbl) THEN RAISE EXCEPTION 'not a ledger table'; END IF;
            FOR r IN EXECUTE format('SELECT t."Id", t."ChainSeq", t."PrevHash", t."RowHash", ledger.row_hash(to_jsonb(t), t."PrevHash") AS calc FROM %I.%I t WHERE t."TenantId" = $1 ORDER BY t."ChainSeq"',
                                    split_part(tbl, '.', 1), split_part(tbl, '.', 2)) USING tenant LOOP
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
            FOR t IN SELECT "Name" FROM ledger."Tables" ORDER BY 1 LOOP
                RETURN QUERY EXECUTE format('SELECT %L::text, "TenantId", max("ChainSeq"), (array_agg("RowHash" ORDER BY "ChainSeq" DESC))[1] FROM %I.%I GROUP BY "TenantId"',
                                            t, split_part(t, '.', 1), split_part(t, '.', 2));
            END LOOP;
        END $$;

        -- The stored hash at one position of a chain (NULL when the row is gone), to compare with a digest.
        CREATE OR REPLACE FUNCTION ledger.hash_at(tbl text, tenant uuid, seq bigint) RETURNS bytea LANGUAGE plpgsql AS $$
        DECLARE h bytea;
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM ledger."Tables" WHERE "Name" = tbl) THEN RAISE EXCEPTION 'not a ledger table'; END IF;
            EXECUTE format('SELECT "RowHash" FROM %I.%I WHERE "TenantId" = $1 AND "ChainSeq" = $2', split_part(tbl, '.', 1), split_part(tbl, '.', 2)) INTO h USING tenant, seq;
            RETURN h;
        END $$;
        """;

    /// <summary>Makes <paramref name="schema"/>.<paramref name="table"/> a hash-chained, append-only table. The shared functions are (re)installed on the way, idempotently.</summary>
    public static MigrationBuilder AddLedger(this MigrationBuilder migrationBuilder, string schema, string table)
    {
        migrationBuilder.Sql(Install);
        migrationBuilder.Sql($"""
            ALTER TABLE {schema}."{table}" ADD COLUMN "ChainSeq" bigint NOT NULL, ADD COLUMN "PrevHash" bytea NOT NULL, ADD COLUMN "RowHash" bytea NOT NULL;
            CREATE UNIQUE INDEX "UX_{table}_Chain" ON {schema}."{table}" ("TenantId", "ChainSeq");
            CREATE TRIGGER "TR_{table}_Chain" BEFORE INSERT ON {schema}."{table}" FOR EACH ROW EXECUTE FUNCTION ledger.chain();
            CREATE TRIGGER "TR_{table}_AppendOnly" BEFORE UPDATE OR DELETE ON {schema}."{table}" FOR EACH ROW EXECUTE FUNCTION ledger.refuse();
            CREATE TRIGGER "TR_{table}_NoTruncate" BEFORE TRUNCATE ON {schema}."{table}" FOR EACH STATEMENT EXECUTE FUNCTION ledger.refuse();
            ALTER TABLE {schema}."{table}" ENABLE ALWAYS TRIGGER "TR_{table}_Chain", ENABLE ALWAYS TRIGGER "TR_{table}_AppendOnly", ENABLE ALWAYS TRIGGER "TR_{table}_NoTruncate";
            INSERT INTO ledger."Tables" VALUES ('{schema}.{table}');
            """);
        return migrationBuilder;
    }
}

-- Medvya database roles (MDV-P0-008, ported to PostgreSQL in MDV-P0-034). Single source of truth for who may do what.
-- Run by the database administrator (a superuser locally; the Azure PostgreSQL administrator in the cloud) BEFORE migrations, and
-- again whenever a module is added. Idempotent: safe to re-run. None of the roles below can log in; a login is created IN ROLE one
-- of them (docs/03-architecture/database-security.md).
-- Rights on single tables that are narrower than their schema's (DataKeys, the ids-only lists, the ledger, what reporting may read) live in
-- table-rights.sql, which db-migrate applies after this script and again after every migration run, once the tables exist.
--
--   medvya_owner      Owns every schema and table, so it can run DDL and keeps the owner's data rights (foreign-key checks run as the
--                     owner and need them). Nobody logs in as it.
--   medvya_migrator   The deploy login. Holds NO rights on module data. db-migrate does SET ROLE medvya_owner (a membership without
--                     INHERIT) for the length of a migration; a plain session as the migrator can create objects but read nothing.
--   medvya_app        The running application = membership of the module roles. Not an owner: no DDL.
--   medvya_reporting  Read-only: tenancy and the masked views in schema reporting. Row-level security still applies: without a tenant
--                     setting it sees no clinic rows.
--   medvya_<module>   Rights on that module's own schema only.
--
-- Every role is NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE, so FORCE ROW LEVEL SECURITY binds all of them, owner included.
-- Adding a module: add its schema to the schema list and a row to module_grants.

BEGIN;

DO $$
DECLARE r text;
BEGIN
    FOREACH r IN ARRAY ARRAY['medvya_owner', 'medvya_migrator', 'medvya_app', 'medvya_reporting', 'medvya_tenancy', 'medvya_audit',
                             'medvya_identity', 'medvya_patients', 'medvya_billing', 'medvya_scheduling', 'medvya_leads',
                             'medvya_notifications', 'medvya_encounters', 'medvya_jobs', 'medvya_platform']
    LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
            EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION', r);
        END IF;
    END LOOP;
    -- Checked, not ALTERed: on Azure the administrator is not a superuser, and PostgreSQL 16 lets only a superuser touch these attributes
    -- (even to switch them off). A role that has gained one is a security fault for a person to look at, so the script stops.
    SELECT string_agg(rolname, ', ') INTO r FROM pg_roles
    WHERE rolname LIKE 'medvya\_%' AND (rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication);
    IF r IS NOT NULL THEN RAISE EXCEPTION 'Medvya roles must not be superuser, bypass RLS, create databases or roles, or replicate: %', r; END IF;
END $$;

-- The administrator hands schemas to medvya_owner, which PostgreSQL 16 allows only to a role that can SET ROLE to it. A superuser can; the
-- Azure administrator is not one, but it created the role and holds ADMIN on it, so it grants itself SET (no INHERIT: no data rights).
DO $$
BEGIN
    IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname = current_user) THEN
        EXECUTE format('GRANT medvya_owner TO %I WITH INHERIT FALSE, SET TRUE', current_user);
    END IF;
END $$;

-- SET only: the migrator can become the owner on purpose, but never holds the owner's rights by default.
GRANT medvya_owner TO medvya_migrator WITH INHERIT FALSE, SET TRUE;

-- Schemas exist before migrations so rights can be granted on them; EF's EnsureSchema then finds them and skips.
-- The migrator may also create objects in them directly (it owns what it creates, which the owner can then drop).
DO $$
DECLARE s text;
BEGIN
    FOREACH s IN ARRAY ARRAY['tenancy', 'audit', 'identity', 'patients', 'billing', 'scheduling', 'leads', 'notifications', 'encounters', 'hangfire', 'ledger', 'reporting', 'platform']
    LOOP
        EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I', s);
        EXECUTE format('ALTER SCHEMA %I OWNER TO medvya_owner', s);
    END LOOP;
END $$;

-- public holds only __EFMigrationsHistory; nobody else may create there.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO medvya_owner;

-- Role memberships need ADMIN on the module roles, which the administrator holds and the owner does not.
-- medvya_platform is NOT here: the platform console (Host.Platform, MDV-P5-040) signs in with its own login IN ROLE medvya_platform alone,
-- so it cannot read any clinic schema, and the clinic application cannot read the console's tables.
DO $$
DECLARE r text;
BEGIN
    FOREACH r IN ARRAY ARRAY['medvya_tenancy', 'medvya_audit', 'medvya_identity', 'medvya_patients', 'medvya_billing', 'medvya_scheduling',
                             'medvya_leads', 'medvya_notifications', 'medvya_encounters', 'medvya_jobs']
    LOOP
        EXECUTE format('GRANT %I TO medvya_app', r);
    END LOOP;
END $$;

-- Everything below is a right on objects medvya_owner owns, so it is granted as the owner: on Azure the administrator is not a superuser and
-- no longer owns the schemas it just handed over.
SET ROLE medvya_owner;

DO $$
DECLARE s text;
BEGIN
    FOREACH s IN ARRAY ARRAY['tenancy', 'audit', 'identity', 'patients', 'billing', 'scheduling', 'leads', 'notifications', 'encounters', 'hangfire', 'ledger', 'reporting', 'platform']
    LOOP
        EXECUTE format('GRANT USAGE, CREATE ON SCHEMA %I TO medvya_migrator', s);
    END LOOP;
END $$;

-- One row per module: its role gets USAGE on its schema and the listed table rights on everything the owner creates there
-- (and on what exists already), plus sequences, and joins medvya_app.
CREATE TEMP TABLE module_grants (schema_name text, role_name text, table_rights text) ON COMMIT DROP;
INSERT INTO module_grants VALUES
    -- Tenancy: row access to its own schema (RLS still limits rows to the session's tenant).
    ('tenancy',       'medvya_tenancy',       'SELECT, INSERT, UPDATE, DELETE'),
    -- Audit: append and read only. The append-only trigger refuses UPDATE/DELETE for every role as well (MDV-P0-036); this refuses them first.
    ('audit',         'medvya_audit',         'SELECT, INSERT'),
    -- Identity: staff accounts and sessions. Never deleted (offboarding is a membership status; a session is revoked, not removed).
    ('identity',      'medvya_identity',      'SELECT, INSERT, UPDATE'),
    ('patients',      'medvya_patients',      'SELECT, INSERT, UPDATE, DELETE'),
    -- Billing: no DELETE, because deleting a DocSequences row would restart a legal number series. Only an owner can alter its trigger.
    ('billing',       'medvya_billing',       'SELECT, INSERT, UPDATE'),
    -- Scheduling: not in reporting, because appointments hold the free-text visit reason (SensitiveHealth).
    ('scheduling',    'medvya_scheduling',    'SELECT, INSERT, UPDATE, DELETE'),
    -- Leads: demo requests from the public site. Add and read only; corrections and deletion are an administrator's job.
    ('leads',         'medvya_leads',         'SELECT, INSERT'),
    -- Notifications: messages and attempts; no DELETE (attempts are the delivery history). No access to patients or scheduling.
    ('notifications', 'medvya_notifications', 'SELECT, INSERT, UPDATE'),
    -- Encounters: clinical records are never deleted (an ended consultation stays; corrections are addenda). Not in reporting.
    ('encounters',    'medvya_encounters',    'SELECT, INSERT, UPDATE'),
    -- Hangfire job storage in its own schema, no module data. Its tables are created by the owner on deploy (the app may not).
    -- shortcut: a role, not yet a separate database login; give the Worker its own identity when Worker.Jobs becomes a separate host.
    ('hangfire',      'medvya_jobs',          'SELECT, INSERT, UPDATE, DELETE'),
    -- Platform console (MDV-P5-040): the Medvya team, keys, links, sessions and its audit log. Nothing is deleted (members are removed by
    -- date, keys revoked); its audit log is insert and read only (table-rights.sql), and its trigger refuses changes for every role.
    ('platform',      'medvya_platform',      'SELECT, INSERT, UPDATE');

DO $$
DECLARE g record;
BEGIN
    FOR g IN SELECT * FROM module_grants LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', g.schema_name, g.role_name);
        EXECUTE format('GRANT %s ON ALL TABLES IN SCHEMA %I TO %I', g.table_rights, g.schema_name, g.role_name);
        EXECUTE format('GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA %I TO %I', g.schema_name, g.role_name);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE medvya_owner IN SCHEMA %I GRANT %s ON TABLES TO %I', g.schema_name, g.table_rights, g.role_name);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE medvya_owner IN SCHEMA %I GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO %I', g.schema_name, g.role_name);
    END LOOP;
END $$;

-- Ledger (MDV-P0-036): the shared hash-chain functions and their ids-only bookkeeping (ledger."Tables", ledger."Chains"). Every role that
-- inserts into a hash-chained table runs the chain trigger, which reads the table's column list and records a new chain; audit also runs
-- the daily digest and verification. Read only here: the one INSERT right (on "Chains") is in table-rights.sql, so "Tables" is written by
-- migrations alone (MDV-P0-041). No UPDATE or DELETE: a chain is never unlisted. Add a module role here when its first table is ledgered.
DO $$
DECLARE r text;
BEGIN
    FOREACH r IN ARRAY ARRAY['medvya_tenancy', 'medvya_patients', 'medvya_audit']
    LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA ledger TO %I', r);
        EXECUTE format('REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON ALL TABLES IN SCHEMA ledger FROM %I', r);
        EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA ledger TO %I', r);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE medvya_owner IN SCHEMA ledger REVOKE INSERT ON TABLES FROM %I', r);
        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE medvya_owner IN SCHEMA ledger GRANT SELECT ON TABLES TO %I', r);
    END LOOP;
END $$;

-- Reporting reads the tenancy tables listed in table-rights.sql (no personal data; still inside RLS) and the masked views in schema reporting
-- (MDV-P0-037: initials, age band, identifier scheme and length; never a name, date of birth, contact, address or identifier value). It has
-- no right on patients, audit, identity or scheduling tables, so a base-table query is denied. The views are owned by medvya_owner, and FORCE
-- ROW LEVEL SECURITY on the base tables binds the owner, so a reporting session still sees only its tenant. A new tenancy table is not
-- readable until it is added to the list (MDV-P0-041: no schema-wide or default right on tenancy).
GRANT USAGE ON SCHEMA tenancy, reporting TO medvya_reporting;
ALTER DEFAULT PRIVILEGES FOR ROLE medvya_owner IN SCHEMA tenancy REVOKE SELECT ON TABLES FROM medvya_reporting;
GRANT SELECT ON ALL TABLES IN SCHEMA reporting TO medvya_reporting;
ALTER DEFAULT PRIVILEGES FOR ROLE medvya_owner IN SCHEMA reporting GRANT SELECT ON TABLES TO medvya_reporting;
-- shortcut: audit, identity and scheduling have no masked views yet (audit ClientIp, staff contacts, the visit reason), so reporting cannot
-- read them at all; add a view in schema reporting when a report needs one.

RESET ROLE;
COMMIT;

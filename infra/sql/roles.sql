-- Medvya database roles (MDV-P0-008). Single source of truth for who may do what inside a Medvya database.
-- Run by the database administrator (the Entra admin group in Azure; sa locally) BEFORE migrations, and again
-- whenever a module is added. Idempotent: safe to re-run. The migration principal cannot run it (it cannot grant
-- rights to itself). Identities are mapped to these roles separately: docs/03-architecture/database-security.md.
--
--   medvya_migrator   DDL only: creates and alters objects; reads/writes no module data, only the EF migrations history.
--   medvya_app        the running application = the sum of the module roles. No DDL.
--   medvya_reporting  read-only. Row-level security still applies: without a tenant SESSION_CONTEXT it sees no clinic rows.
--   medvya_<module>   rights on that module's own schema only.
--
-- Adding a module: create its schema and role below, grant only ON SCHEMA::<its schema>, add the role to medvya_app.
-- RLS predicate functions (security.fn_tenant_access, audit.fn_tenant_access) need no grant: SQL Server evaluates
-- security predicates without checking the querying user's EXECUTE/SELECT rights (proven by DatabaseRoleTests).

-- Database settings are the administrator's job, so the migration role needs no ALTER DATABASE.
-- Ledger verification (sp_verify_database_ledger) needs snapshot isolation; Azure SQL has it on, SQL Server does not.
IF (SELECT snapshot_isolation_state FROM sys.databases WHERE database_id = DB_ID()) = 0
    ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Schemas exist before migrations so rights can be granted on them; EF's EnsureSchema then finds them and skips.
IF SCHEMA_ID(N'tenancy') IS NULL EXEC(N'CREATE SCHEMA tenancy AUTHORIZATION dbo');
IF SCHEMA_ID(N'security') IS NULL EXEC(N'CREATE SCHEMA security AUTHORIZATION dbo');
IF SCHEMA_ID(N'audit') IS NULL EXEC(N'CREATE SCHEMA audit AUTHORIZATION dbo');
IF SCHEMA_ID(N'identity') IS NULL EXEC(N'CREATE SCHEMA [identity] AUTHORIZATION dbo');

IF DATABASE_PRINCIPAL_ID(N'medvya_migrator') IS NULL CREATE ROLE medvya_migrator AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_app') IS NULL CREATE ROLE medvya_app AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_reporting') IS NULL CREATE ROLE medvya_reporting AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_tenancy') IS NULL CREATE ROLE medvya_tenancy AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_audit') IS NULL CREATE ROLE medvya_audit AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_identity') IS NULL CREATE ROLE medvya_identity AUTHORIZATION dbo;

-- Migration principal: schema objects, security policies and ledger tables. No rights on module data.
GRANT CREATE TABLE, CREATE VIEW, CREATE FUNCTION, CREATE PROCEDURE, CREATE TYPE, ALTER ANY SCHEMA, REFERENCES,
      ALTER ANY SECURITY POLICY, ENABLE LEDGER TO medvya_migrator;
GRANT SELECT, INSERT, DELETE ON SCHEMA::dbo TO medvya_migrator; -- dbo holds only __EFMigrationsHistory
-- CREATE SECURITY POLICY needs SELECT on the predicate function. security holds only predicate functions.
GRANT SELECT ON SCHEMA::security TO medvya_migrator;
-- shortcut: audit's predicate function lives in the audit schema (InitialAudit), so the migrator can SELECT audit rows;
-- RLS still returns none without a tenant SESSION_CONTEXT. New modules put predicates in `security`; revoke this if audit's moves.
GRANT SELECT ON SCHEMA::audit TO medvya_migrator;

-- Tenancy module: row access to its own schema (RLS still limits rows to the session's tenant).
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::tenancy TO medvya_tenancy;

-- Audit module: append and read only. The ledger table refuses UPDATE/DELETE too; this refuses them first, by permission.
GRANT SELECT, INSERT ON SCHEMA::audit TO medvya_audit;

-- Identity module: staff accounts. Never deleted (offboarding is a membership status), so no DELETE.
GRANT SELECT, INSERT, UPDATE ON SCHEMA::[identity] TO medvya_identity;

ALTER ROLE medvya_tenancy ADD MEMBER medvya_app;
ALTER ROLE medvya_audit ADD MEMBER medvya_app;
ALTER ROLE medvya_identity ADD MEMBER medvya_app;

-- shortcut: reporting reads tenancy only; audit (ClientIp is Personal) is added once dynamic data masking lands
-- with the Patients module (docs/03-architecture/data-classification.md).
GRANT SELECT ON SCHEMA::tenancy TO medvya_reporting;

COMMIT;

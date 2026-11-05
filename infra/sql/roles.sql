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
-- RLS predicate functions (security.fn_*, audit.fn_tenant_access) need no grant: SQL Server evaluates
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
IF SCHEMA_ID(N'patients') IS NULL EXEC(N'CREATE SCHEMA patients AUTHORIZATION dbo');
IF SCHEMA_ID(N'billing') IS NULL EXEC(N'CREATE SCHEMA billing AUTHORIZATION dbo');
IF SCHEMA_ID(N'scheduling') IS NULL EXEC(N'CREATE SCHEMA scheduling AUTHORIZATION dbo');
IF SCHEMA_ID(N'leads') IS NULL EXEC(N'CREATE SCHEMA leads AUTHORIZATION dbo');
IF SCHEMA_ID(N'notifications') IS NULL EXEC(N'CREATE SCHEMA notifications AUTHORIZATION dbo');
IF SCHEMA_ID(N'hangfire') IS NULL EXEC(N'CREATE SCHEMA hangfire AUTHORIZATION dbo');

IF DATABASE_PRINCIPAL_ID(N'medvya_migrator') IS NULL CREATE ROLE medvya_migrator AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_app') IS NULL CREATE ROLE medvya_app AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_reporting') IS NULL CREATE ROLE medvya_reporting AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_tenancy') IS NULL CREATE ROLE medvya_tenancy AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_audit') IS NULL CREATE ROLE medvya_audit AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_identity') IS NULL CREATE ROLE medvya_identity AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_patients') IS NULL CREATE ROLE medvya_patients AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_billing') IS NULL CREATE ROLE medvya_billing AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_scheduling') IS NULL CREATE ROLE medvya_scheduling AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_leads') IS NULL CREATE ROLE medvya_leads AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_notifications') IS NULL CREATE ROLE medvya_notifications AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'medvya_jobs') IS NULL CREATE ROLE medvya_jobs AUTHORIZATION dbo;

-- Migration principal: schema objects, security policies and ledger tables. No rights on module data.
GRANT CREATE TABLE, CREATE VIEW, CREATE FUNCTION, CREATE PROCEDURE, CREATE TYPE, ALTER ANY SCHEMA, REFERENCES,
      ALTER ANY SECURITY POLICY, ENABLE LEDGER, ALTER ANY MASK TO medvya_migrator;
GRANT SELECT, INSERT, DELETE ON SCHEMA::dbo TO medvya_migrator; -- dbo holds only __EFMigrationsHistory
-- CREATE SECURITY POLICY needs SELECT on the predicate function. security holds only predicate functions.
GRANT SELECT ON SCHEMA::security TO medvya_migrator;
-- CREATE TABLE ... ENCRYPTED WITH needs to see the key metadata (never the keys). SQL Server grants this to public; Azure SQL does not
-- (deploy run 41962207363 failed with 15247 on patients.Identifiers), so grant it explicitly.
GRANT VIEW ANY COLUMN MASTER KEY DEFINITION, VIEW ANY COLUMN ENCRYPTION KEY DEFINITION TO medvya_migrator;
-- shortcut: audit's predicate function lives in the audit schema (InitialAudit), so the migrator can SELECT audit rows;
-- RLS still returns none without a tenant SESSION_CONTEXT. New modules put predicates in `security`; revoke this if audit's moves.
GRANT SELECT ON SCHEMA::audit TO medvya_migrator;

-- Tenancy module: row access to its own schema (RLS still limits rows to the session's tenant).
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::tenancy TO medvya_tenancy;

-- Audit module: append and read only. The ledger table refuses UPDATE/DELETE too; this refuses them first, by permission.
GRANT SELECT, INSERT ON SCHEMA::audit TO medvya_audit;

-- Identity module: staff accounts and sessions. Never deleted (offboarding is a membership status; a session is
-- revoked, not removed), so no DELETE.
GRANT SELECT, INSERT, UPDATE ON SCHEMA::[identity] TO medvya_identity;

-- Patients module (MDV-P1-013, MDV-P0-009): row access plus UNMASK on its own schema only, so the app reads clear demographics
-- while every role without UNMASK sees the dynamic data masks. Always Encrypted values still need the column master key,
-- which no database role can grant. Reading key metadata lets SqlClient encrypt parameters (sp_describe_parameter_encryption).
GRANT SELECT, INSERT, UPDATE, DELETE, UNMASK ON SCHEMA::patients TO medvya_patients;
GRANT VIEW ANY COLUMN MASTER KEY DEFINITION, VIEW ANY COLUMN ENCRYPTION KEY DEFINITION TO medvya_patients;

-- Billing module (MDV-P4-001): no DELETE, because deleting a DOC_SEQUENCE row would restart a legal number series.
GRANT SELECT, INSERT, UPDATE ON SCHEMA::billing TO medvya_billing;
-- Scheduling module (MDV-P1-011): templates, resources and, later, appointments. Not in reporting: appointments will hold the
-- free-text visit reason (SensitiveHealth); add a masked or reason-free view for reporting when the no-show metric needs it.
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::scheduling TO medvya_scheduling;
-- Leads module (MDV-P1-003): demo requests from the public site. Add and read only; corrections and deletion are an administrator's job.
GRANT SELECT, INSERT ON SCHEMA::leads TO medvya_leads;

-- Notifications module (MDV-P1-032): messages and their attempts, no DELETE (attempts are the delivery history). No access to patients or
-- scheduling: contacts and events come through the module contracts.
GRANT SELECT, INSERT, UPDATE ON SCHEMA::notifications TO medvya_notifications;
-- Hangfire (MDV-P1-032): job storage in its own schema, no access to any module's data. Hangfire creates its tables itself on first
-- start, which needs more than this role has: the deploy step runs that install as the migration principal (MDV-P0-007); only
-- Development lets the app prepare the schema.
-- shortcut: a role, not yet a separate database user; give the Worker its own identity when Worker.Jobs becomes a separate host.
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::hangfire TO medvya_jobs;
-- Hangfire's installer reads and writes its own version table (hangfire.Schema); job storage holds no module data.
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::hangfire TO medvya_migrator;

ALTER ROLE medvya_tenancy ADD MEMBER medvya_app;
ALTER ROLE medvya_audit ADD MEMBER medvya_app;
ALTER ROLE medvya_identity ADD MEMBER medvya_app;
ALTER ROLE medvya_patients ADD MEMBER medvya_app;
ALTER ROLE medvya_billing ADD MEMBER medvya_app;
ALTER ROLE medvya_scheduling ADD MEMBER medvya_app;
ALTER ROLE medvya_leads ADD MEMBER medvya_app;
ALTER ROLE medvya_notifications ADD MEMBER medvya_app;
ALTER ROLE medvya_jobs ADD MEMBER medvya_app;

-- Reporting and support read masked patient demographics and ciphertext identifiers (no UNMASK, no column master key).
-- shortcut: audit stays out of reporting until AUDIT_EVENT.ClientIp is masked on the ledger table; add it with that migration.
GRANT SELECT ON SCHEMA::tenancy TO medvya_reporting;
GRANT SELECT ON SCHEMA::patients TO medvya_reporting;

COMMIT;

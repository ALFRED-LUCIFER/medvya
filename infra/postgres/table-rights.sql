-- Rights on single tables that are narrower than the schema-wide grants in roles.sql (MDV-P0-041). db-migrate applies this file twice:
-- right after roles.sql (which re-grants schema-wide rights) and at the end of every migration run, both times as medvya_owner (once the
-- tables exist). Idempotent; a table that does not exist yet is skipped and picked up by the next run.

DO $$
BEGIN
    -- Wrapped tenant data keys: deleting a row makes every identifier of that tenant unreadable. Insert the first key, update only the
    -- wrapping (the re-wrap path: WrapKeyId and Wrapped), never delete or re-key a row to another tenant or purpose.
    IF to_regclass('patients."DataKeys"') IS NOT NULL THEN
        REVOKE UPDATE, DELETE, TRUNCATE ON patients."DataKeys" FROM medvya_patients;
        GRANT UPDATE ("WrapKeyId", "Wrapped") ON patients."DataKeys" TO medvya_patients;
    END IF;

    -- Ids-only lists outside row-level security, kept by insert triggers: a removed or changed id hides a tenant from the job that reads it.
    IF to_regclass('tenancy."TenantIndex"') IS NOT NULL THEN
        REVOKE UPDATE, DELETE, TRUNCATE ON tenancy."TenantIndex" FROM medvya_tenancy;
    END IF;
    IF to_regclass('scheduling."OutboxTenants"') IS NOT NULL THEN
        REVOKE UPDATE, DELETE, TRUNCATE ON scheduling."OutboxTenants" FROM medvya_scheduling;
    END IF;

    -- The platform audit log is append-only (MDV-P5-040): read and add, never change.
    IF to_regclass('platform."AuditEntries"') IS NOT NULL THEN
        REVOKE UPDATE, DELETE, TRUNCATE ON platform."AuditEntries" FROM medvya_platform;
    END IF;

    -- The chain trigger lists a tenant's first row in ledger."Chains"; ledger."Tables" stays read-only (roles.sql).
    IF to_regclass('ledger."Chains"') IS NOT NULL THEN
        GRANT INSERT ON ledger."Chains" TO medvya_tenancy, medvya_patients, medvya_audit, medvya_encounters;
    END IF;
    -- The digest job records each published digest (MDV-P0-042); append-only (its triggers refuse UPDATE/DELETE for every role too).
    IF to_regclass('ledger."Digests"') IS NOT NULL THEN
        GRANT INSERT ON ledger."Digests" TO medvya_audit;
    END IF;
END $$;

-- DELETE only on the tables the application deletes from (#879): replaced opening hours, a removed closure, a replaced payer or
-- emergency contact, freed slots, released holds and removed schedule exceptions. Everything else (patients, memberships, settings
-- history, access reviews, the MRN counters, visit history) is never deleted, so a buggy or injected delete cannot remove it either.
-- Revoked schema-wide first, so a new table (granted DELETE by its schema's default privilege) is covered from its first run.
REVOKE DELETE, TRUNCATE ON ALL TABLES IN SCHEMA tenancy FROM medvya_tenancy;
REVOKE DELETE, TRUNCATE ON ALL TABLES IN SCHEMA patients FROM medvya_patients;
REVOKE DELETE, TRUNCATE ON ALL TABLES IN SCHEMA scheduling FROM medvya_scheduling;
DO $$
DECLARE t text[];
BEGIN
    FOREACH t SLICE 1 IN ARRAY ARRAY[['tenancy', 'OperatingHours', 'medvya_tenancy'], ['tenancy', 'Closures', 'medvya_tenancy'],
                                    ['patients', 'PayerCoverages', 'medvya_patients'], ['patients', 'Relationships', 'medvya_patients'],
                                    ['scheduling', 'ScheduleSlots', 'medvya_scheduling'], ['scheduling', 'SlotHolds', 'medvya_scheduling'],
                                    ['scheduling', 'ScheduleExceptions', 'medvya_scheduling']]
    LOOP
        IF to_regclass(format('%I.%I', t[1], t[2])) IS NOT NULL THEN
            EXECUTE format('GRANT DELETE ON %I.%I TO %I', t[1], t[2], t[3]);
        END IF;
    END LOOP;
END $$;

-- Tenancy tables reporting may read: no personal data (a test checks every listed table's classified columns). Not listed on purpose:
-- Invitations (invitee email), DoctorLicences (licence number, certificate name), DoctorProfiles (doctor's name, signature key),
-- Profiles (practice contact details), SettingsChanges (before and after values), TenantIndex (no reporting use).
-- Revoked first, so whatever granted more (a default privilege, a schema-wide grant) is undone each run.
REVOKE ALL ON ALL TABLES IN SCHEMA tenancy FROM medvya_reporting;
DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['Tenants', 'Facilities', 'Memberships', 'FeatureFlags', 'OperatingHours', 'Closures', 'PracticeDefaults',
                             'CustomRoles', 'TermsAcceptances', 'AccessReviews', 'AccessReviewDecisions']
    LOOP
        IF to_regclass(format('tenancy.%I', t)) IS NOT NULL THEN
            EXECUTE format('GRANT SELECT ON tenancy.%I TO medvya_reporting', t);
        END IF;
    END LOOP;
END $$;

-- The platform console's organisation approvals (MDV-P1-064, ADR-014 note): the console host's second login, IN ROLE medvya_review alone. It reads
-- the organisation, its onboarding request and documents and the practice profile, updates the state and the request's last reason, and appends the
-- settings change and audit event of a decision. It holds no right on patients, scheduling, encounters, identity, staff memberships or licences, and
-- no schema-wide or default right anywhere (a new table is closed to it until it is listed here). Row-level security still scopes every row: the
-- queue's operator branch covers Tenants, OrganisationRequests and OrganisationDocuments only (migration OrganisationRequests). Granted from scratch
-- on each run, so whatever an earlier run or a default privilege gave more is undone.
REVOKE ALL ON ALL TABLES IN SCHEMA tenancy, audit, ledger FROM medvya_review;
GRANT USAGE ON SCHEMA tenancy, audit, ledger TO medvya_review;
DO $$
DECLARE g text[];
BEGIN
    FOREACH g SLICE 1 IN ARRAY ARRAY[['tenancy.Tenants', 'SELECT'], ['tenancy.OrganisationRequests', 'SELECT'],
                                    ['tenancy.OrganisationDocuments', 'SELECT'], ['tenancy.Profiles', 'SELECT'],
                                    ['tenancy.SettingsChanges', 'SELECT, INSERT'], ['audit.AuditEvents', 'INSERT']]
    LOOP
        IF to_regclass(format('%I.%I', split_part(g[1], '.', 1), split_part(g[1], '.', 2))) IS NOT NULL THEN
            EXECUTE format('GRANT %s ON %I.%I TO medvya_review', g[2], split_part(g[1], '.', 1), split_part(g[1], '.', 2));
        END IF;
    END LOOP;
    -- Column rights (each only once its columns exist: this file also runs before migrations, when a new column is not there yet).
    --   Tenants.State and OrganisationRequests.LastReason: all a decision changes, so not the organisation's name or anything else.
    --   AuditEvents chain head: the ledger trigger (run as the caller) reads it to link a new row; ids, sequence and hash, never what happened.
    FOREACH g SLICE 1 IN ARRAY ARRAY[['tenancy.Tenants', 'UPDATE', 'State'], ['tenancy.OrganisationRequests', 'UPDATE', 'LastReason'],
                                    ['audit.AuditEvents', 'SELECT', 'TenantId,ChainSeq,RowHash']]
    LOOP
        IF to_regclass(format('%I.%I', split_part(g[1], '.', 1), split_part(g[1], '.', 2))) IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM unnest(string_to_array(g[3], ',')) AS col(name) WHERE NOT EXISTS (
                SELECT 1 FROM pg_attribute WHERE attrelid = to_regclass(format('%I.%I', split_part(g[1], '.', 1), split_part(g[1], '.', 2)))
                AND attname = col.name AND NOT attisdropped))
        THEN
            EXECUTE format('GRANT %s (%s) ON %I.%I TO medvya_review', g[2],
                (SELECT string_agg(format('%I', c), ', ') FROM unnest(string_to_array(g[3], ',')) AS c), split_part(g[1], '.', 1), split_part(g[1], '.', 2));
        END IF;
    END LOOP;
    -- The tenant row's read policy has a facility-picker branch that looks the signed-in user up in Memberships; a policy runs with the caller's rights,
    -- so it needs those three columns and no others (no role, facility or invitation: nothing about a person beyond an opaque id).
    IF to_regclass('tenancy."Memberships"') IS NOT NULL THEN
        GRANT SELECT ("TenantId", "UserId", "Status") ON tenancy."Memberships" TO medvya_review;
    END IF;
    -- The chain triggers of the audit and settings tables list a tenant's first row in ledger."Chains" (see the module roles above).
    IF to_regclass('ledger."Chains"') IS NOT NULL THEN
        GRANT SELECT ON ALL TABLES IN SCHEMA ledger TO medvya_review;
        GRANT INSERT ON ledger."Chains" TO medvya_review;
    END IF;
END $$;

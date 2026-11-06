-- Rights on single tables that are narrower than the schema-wide grants in roles.sql (MDV-P0-041). db-migrate applies this file twice:
-- right after roles.sql (as the administrator, which re-grants schema-wide rights) and at the end of every migration run (as medvya_owner,
-- once the tables exist). Idempotent; a table that does not exist yet is skipped and picked up by the next run.

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

    -- The chain trigger lists a tenant's first row in ledger."Chains"; ledger."Tables" stays read-only (roles.sql).
    IF to_regclass('ledger."Chains"') IS NOT NULL THEN
        GRANT INSERT ON ledger."Chains" TO medvya_tenancy, medvya_patients, medvya_audit;
    END IF;
    -- The digest job records each published digest (MDV-P0-042); append-only (its triggers refuse UPDATE/DELETE for every role too).
    IF to_regclass('ledger."Digests"') IS NOT NULL THEN
        GRANT INSERT ON ledger."Digests" TO medvya_audit;
    END IF;
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

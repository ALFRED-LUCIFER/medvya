# Data model (ERD) for Azure SQL

Sources: Software Architecture §6–8 (written for PostgreSQL), Foundations ERD coverage, Handoff ERD additions, PRD v2 §07b. The ERD → Azure SQL type mapping is an **open item that must be closed before the end of P0** (PRD v2 completeness check). Ticket MDV-P0-016 delivers it. This page is the starting point.

## Conventions

- **Primary keys:** `uniqueidentifier` with sequential GUIDs (`NEWSEQUENTIALID()` or app-generated UUIDv7) to limit index fragmentation.
- **Tenant column:** `tenant_id uniqueidentifier NOT NULL` on every clinic-owned row (PRD calls it `clinic_id`). Composite FKs `(tenant_id, id)` where a child references a parent.
- **Concurrency:** `row_version rowversion` on mutable aggregates (optimistic concurrency).
- **Timestamps:** `datetimeoffset(3)` in UTC. The facility time zone is stored on FACILITY.
- **Money:** `bigint amount_minor` + `char(3) currency`. Never `decimal` for stored money.
- **Free JSON:** `nvarchar(max)` with `ISJSON` check, or the native `json` type where available.
- **Soft delete:** avoided for clinical data. Use status + retention jobs. Hard delete only via retention/erasure workflows (P5-12).
- **Schemas:** one per module (`patients`, `scheduling`, `encounters`, …). One least-privilege DB role per module, an app role made of them, a separate migration role and a read-only reporting role: [database-security.md](database-security.md) (P0-09, MDV-P0-008).

## PostgreSQL → Azure SQL mapping (from PRD v2 finding)

| Source assumption | Azure SQL equivalent |
|---|---|
| `uuid` | `uniqueidentifier` |
| `jsonb` | `json` / `nvarchar(max)` + `ISJSON` check. Index computed columns if queried |
| `timestamptz` | `datetimeoffset(3)` |
| `text` | `nvarchar(n)` / `nvarchar(max)` (Unicode needed for Arabic, Hindi, Tamil) |
| RLS policies | `CREATE SECURITY POLICY` with an inline TVF predicate on `SESSION_CONTEXT(N'tenant_id')`, FILTER + BLOCK predicates |
| Exclusion constraint (no overlapping slots) | Unique index `(doctor_id, location_id, slot_start)` on SCHEDULE_SLOT (one row per 5-minute unit an appointment occupies) + serializable transaction + `sp_getapplock` per doctor-day (P1-07, MDV-P1-022) |
| `pgaudit` | Azure SQL auditing → Log Analytics + immutable storage. App audit in **ledger tables** |
| `pgcrypto` / app encryption | **Always Encrypted** (deterministic for exact lookups on IDs; randomized + VBS enclave for range/LIKE) |
| Partial index on active follow-ups | Filtered index `WHERE status IN (...)` |

## Data classification (P0-10)

| Class | Examples | Controls |
|---|---|---|
| Public | Clinic name, doctor public profile | — |
| Internal | Config, service catalogue | Tenant RLS |
| Personal | Name, phone, email, address, DOB | RLS + dynamic data masking for support/reporting roles. No PHI in logs |
| Sensitive health | Notes, problems, meds, results, transcripts, audio | RLS + relationship/purpose authorisation + audit on read. Blob with CMK |
| National ID / financial ID | Aadhaar last 4, ABHA, Emirates ID, passport, insurance member no. | **Always Encrypted** (column master key usable only by the API identity) + tokenised storage where possible |

## Entities by area

### A · Tenant, practice, PMS and billing
TENANT (`country_code`, `residency_region`, `status`) · FACILITY (`tenant_id`, `facility_type`, `timezone`, `authority_mode`) · DEPARTMENT · USER (`entra_object_id` UK, `status`) · MEMBERSHIP (`tenant_id`, `user_id`, `facility_id`, `role_id`, `status` invited/active/suspended/offboarded, `expires_at`) · ROLE / ROLE_PERMISSION (`built_in`, `capability`, `granted`) · CLINICIAN_PROFILE · CLINICIAN_LICENCE (`authority`, `number`, `category`, `expires_at`, `verified_by`, `verified_at`) · SCHEDULE_TEMPLATE (`weekday`, `start`, `end`, `room`, `slot_minutes`) · LEAVE (`from`, `to`, `reason`) · HOLIDAY · PATIENT (`local_patient_number`/MRN, encrypted demographics) · PATIENT_IDENTIFIER (`scheme` ABHA/AADHAAR_LAST4/EMIRATES_ID/PASSPORT/…, `tokenized_value`, `issuer`) · PATIENT_RELATIONSHIP (`related_name`, `relationship`, `phone`, `is_emergency`, `is_guardian`) · PAYER_COVERAGE (`payer_id`, `plan`, `member_no` AE, `valid_to`, `country`) · CONSENT_RECEIPT (`consent_type`, `policy_version`, `jurisdiction`, `decision`, `decided_at`, `channel`, `encounter_id?`) · SCHEDULE_SLOT · APPOINTMENT (`state`, `source` desk/portal/voice, `reason_category`) · WAITLIST_ENTRY · QUEUE_TOKEN · SERVICE_CATALOGUE (`category`, `code`, `sac_or_cpt`, `tax_code`, `duration_min`) · PRICE_VERSION (`amount_minor`, `currency`, `effective_from`, `payer_id?`) · PACKAGE · INVOICE / INVOICE_LINE · PAYMENT · PAYMENT_SPLIT (`method`, `amount_minor`, `provider_ref`, `state`, `confirmed_at`) · DOC_SEQUENCE (`country`, `doc_type` receipt/invoice/credit/advance, `fin_year`, `prefix`, `next_no`) · PACKAGE_ENROLMENT / REDEMPTION · DUE / STATEMENT · CASH_SESSION (`float_minor`, `counted_json`, `variance_minor`, `reason`, `confirmed_by`) · CLAIM · ELIGIBILITY_CHECK / PRIOR_AUTH · REMITTANCE / CLAIM_RESUBMISSION.

### B · EMR and clinical authority
ENCOUNTER (`authority_mode` medvya/his, `status`) · OBSERVATION · PROBLEM (`icd10`, `verification`) · ALLERGY (`status` confirmed/patient_reported/unknown/not_allergy) · MEDICATION_STATEMENT (`source`, `review_state`) · CLINICAL_NOTE / NOTE_VERSION (append-only, `amends_version_id`, `document_hash`) · CLINICIAN_SIGNATURE · CLINICAL_ORDER · CLINICAL_RESULT · PRESCRIPTION / PRESCRIPTION_ITEM · REFERRAL (`to_clinician_ref`, `urgency`, `channel`, `state`, `consent_id`) · PHARMACY_DISPATCH · FORMULARY_ITEM (`inn`, `strength`, `form`, `reg_class` Schedule H/H1/X, `national_code`, `atc`, `defaults_json`) · ORDER_SET · CLINICAL_DOCUMENT · DOCUMENT_PROVENANCE · EXTERNAL_RECORD_LINK.

### C · Consent, capture, transcript and AI
CONSENT_RECEIPT (recording, per encounter) · ENCOUNTER_PARTICIPANT · DEVICE · DEVICE_PAIRING · RECORDING_SESSION (`capture_state`, `consent_receipt_id`) · AUDIO_CHUNK (`sequence_no`, `blob_object_key`, `sha256_ciphertext`, `expires_at`) · TRANSCRIPT · TRANSCRIPT_SEGMENT (`start_ms`, `end_ms`, `content_ciphertext`, `confidence_band`, `lang`) · SPEAKER_LABEL (`provisional_label`, `clinician_confirmed_role`) · AI_JOB / AI_RUN (`model_ref`, `prompt_hash`, `schema_version`, `region`) · NOTE_DRAFT · EVIDENCE_LINK (`json_path`, `transcript_segment_id`, `source_type`).

### D · Care, portal, integration and audit
CARE_PLAN · CARE_TASK (`owner_membership_id`, `task_type`, `clinical_status`, `due_at`) · CARE_TASK_EVENT · INBOX_ITEM (`patient_match_state`, `review_state`) · REVIEW_ACKNOWLEDGEMENT · MESSAGE_THREAD / MESSAGE (`category`, `owner_id`, `sla_due`, `body_ciphertext`) · NOTIFICATION / NOTIFICATION_ATTEMPT · PORTAL_ACCESS · PORTAL_PROXY (`scopes`, `expires_at`, `authority`) · INTEGRATION_CONNECTION (`vault_secret_ref`) · EXTERNAL_PATIENT_MAP / EXTERNAL_RESOURCE_MAP · INTEGRATION_MESSAGE / DELIVERY (`idempotency_key` UK, `receipt_state`, `remote_receipt_ref`) · INTEGRATION_ATTEMPT · AUDIT_EVENT (**ledger**) · OUTBOX_EVENT.

## Key constraints

- `(tenant_id, facility_id, local_patient_number)` unique (MRN facility-scoped).
- `(doctor_id, location_id, slot_start)` unique on SCHEDULE_SLOT: every 5-minute unit of an occupying appointment has a row, deleted when the appointment is cancelled, marked no-show or moved, so overlaps on different slot grids are refused too (MDV-P1-022).
- NOTE_VERSION: no UPDATE permission for the app user once `signed_at` is set (ledger table, append-only).
- AUDIT_EVENT, CONSENT_RECEIPT, NOTE_VERSION (signed), INVOICE (issued) live in **ledger tables**. Digests are stored in immutable Blob and verified on schedule.
- DOC_SEQUENCE increments inside the same transaction as issuing (gapless per country rules). Built in MDV-P4-001 as `billing.DocSequences`, one row per (tenant, country, document type, financial year, prefix), unique. `DocumentNumbering` runs one `MERGE … WITH (HOLDLOCK)` in the caller's transaction (it refuses to run without one): concurrent issuers of a series wait on the row lock, and a rollback returns the number. No SQL `SEQUENCE`/`IDENTITY`, which leave gaps on rollback. Formats come from the billing country rules: India `INV/25-26/00042`, financial year 1 April–31 March in India time, at most 16 characters (CGST Rule 46(b)); UAE `TI-AD-00042`, one continuous series, because the design's numbers carry no year and a yearly reset would repeat them. Both formats are reviewed by the advisers in MDV-P4-031.
- Money is `Money` (`BuildingBlocks.Domain`): `long` minor units plus ISO 4217 currency (INR, AED). Money columns (from MDV-P4-002) are `bigint` + `char(3)`. Rates round half away from zero to the minor unit. No `decimal`, `float` or `money` column exists in the database (`BillingNumberingTests.NoColumnIsFloatingPointOrDecimal`), and no domain property uses a floating-point type (`FloatingPointMoneyTests`).
- No byte arrays for audio or documents in SQL. Blob keys are random UUIDs, with no names, MRNs or diagnoses in paths.

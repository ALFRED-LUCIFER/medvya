# Data classification register (MDV-P0-016)

Every stored field has one class. In code, each domain property carries `[Classification(DataClass.X)]` (`src/BuildingBlocks/Medvya.BuildingBlocks.Domain/Classification.cs`). The architecture test `ClassificationTests` fails the build if any public domain property lacks it. Log redaction (MDV-P0-018), masking and encryption (MDV-P0-009) and audit-on-read (MDV-P0-017) all key off this class.

## Classes and the controls they trigger

| Class | `DataClass` | Examples | Controls |
|---|---|---|---|
| Public | `Public` | Clinic display name, doctor public profile | None beyond integrity |
| Internal | `Internal` | Ids, status codes, timestamps, configuration, service catalogue | Tenant RLS. May appear in logs as opaque values |
| Personal | `Personal` | Patient/staff name, phone, email, address, date of birth | RLS. Support and reporting see only the masked views (MDV-P0-037). Never in logs or telemetry |
| Sensitive health | `SensitiveHealth` | Notes, problems, allergies, medicines, vitals, results, transcripts, documents, free-text visit reason | RLS + relationship/purpose authorisation. **Audit on every read.** Never in logs. Blob under CMK |
| National / financial ID | `NationalId` | Aadhaar last 4, ABHA number and address, Emirates ID, passport, visa file, insurance member number | **Encrypted in the application** (AES-256-GCM, per-tenant key wrapped by Key Vault; only the API identity can unwrap, MDV-P0-035). Exact-match lookups use an HMAC blind index. Never in logs. Masked in UI except where needed |

**Rule for logs and telemetry:** only `Internal` and `Public` values may be emitted. Everything else is dropped by the telemetry processor (MDV-P0-018).

## Register: planned fields, P0–P2

`AE` = encrypted in the application, MDV-P0-035 (the short name is kept from Always Encrypted; D = exact match through the blind index, R = display only). `DDM` = left out of, or masked in, the reporting views, MDV-P0-037 (a table with no view is not readable by reporting at all). `Audit` = read is audited.

### Identity / Tenancy (P0)

| Entity.field | Class | Control |
|---|---|---|
| TENANT.id, country_code, residency_region, status | Internal | — |
| TENANT.display_name | Public | — |
| FACILITY.id, tenant_id, facility_type, timezone, authority_mode | Internal | — |
| FACILITY.name, address | Public | — |
| FACILITY.registration_no (CE reg., GSTIN, TRN, DoH facility licence) | Internal | — |
| USER.id, entra_object_id, status | Internal | — |
| USER.display_name, email, mobile | Personal | DDM |
| MEMBERSHIP.* (ids, role, status, expires_at) | Internal | — |
| INVITATION.token_hash, expires_at, status | Internal | Token stored hashed only |
| INVITATION.email_or_mobile | Personal | DDM |
| AUDIT_EVENT.* (actor id, tenant, patient ref (opaque), action, purpose, outcome, time) | Internal | Ledger table. Opaque patient ref only |
| AUDIT_EVENT.ip | Personal | DDM |
| FEATURE_FLAG.* | Internal | — |

### Practice and patients (P1)

| Entity.field | Class | Control |
|---|---|---|
| CLINICIAN_PROFILE.name, qualifications, specialty, languages | Public | (opt-in public profile P4) |
| CLINICIAN_LICENCE.authority, number, category, expires_at | Internal | — |
| CLINICIAN_LICENCE.certificate_blob_key | Internal | Private Blob |
| CLINICIAN_PROFILE.signature_blob_key | Personal | Private Blob. Print only |
| PATIENT.id, tenant_id, facility_id, mrn (local_patient_number) | Internal | — |
| PATIENT.full_name (as on the ID), arabic_name | Personal | DDM. Audit |
| PATIENT.date_of_birth (DDM), date_of_birth_estimated | Personal | Audit |
| PATIENT.sex_at_birth, gender_identity | SensitiveHealth | Audit. DDM on gender_identity |
| PATIENT.preferred_language, nationality | Personal | — |
| PATIENT.mobile, alternate_phone, email | Personal | DDM |
| PATIENT.address_* (house, street, city, district, state/emirate, PIN, Makani, P.O. box) | Personal | DDM |
| PATIENT_IDENTIFIER.scheme, issuer | Internal | — |
| PATIENT_IDENTIFIER.value for ABHA number / ABHA address / Emirates ID / passport | NationalId | **AE-D** (exact lookup; passport since MDV-P1-016) |
| PATIENT_IDENTIFIER.value for Aadhaar (last 4 only), visa file | NationalId | **AE-R** |
| PATIENT_IDENTIFIER.expires_at (EID expiry) | Personal | — |
| PATIENT_RELATIONSHIP.related_name, phone | Personal | DDM |
| PATIENT_RELATIONSHIP.relationship, is_emergency, is_guardian | Internal | — |
| PAYER_COVERAGE.payer_id, plan, valid_to, network | Internal | — |
| PAYER_COVERAGE.member_no / policy_no | NationalId | **AE-D** |
| CONSENT_RECEIPT.* (type, policy_version, jurisdiction, decision, decided_at, channel, actor) | Internal | Ledger table |
| MERGE_REQUEST.* | Internal | Audit |
| SCHEDULE_TEMPLATE.* (calendar, weekday hours, room, breaks and their labels, slot and visit lengths) | Internal | — |
| SCHEDULE_RESOURCE.* (name, e.g. "Procedure room") | Internal | — |
| APPOINTMENT.id, doctor, location, slot_start, state, source, reason_category, room (set at call-in, MDV-P1-029) | Internal | — |
| Board hub message `BoardChanged` (appointment id, state, changed at) | Internal | Ids and state only; payload allowlist test (MDV-P1-029) |
| APPOINTMENT.reason_text (free text) | SensitiveHealth | Clinicians only. Audit. Never returned to reception screens or put in events (MDV-P1-021) |
| SLOT_HOLD.* (calendar, slot, visit type, held_by, expires_at) | Internal | — |
| SCHEDULE_SLOT.* (calendar, location, 5-minute slot start, appointment) | Internal | Unique (calendar, location, slot start) |
| VISIT_STATUS_EVENT.* (appointment, state, actor, time, cancel reason) | Internal | Append-only history |
| VISIT_STATUS_EVENT.note (cancellation note, free text) | SensitiveHealth | May mention health. Never on reception screens, in logs or in events (MDV-P1-024) |
| APPOINTMENT_RESCHEDULE.* (appointment, old and new calendar and times, actor, time, reassign reason code) | Internal | Append-only history (MDV-P1-024); a reassign reason is a fixed code, never free text (MDV-P1-030) |
| OUTBOX_EVENT.* (type, aggregate id, payload of opaque ids and instants) | Internal | No PHI in payloads |
| WAITLIST_ENTRY.*, QUEUE_TOKEN.* (reception queue: facility day, letter per doctor, number, visit) | Internal | Tokens on TV, never names (MDV-P1-031) |
| QUEUE_DISPLAY.* (facility, name, token hash, created/revoked by and at) | Internal | Only the SHA-256 of the display token is stored; the token is shown once |
| NOTIFICATION.template_id, channel, state, provider_ref | Internal | Templates carry no clinical data |
| NOTIFICATION.recipient (phone/email) | Personal | DDM |

### Platform console: the Medvya team (P5, MDV-P5-040, ADR-014)

Not clinic data, so no tenant. Only `medvya_platform` reaches the `platform` schema; the clinic app role and reporting do not, and the console login reaches no clinic schema. No field is SensitiveHealth or NationalId (`TheConsoleDomainHoldsNoHealthOrNationalIdField`).

| Entity.field | Class | Control |
|---|---|---|
| platform.Members: name, email | Personal | Medvya staff, not patients. Never in logs |
| platform.Members: role, issuer, subject, invited, access end, last active, removed | Internal | Removed by date, never deleted, so the audit log can name them |
| platform.SecurityKeys: credential id, public key, counter, revoked | Internal | Public key material only; revoked, never deleted |
| platform.KeyLinks: token hash, purpose, issued, expires, used | Internal | Only the SHA-256 of the one-hour token is stored; the token is shown once to the issuing owner |
| platform.Sessions: times, key verified, pending ceremony | Internal | Server-side session behind the console cookie |
| platform.AuditEntries: action, resource, actor, subject practice id, reason, time, correlation id | Internal | Append-only. The reason is typed by Medvya staff; the form says never to enter patient details |
| platform.AuditEntries: client IP | Personal | As in the clinic audit log |

### Leads: public-site contacts (P1, MDV-P1-002/003/057)

Not clinic data, so no tenant. Of the app roles only `medvya_leads` reaches the `leads` schema (select and insert); reporting does not.

| Entity.field | Class | Control |
|---|---|---|
| leads.DemoRequests: id, organisation, role, country, received_at | Internal | — |
| leads.DemoRequests: name, email, phone | Personal | Never in logs. Sent to the founder by email (data map) |
| leads.DemoRequests: message (free text) | Personal | Never in logs and never emailed: a visitor could type patient details despite the warning |
| leads.DemoRequests: consented_at, notice_version | Internal | Proof of consent to the privacy notice shown above the form (UTC time, notice version). Null only on rows stored before the notice |
| leads.DemoRequests: preferred_at | Internal | The visitor's preferred demo time (UTC), a request the team confirms (MDV-P1-057). Sent to the founder by email |
| leads.Waitlist: id, country, received_at | Internal | — |
| leads.Waitlist: name, email, phone | Personal | Never in logs |
| leads.Waitlist: consented_at, notice_version | Internal | Proof of consent, as for demo requests |

### Onboarding (P1, MDV-P1-004)

| Entity.field | Class | Control |
|---|---|---|
| identity.OnboardingDrafts: user_id, country, step, updated_at, completed_at | Internal | One row per person, never listed. Not in reporting (identity schema) |
| identity.OnboardingDrafts: data (name, licence, clinic address and fees as typed) | Personal | Emptied when the practice is created. Never in logs |
| tenancy.PracticeProfiles: tenant_id, slot_minutes, fees (minor units), currency, hours, note templates, registration number, GSTIN/TRN | Internal | Row-level security |
| tenancy.PracticeProfiles: address | Personal | A solo doctor's clinic can be a home address. Row-level security |
| tenancy.PracticeProfiles: specialty, qualifications, languages | Public | Shown to patients later (P5) |
| tenancy.DoctorLicences: membership_id, tenant_id, authority, category, expires_on, submitted_at, verified_at, verified_by, rejected_at, rejection_reason | Internal | Row-level security. Until verified_at is set (by Medvya operations only), recording and signing capabilities are off. The rejection reason is operations' text shown to the practice; the screen tells them to keep personal details out |
| tenancy.DoctorLicences: number, certificate_name | Personal | The certificate file itself is not stored yet (shortcut in the wizard) |
| tenancy.DoctorProfiles: full_name | Personal | Row-level security. Added doctors and the owner doctor (MDV-P1-007) |
| tenancy.DoctorProfiles: specialty, qualifications, languages | Public | Shown to patients later (P5) |
| tenancy.DoctorProfiles: fees (minor units), changed_by, changed_at | Internal | Per-doctor fee defaults |
| tenancy.DoctorProfiles: signature_key | Personal | Key of the private Blob object (random path, no names). The image is never served to the screen; only printed prescriptions read it. Not a way to sign |

### Clinical (P2)

| Entity.field | Class | Control |
|---|---|---|
| ENCOUNTER.id, status, started_at, room, authority_mode | Internal | — |
| ENCOUNTER.chief_complaint | SensitiveHealth | Audit |
| OBSERVATION.code, value, unit, taken_at, source | SensitiveHealth | Audit |
| ALLERGY.substance, reaction, status | SensitiveHealth | Audit |
| PROBLEM.icd10_code, description, verification, onset | SensitiveHealth | Audit |
| MEDICATION_STATEMENT.*, PRESCRIPTION_ITEM.* | SensitiveHealth | Audit |
| CLINICAL_NOTE / NOTE_VERSION.body | SensitiveHealth | Ledger (signed). Audit |
| NOTE_VERSION.document_hash, signed_at, signer_id | Internal | Ledger |
| CLINICAL_ORDER.*, REFERRAL.reason, summary | SensitiveHealth | Audit |
| REFERRAL.state, channel, urgency | Internal | — |
| CLINICAL_DOCUMENT.blob_key, type, source | Internal | Private Blob (CMK). Content is SensitiveHealth |
| CLINICAL_DOCUMENT.title, extracted values | SensitiveHealth | Audit |
| FORMULARY_ITEM.*, ORDER_SET.* | Internal | — |

### Billing (P4, started in MDV-P4-001)

| Entity.field | Class | Control |
|---|---|---|
| DOC_SEQUENCE.* (tenant, country, doc_type, fin_year, prefix, next_no) | Internal | Tenant RLS. No `DELETE` for the app (`medvya_billing`) |

## Protected column sets

**Encrypted in the application (MDV-P0-035, replaces Always Encrypted from MDV-P0-009).** Keep this set small: an encrypted value can only be matched exactly.

| Column | Stored as | Why |
|---|---|---|
| PATIENT_IDENTIFIER.value (ABHA number, ABHA address, Emirates ID, passport) | `LookupValue` ciphertext + `LookupIndex` blind index | Exact-match search at the desk; passport for the duplicate check only (MDV-P1-016) |
| PATIENT_IDENTIFIER.value (Aadhaar last 4) | randomized `DisplayValue` ciphertext + `LookupIndex` blind index (MED-FEAT-001, #931) | Desk search by the last 4 (a typed 12-digit Aadhaar is cut to its last 4 on the server and dropped); rows registered earlier have no index |
| PATIENT_IDENTIFIER.value (Aadhaar last 4, visa file) | `DisplayValue` ciphertext | Display only, no lookup |
| PAYER_COVERAGE.member_no | `MemberNumber` ciphertext | Shown to the desk; no lookup yet (add a blind index when eligibility needs exact match) |

PATIENT_IDENTIFIER keeps two value columns and each row uses exactly one, chosen by scheme (`PatientIdentifier.IsLookup`); only lookup schemes get a blind index. Nothing else is encrypted: names, phone and DOB are protected by RLS, the masked views and audit instead, so they stay searchable for duplicate checks. Keys and the code: [database-security.md](database-security.md#national-id-encryption-mdv-p0-035). The architecture test cannot see encryption; `PatientRegistryTests.AnAdministratorWithoutTheKeySeesOnlyCiphertext` pins the list.

**Masked views (support and reporting roles, MDV-P0-037):** the role reads only schema `reporting`. `reporting."Patients"` shows initials and an age band instead of names and date of birth and has no gender identity, phone, email or address; `reporting."Identifiers"` shows the scheme and length only. Staff USER fields, PATIENT_RELATIONSHIP.phone, NOTIFICATION.recipient and AUDIT_EVENT.ip have no view yet, so reporting cannot read those tables at all.

## ERD → Azure SQL type mapping

Closed: see [data-model.md](data-model.md#postgresql--azure-sql-mapping-from-prd-v2-finding). This resolves the PRD v2 completeness item "ERD mapped to Azure SQL types".

## Data map

Covered by MDV-P0-024 (threat model, data map, DPIA). Each class above maps to: **stored** in Azure SQL (country region) or Blob (CMK, same region); **processed** by Host.Api (and, from P3, the AI worker for SensitiveHealth audio and transcripts, in-region); **never** sent to logs, analytics or third countries.

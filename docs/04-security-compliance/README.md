# Security and compliance

> Hosting on Azure does not make Medvya compliant. Qualified legal and clinical review in each market is required before real patient records are processed: **P1 for demographics, P2 for clinical data, P3 for audio** (PRD v2). Policy labels in the product show configured policy, **not certification claims**.

## Database protection: the 12 layers (PRD v2 §07b)

Full table in [../01-prd/technology-and-db-security.md](../01-prd/technology-and-db-security.md). All twelve must be in place **before the first real record (P1)**:

Network (private endpoint only) · Who can connect (Entra-only, managed identity, per-module users, JIT for humans) · Encryption at rest (TDE + CMK) · Encryption in use (Always Encrypted on Aadhaar, Emirates ID, passport, ABHA, insurance member numbers) · In transit (TLS 1.2+, `Encrypt=Strict`, HSTS) · Row access (RLS on `SESSION_CONTEXT` + EF filters + CI cross-tenant tests) · Masking (phone, email, DOB for support/reporting roles) · Tamper evidence (ledger tables for audit, signed notes, consents, invoices; digests in immutable Blob) · Monitoring (SQL auditing, Defender for SQL, alerts on bulk reads/exports) · Backup & recovery (PITR 35 days, LTR in-country, monthly restore drill) · Application (parameterised queries only, validation, rate limits, watermarked exports) · Secrets (Key Vault RBAC, purge protection, GitHub OIDC).

## Security by layer (Software Architecture §10)

| Layer | Threat | Defence |
|---|---|---|
| Authentication | Shared clinic passwords, account takeover | Entra External ID, MFA for all staff, passkeys preferred, short sessions, idle timeout on shared PCs, session revocation, suspicious-login alerts |
| Authorisation | IDOR, receptionist reading notes, tenant leakage | Tenant + facility + relationship + purpose checks. RBAC + ABAC. Field-limited responses. RLS defence in depth. Adversarial tests |
| Consent | Invisible recording, withdrawn permission | Versioned consent per encounter/purpose/channel. Start blocked without consent. Withdrawal stops capture and propagates to pending jobs |
| Network/API | Replay, injection, large files, abuse | HTTPS, strict CORS, CSP, rate limits, request size limits, malware scan, idempotency keys, validation, antiforgery (cookies) |
| Uploads/audio | Guessable key, cross-tenant SAS | Private storage, UUID keys, SAS scoped to one encounter/blob, integrity hashes, type checks |
| AI pipeline | Hallucination, prompt injection, wrong speaker, provider leakage | Untrusted input, no tools, schema + provenance validators, in-region endpoints, clinician sign-off |
| Clinical workflow | AI orders silently, abnormal result auto-closed | Signed note required. Explicit task owner. Clinician-approved templates. No autonomous interpretation to patients |
| Integration | Wrong chart, premature "synced" | Patient + encounter mapping verified, idempotent export, receipts, retry/dead-letter/reconciliation |
| Observability | PHI in logs | Allowlist telemetry. Redact bodies, prompts, query strings, auth headers, patient metadata, blob URLs |
| Supply chain | Dependency compromise | Pinned deps, SAST/SCA/secret scanning, SBOM, signed artifacts, OIDC (no long-lived keys) |
| Operations | Ransomware, unrecoverable DB | Environment separation, protected backups, restore drills, incident playbooks, breach notification matrix |

**Checklist:** OWASP ASVS Level 2 from the first commit (P0 NFR).

## Data residency

| | India | UAE |
|---|---|---|
| Primary / DR | Central India / South India | UAE North / UAE Central |
| Law | DPDP Act 2023 + Rules 2025 (track the commencement schedule provision by provision); Telemedicine Practice Guidelines; ABDM policies | Federal Law No. 2 of 2019 (ICT in health: health data localisation); DoH ADHICS; DHA standards; exceptions need regulator approval |
| Must stay in country | PHI, audio, backups, **AI inference and speech processing**, support access, telemetry with any PHI | Same |
| Cross-border | Disabled by default | Disabled by default |

## Consent model (separate, versioned receipts)

| Consent | Where captured | Default |
|---|---|---|
| Treatment | Registration (P-02) | Required |
| Reminders (per channel: WhatsApp, SMS, email) | Registration, portal R-03 | Per opt-in |
| Health information exchange / ABHA linking | Registration, portal | Off unless chosen (UAE: per emirate HIE policy) |
| Portal access | Registration | Invite on |
| Marketing / health tips | Registration, portal | **Off** |
| **Recording** | **Each encounter (A-02)**, not at registration | Asked every visit. Decline → manual note |
| Caregiver proxy | Portal R-04 | Scoped, with expiry |

Each receipt stores `consent_type`, `policy_version`, `jurisdiction`, `decision`, `decided_at`, `channel`, and the actor (patient or staff on the patient's behalf).

## Retention (H-06 defaults, subject to legal review)

| Data | Default |
|---|---|
| Consultation audio | 30 days, then delete (purpose: documentation only). Legal hold blocks deletion |
| Transcripts and AI drafts | Same as the medical record (kept with the signed note) |
| Medical records | Per applicable country rules, configured with the legal team |
| Audit events | 7 years (configurable), append-only, exportable |
| Cross-border transfer | Disabled |

## Compliance artefacts and when they are due

| Artefact | Ticketed in |
|---|---|
| Threat model, data map, DPIA draft (DPDP), retention map per country, ADR-001/002 | P0-08 |
| Data classification, synthetic-only test data policy, SECURITY.md, JIT access, offline break-glass account | P0-10 |
| Terms of service, privacy notice, clinic DPA, versioned patient consents | P1-12 |
| Clinical safety case and hazard log, named clinical safety reviewer | P2-08 |
| AI vendor DPA, recording consent legal review, waiting-room notices (EN/HI/AR) | P3 |
| Incident response + breach notification (DPDP and UAE), Sentinel/Defender on-call, Customer Lockbox, VDP, ADHICS evidence, ISO 27001 gap assessment | P5-11 |
| Retention/deletion jobs, data subject requests | P5-12 (clinics), P7 H-10 (enterprise) |
| External penetration test, DR drill, accessibility audit | P5-10 |
| Regulated-device classification advice (CDSCO India / UAE) for the clip | Before commercial hardware shipment (P6) |

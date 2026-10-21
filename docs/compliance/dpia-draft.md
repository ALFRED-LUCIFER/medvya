# Data protection impact assessment, draft (MDV-P0-024)

> **Draft for counsel.** Prepared by engineering against the India **Digital Personal Data Protection Act 2023 and DPDP Rules 2025**, with UAE notes. It is not legal advice. Items marked ⚖ need legal confirmation before real data. Commencement of DPDP duties is phased, so track the schedule provision by provision (Software Architecture §10).

## 1 · Processing described

Medvya is a cloud platform used by clinics (the **Data Fiduciaries** for their patients) to register patients, book appointments, document care (including AI-drafted notes from consented recordings), prescribe, bill and follow up. Medvya acts as a **Data Processor** for clinic data, and as a Data Fiduciary for its own staff-account and subscription data. ⚖ Confirm the role split and the processing agreement (MDV-P1-044).

## 2 · Necessity and proportionality

| Principle | How Medvya meets it |
|---|---|
| Purpose limitation | Purposes per category in the [data map](data-map.md). Marketing is a separate, default-off consent |
| Data minimisation | Aadhaar stored as **last 4 digits only**. ABHA optional and never required for care. Minimum fields for quick registration |
| Consent | Separate, versioned consents (treatment, reminders per channel, sharing/ABHA/HIE, portal, marketing). **Recording consent per encounter.** Withdrawal honoured and recorded |
| Accuracy | Duplicate checks, merge review with approvers, correction requests (P5-12) |
| Storage limitation | Retention map below. Audio 30 days by default. Scheduled deletion with legal hold |
| Security | 12-layer database protection, Always Encrypted for national IDs, RLS, audit ledger, PHI-free telemetry ([threat model](threat-model.md)) |
| Accountability | Append-only audit; access log visible to clinic owners (F-12); this DPIA, reviewed per phase |

## 3 · Rights of data principals

Access, correction, erasure (subject to medical-record retention), grievance redressal and nomination are handled for clinics through MDV-P5-025 (clinic scale) and MDV-P7-009 (enterprise). ⚖ Confirm response timelines and the grievance officer contact per clinic.

## 4 · Children's data

A guardian is required when age < 18 (registration), and portal access for minors goes through a guardian proxy. ⚖ DPDP requires verifiable parental consent for children's data; confirm the mechanism for clinic registration and portal.

## 5 · Risks and mitigations

| # | Risk to individuals | Likelihood | Severity | Mitigation | Residual |
|---|---|---|---|---|---|
| R1 | Cross-tenant disclosure (one clinic sees another's patients) | Low | High | RLS + EF filters + CI test | Low |
| R2 | Staff snooping on records without care relationship | Medium | High | Relationship/purpose checks, audit of every read, owner access log, break-glass review | Low–Medium |
| R3 | Recording without valid consent | Low | High | Capture blocked without per-encounter receipt; revocation stops capture | Low |
| R4 | AI fabricates clinical facts that enter the record | Medium | High | Evidence links, validators, clinician must accept each field and sign; eval CI gate | Low |
| R5 | Breach of national IDs | Low | High | Always Encrypted; keys only usable by the API identity | Low |
| R6 | Disclosure via messaging previews | Medium | Medium | Templates without clinical data; generic push copy | Low |
| R7 | Cross-border transfer (incl. AI inference, support access) | Low | High | Country deployments; deny-cross-region policy; in-region AI; audited, time-boxed support access | Low |
| R8 | Wrong-patient merge or attach | Medium | High | Name+DOB+identifier matching (phone never alone), approvers, reversible merges, wrong-patient lock | Low–Medium |

## 6 · Breach handling

Incident response with notification to the Data Protection Board of India and affected principals, and UAE regulators where applicable, is defined in MDV-P5-024. ⚖ Confirm timelines (DPDP Rules) and responsibilities between Medvya (processor) and clinics (fiduciaries).

## 7 · UAE notes

Federal Law No. 2 of 2019 requires health data to stay in the UAE except where permitted. DoH Abu Dhabi ADHICS and DHA standards apply per emirate. ⚖ Confirm whether the UAE deployment and every sub-processor (including Entra and SendGrid) meet localisation rules before any UAE patient data.

## 8 · Sign-off

| Role | Name | Date | Decision |
|---|---|---|---|
| Founder | | | |
| Counsel (India) | | | |
| Counsel (UAE, if in scope) | | | |
| Clinical advisor | | | |

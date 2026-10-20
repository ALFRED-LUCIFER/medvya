# Product summary

Source: PRD v2 §01–02, Foundations §01, Architecture v2.

## Problem

Clinics in India and the UAE run on a mix of paper, legacy desktop software (often Oracle-based) and WhatsApp. Doctors spend a large part of each visit typing notes. Receptionists juggle calls, walk-ins and payments across separate tools. History from other hospitals and labs arrives as PDFs and photos and rarely reaches the chart. Owners will not switch systems if they lose old data or have to close for a weekend.

## Product

One browser-based platform for the whole clinic day:

- booking and front desk
- nurse intake
- patient chart
- AI-drafted consultation notes that the doctor reviews against source evidence and signs
- prescriptions
- billing and day-end close
- results and follow-up tracking
- patient portal

It starts as a single modular application and grows into multi-clinic SaaS and hospital integration without a rewrite.

**Core promise (UI/UX prompt):** a doctor opens the right patient, sees a 60-second verified brief, obtains recording consent and talks naturally. The doctor records from a phone, desktop mic or wearable clip, reviews speaker labels, edits an evidence-linked AI draft, signs the note and approved orders, and generates accountable follow-ups, all without constant typing. An independent doctor also gets appointments and simple billing. A hospital can instead connect Medvya to its existing authoritative HIS/EMR.

## Markets and data residency

| | India | UAE |
|---|---|---|
| Azure region | Central India (DR: South India) | UAE North (DR: UAE Central) |
| Law and regulation | DPDP Act 2023 and 2025 Rules; Telemedicine Practice Guidelines; ABDM policies | Federal Law No. 2 of 2019 (ICT in health); DoH ADHICS; DHA standards |
| Identity | ABHA (optional), Aadhaar stored as last 4 digits only | Emirates ID (ICP), UAE PASS |
| Exchanges | ABDM (HIP/HIU, consent) | Malaffi (Abu Dhabi), NABIDH (Dubai), Riayati (northern emirates) |
| Claims | TPA, PMJAY, CGHS | eClaimLink (Dubai), Shafafiya (Abu Dhabi) |
| Tax | GST/SAC (clinical services are generally exempt) | VAT 5% (healthcare zero-rated where applicable), CPT codes |
| Messaging | DLT-registered SMS, WhatsApp | SMS, WhatsApp |

Each country has its own data plane. **No patient record is shared across countries by default.**

## Product blocks (Architecture v2, Foundations module groupings)

| Block | Colour rail | Scope | First release |
|---|---|---|---|
| PMS · Practice | blue `#2563EB` | Registration, scheduling, reception, waitlist, billing, reconciliation, claims boundary | P1 appointments, P4 billing |
| EMR · Clinical | teal `#0E9384` | Patient 360, chart, meds, orders, results, signed notes | P2 |
| AI Assistant | violet `#7155D8` | Brief, consent, capture, transcript, evidence, drafts | P3 |
| Care coordination | sage `#4F7A5E` | Inbox, result loop, tasks, reminders, follow-up proof | P4 |
| Patient portal | `#0F766E` | Booking, consent, released records, messages, proxy | P4 |
| Wearable clip | `#9A6B1F` | Pairing, battery, offline queue, firmware, fleet | P6 |
| Hospital enterprise | navy `#102A43` | Facilities, SSO, HIS/FHIR, audit, residency | P7 |

## Product principles (apply to every phase, PRD v2)

1. **The clinician signs.** AI drafts. A named clinician reviews the evidence and approves before anything is saved to the record.
2. **Consent before capture.** Recording, sharing, reminders and marketing are separate consents. Manual documentation is always available.
3. **Provenance on every fact.** Every value shows where it came from (clinic, exchange, patient upload, import or AI), and original files are kept.
4. **Least privilege, audited.** Tenant, facility, role and relationship are checked on the server. Every patient-data read is logged.
5. **Country data stays in country.** India and UAE have separate data planes and no shared patient datastore.
6. **Evolve on evidence.** One application and one database per country until load or a contract proves otherwise.

## Non-negotiable patient-safety constraints (Software Architecture §1)

1. No clinician approval means no signed note, order, patient instruction or EMR write-back.
2. Capture starts only for the verified current encounter, after the required consent. The device LED and the app indicator stay on throughout capture.
3. Identifying a *speaker* is not proof of *identity*. Speaker labels stay provisional until a clinician confirms them, and family members never inherit the patient's identity.
4. Every material generated field keeps its source evidence. Missing or unclear information stays **unknown** and is never guessed.
5. An order drafted, a reminder sent, a result arrived, a result reviewed and a patient contacted are **different states**.
6. A hospital's patient data is not a physician's portable personal data.
7. No unapproved cross-border PHI movement. That includes AI inference, speech services, backups, support access and telemetry.

## Out of scope until clinical and regulatory evidence exists (PRD v2 §10)

- AI diagnosis or autonomous triage
- Complex agent orchestration with broad record access
- Full hospital inventory and ERP
- Insurance claims beyond eligibility, pre-auth and remittance
- Wearables beyond the Medvya clip
- Microservices, Kubernetes, Kafka, GraphQL, Elasticsearch before P7

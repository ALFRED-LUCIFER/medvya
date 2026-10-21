# Data map (MDV-P0-024)

What personal data Medvya holds, why, where it lives and who processes it. Field-level classes are in [../03-architecture/data-classification.md](../03-architecture/data-classification.md). Each country (India, UAE) is a separate deployment with its own copy of this map. **Nothing crosses countries.**

## Data categories

| Category | Data subjects | Examples | Purpose | Lawful basis (to confirm with counsel) | Stored in | From phase |
|---|---|---|---|---|---|---|
| Staff identity | Clinic staff | Name, email, mobile, role, licence | Account, access control, licence verification | Contract with the clinic | Entra External ID (identity); Azure SQL (profile) | P0 |
| Audit trail | Staff, patients (opaque refs) | Who did what, when, purpose, IP | Security, accountability, legal obligation | Legal obligation / legitimate use | Azure SQL ledger + Log Analytics | P0 |
| Patient demographics | Patients | Name, DOB, sex, phone, address, language | Registration, booking, reminders | Consent for treatment / legitimate use for healthcare (DPDP) | Azure SQL (DDM) | P1 |
| National / insurance IDs | Patients | ABHA, Aadhaar last 4, Emirates ID, passport, member no. | Identification, dedup, claims | Consent / legal requirement (UAE EID) | Azure SQL (Always Encrypted) | P1 |
| Relationships | Emergency contacts, guardians | Name, phone, relationship | Emergency contact, minors | Legitimate use / consent | Azure SQL | P1 |
| Consents | Patients | Decisions with policy version | Proof of consent | Legal obligation | Azure SQL ledger | P1 |
| Scheduling | Patients | Appointments, reason category, free-text reason | Care delivery | Healthcare | Azure SQL | P1 |
| Clinical record | Patients | Vitals, allergies, problems, notes, Rx, orders, documents | Care delivery | Healthcare / consent | Azure SQL + Blob (CMK) | P2 |
| Consultation audio and transcripts | Patients, companions, clinicians | Audio, transcript segments, speaker labels | Documentation (AI draft) | **Separate per-encounter recording consent** | Blob (CMK, 30-day default) + Azure SQL | P3 |
| Billing | Patients, payers | Invoices, payments, provider refs | Billing, tax | Contract / legal obligation (tax) | Azure SQL ledger | P4 |
| Portal | Patients, caregivers | Portal account, proxy scopes | Patient access | Consent | Entra External ID + Azure SQL | P4 |

## Processors and sub-processors

| Processor | Role | Data | Region | Contract |
|---|---|---|---|---|
| Microsoft Azure (App Service, SQL, Blob, Key Vault, Monitor, Defender) | Hosting | All | Central India / UAE North (DR in-country) | Microsoft DPA |
| Microsoft Entra External ID | Identity | Staff and patient identity | Per Microsoft data residency for the tenant (confirm per country) | Microsoft DPA |
| SendGrid (Twilio) | Email delivery | Recipient email, name, appointment time, clinic name. **No clinical data** | Confirm processing location | Twilio DPA (to sign before real patient email) |
| SMS provider (DLT-registered, TBD) | SMS delivery | Mobile, template variables. No clinical data | India / UAE | DPA (MDV-P1-045) |
| Azure OpenAI + AI Speech | AI drafting (P3) | Audio, transcripts | In-region only | Microsoft DPA + no-training terms (MDV-P3-022) |
| Payment gateways (Razorpay; Network International or Stripe) | Payments (P4) | Amount, reference, payer contact | Per provider | Provider DPA |
| WhatsApp Business (P4) | Messaging | Mobile, template variables | Per provider | Meta terms + DPA |

## Flows that must never happen

- PHI in logs, telemetry, analytics, crash reports, URLs or browser storage (enforced by P0-018 tests and the API rules).
- Patient data across India ↔ UAE (deny-cross-region policy, separate deployments).
- Real patient data in dev, staging, CI or AI evaluation sets (`tools/check-synthetic`, secure development policy).
- Clinical details in SMS, WhatsApp, email or push previews.

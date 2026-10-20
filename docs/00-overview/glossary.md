# Glossary

| Term | Meaning |
|---|---|
| ABHA / ABDM | Ayushman Bharat Health Account (14-digit ID) / Ayushman Bharat Digital Mission (India's health data exchange) |
| HIP / HIU | ABDM Health Information Provider / User |
| Aadhaar | Indian national ID. Medvya stores **last 4 digits only** |
| Emirates ID (EID) | UAE national ID, format `784-YYYY-NNNNNNN-N`, issued by ICP |
| UAE PASS | UAE national digital identity sign-in |
| Malaffi / NABIDH / Riayati | Health information exchanges of Abu Dhabi (DoH), Dubai (DHA) and the northern emirates (MOHAP) |
| eClaimLink / Shafafiya | UAE claims platforms (Dubai / Abu Dhabi) |
| TPA / PMJAY / CGHS | India: third-party administrator / Ayushman Bharat PM-JAY / Central Government Health Scheme |
| DPDP | India Digital Personal Data Protection Act 2023 and Rules 2025 |
| ADHICS | Abu Dhabi Healthcare Information and Cyber Security Standard |
| DLT | India SMS sender and template registration (TRAI) |
| GST / SAC | India Goods & Services Tax / Services Accounting Code |
| HIS / EMR / PMS | Hospital Information System / Electronic Medical Record / Practice Management System |
| FHIR R4 / HL7 v2 | Interoperability standards (resources / pipe-delimited messages, e.g. ORU results, ORM orders) |
| BFF | Backend-for-frontend: the ASP.NET Core host that keeps tokens server-side and gives the SPA an HTTP-only cookie |
| RLS | Row-level security (Azure SQL security predicates keyed on tenant via `SESSION_CONTEXT`) |
| Always Encrypted | Azure SQL client-side column encryption. Admins only see ciphertext |
| Ledger tables | Azure SQL tamper-evident tables with cryptographic digests |
| CMK | Customer-managed key (Key Vault / Managed HSM) |
| Stamp | A self-contained regional deployment unit (pooled or dedicated) |
| Provenance tag | Label showing a fact's source: AI draft, Clinician verified, Patient reported, Synced from HIS, Imported, Signed |
| Record authority | Which system holds the official chart for a facility: Medvya EMR or the external HIS |
| Break-glass | Emergency access with typed justification and post-hoc review |
| Encounter | One visit; all recordings and notes belong to exactly one verified encounter |
| Addendum | New signed version that amends a signed note; v1 is never edited |
| Closed loop | Ordered → collected → received → reviewed → contacted → follow-up completed, each a distinct state |
| M1–M4 / Later | Milestone labels used in the design inventory (pre-PRD v2). PRD v2 phases P0–P8 supersede them for planning |
| R0–R8 | Release names per phase (R5 = v1.0 GA) |
| N-xx | Screens required by sources but **not yet designed** (DESIGN NEEDED) |
| X1–X8 | Cross-cutting requirement groups (PRD v2 §05) |

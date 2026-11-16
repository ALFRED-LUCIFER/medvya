# Patient data requests during the pilot (manual process)

Until MDV-P5-025 builds request tracking and retention jobs ([ADR-024](../adr/ADR-024-patient-data-requests-and-erasure.md)), every request from a patient (data principal) about their personal data follows these steps by hand. Counsel confirms the deadlines and wording before the first real patient (MDV-P1-044).

**Roles.** The clinic is the data fiduciary (controller) and answers the patient. Medvya is the processor: the founder (or a named Medvya operator) does the steps the clinic cannot do in the app. Nobody acts on a request that arrives only at Medvya: forward it to the clinic and tell the patient it was forwarded.

## 1 · Log the request

The clinic owner records each request in the clinic's request log (a spreadsheet the clinic keeps, outside Medvya): date received, the patient's MRN (never the national ID), the type (access, correction, erasure, consent withdrawal, grievance), who verified identity, the due date and the outcome. No clinical detail in the log. Medvya keeps only the MRN, date, type and outcome of any step it performs, in the support ticket.

## 2 · Verify identity

At the desk with the photo ID the clinic already uses at registration, or by a call back to the mobile number on the record. For a minor or a patient without capacity, verify the guardian on the record. Never accept a request by email alone.

## 3 · Act

| Request | Who | How |
|---|---|---|
| **Access** (a copy of their data) | Clinic | Print or export the patient's record from the app (visit summaries, prescriptions, documents). If a part cannot be exported from the app, the clinic asks Medvya, which exports it from the database as the administrator and hands it to the clinic, never to the patient directly |
| **Correction** | Clinic | Demographics: edit in the app (audited). Clinical content of a signed record is never edited: the doctor adds an addendum (AGENTS.md 1.5) |
| **Erasure** | Clinic decides, Medvya executes | Medical records inside their retention period are held: tell the patient "partially fulfilled, legal hold" and the end date (retention map). For data outside retention (for example reminder consent, contact details no longer needed), the clinic removes what the app allows; anything else waits for Medvya with an erasure hold recorded in the log. Medvya does not delete rows by hand from the database (ledger chains and foreign keys): it records the hold until ADR-024 is built |
| **Consent withdrawal** | Clinic | Turn the consent off in the app; reminders on that channel stop |
| **Grievance** | Clinic, then Medvya | The clinic answers; anything about the software goes to the founder |

## 4 · Close

Record the outcome and date in the clinic's log and tell the patient in writing. Medvya comments the outcome (MRN, type, date, no clinical detail) on its support ticket.

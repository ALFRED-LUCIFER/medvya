<!-- GENERATED from design/Medvya Journeys.dc.html via tools/prd-extract/extract.mjs. -->

# End-to-end journeys (X-03)

Each step: who acts, on which screen, and what changes. 🔒 = clinician approval boundary. ↪ = recovery branch. The P0 exit gate requires the clinical advisor to walk all six journeys and sign them off. In P0 they become Playwright E2E specs (X8).

## J1 · First solo-doctor visit — Solo doctor · M1

| # | Screen | Role | Step | What changes | Gate |
|---|---|---|---|---|---|
| 1 | F-04 | Practice admin | Set up practice | Licence, clinic profile, hours, invite receptionist. |  |
| 2 | F-11 | Practice admin | Add services & fees | INR catalogue, GST-exempt consults. |  |
| 3 | P-02 | Reception | Register first patient | Quick or full registration, ABHA optional. |  |
| 4 | P-04 | Reception | Book & check in | Slot booked, WhatsApp confirmation. |  |
| 5 | A-02 | Doctor | Consent & start recording | Patient agrees in Tamil; phone mic. |  |
| 6 | E-04 | Doctor | Branch: consent declined | Manual note, same sign flow. | ↪ |
| 7 | A-05 | Doctor | Review & sign note | Evidence-linked draft; sign. | 🔒 |
| 8 | P-06 | Reception | Invoice & UPI payment | Paid only on provider confirmation. |  |

## J2 · Returning diabetic patient — Meera Iyer · M1–M2

| # | Screen | Role | Step | What changes | Gate |
|---|---|---|---|---|---|
| 1 | D-01 | Doctor | Today → next patient | Meera in Room 3. |  |
| 2 | E-01 | Doctor | 60-second brief | Verified vs AI summary split. |  |
| 3 | A-03 | Doctor | Live consultation | Tamil, Hindi, English; 3 voices. |  |
| 4 | A-04 | Doctor | Confirm speakers | Patient and daughter confirmed. |  |
| 5 | A-05 | Doctor | Resolve conflicts & sign | Statin stopped vs chart active. | 🔒 |
| 6 | E-05 | Doctor | Authorise Rx & orders | Separate approvals. | 🔒 |
| 7 | E-10 | Doctor | Print / e-Rx to pharmacy | Generic names first; QR verify. |  |
| 8 | C-02 | Care coordinator | Care plan created | Owner Priya; reminder ≠ done. |  |

## J3 · Busy reception → nurse handoff — Front desk · M1–M2

| # | Screen | Role | Step | What changes | Gate |
|---|---|---|---|---|---|
| 1 | P-01 | Reception | Walk-in arrives | Reception board, token issued. |  |
| 2 | P-02 | Reception | Quick registration | 5 fields; completion task created. |  |
| 3 | P-03 | Reception | Branch: possible duplicate | Name + DOB match; request merge review. | ↪ |
| 4 | P-01 | Reception | Send to nurse | Card moves to “With nurse”. |  |
| 5 | N-01 | Nurse | Vitals & allergies | Open intake from the “With nurse” card; hand off returns to the board. |  |
| 6 | P-01 | Nurse | Ready for doctor | Doctor sees it on Today. |  |
| 7 | D-01 | Doctor | Clinic overview | Shared waiting room, reassign doctor. |  |

## J4 · Multilingual ward round · 3 speakers · clip — Hospital clinician · M3–M4

| # | Screen | Role | Step | What changes | Gate |
|---|---|---|---|---|---|
| 1 | W-01 | Doctor | Pair clip | Code match, mic self-test; clip then selectable at consent. |  |
| 2 | A-02 | Doctor | Bind encounter & consent | Patient + spouse consent; clip selected. |  |
| 3 | W-02 | System | Branch: offline on ward | Encrypted queue on clip; sync later. | ↪ |
| 4 | A-10 | Doctor | Overlap & unconfirmed speaker | Statements flagged, not attributed. | ↪ |
| 5 | A-04 | Doctor | Confirm 3 speakers | Doctor, patient, spouse. |  |
| 6 | A-05 | Doctor | Sign note | HIS is authority; write-back queued. | 🔒 |

## J5 · Delayed lab result escalation — Care loop · M2

| # | Screen | Role | Step | What changes | Gate |
|---|---|---|---|---|---|
| 1 | E-05 | Doctor | Order placed | State: Ordered. |  |
| 2 | C-05 | System | Report arrives, low-confidence match | Human confirms patient. | ↪ |
| 3 | C-01 | Doctor | Received ≠ reviewed | Critical K⁺ 6.1 sits unacknowledged. |  |
| 4 | C-04 | Care coordinator | Overdue escalation | Escalate to doctor after SLA. | ↪ |
| 5 | C-01 | Doctor | Mark reviewed | Acknowledgement with actor & time. | 🔒 |
| 6 | C-06 | Care coordinator | Contact patient | Message via portal; log outcome. |  |
| 7 | C-02 | Care coordinator | Close with evidence | Follow-up booked & attended. |  |

## J6 · Hospital with existing HIS · failed write-back — Enterprise · M4

| # | Screen | Role | Step | What changes | Gate |
|---|---|---|---|---|---|
| 1 | H-08 | Hospital IT | Set record authority | HIS is system of record. |  |
| 2 | A-05 | Doctor | Clinician signs note | Set Tweak authority = HIS; signed banner shows “Not yet in HIS”. | 🔒 |
| 3 | H-01 | System | HIS returns 503 ×3 | Chart shows "Not yet in HIS". | ↪ |
| 4 | H-01 | Hospital IT | Retry | Delivered with receipt. |  |
| 5 | H-04 | Doctor | Conflict to clinician | HIS med active vs Medvya stopped. | 🔒 ↪ |
| 6 | H-05 | Hospital IT | Inspect audit event | Append-only trail. |  |

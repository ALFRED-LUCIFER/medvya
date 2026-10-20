# Personas and first release

Source: PRD v2 §02, Foundations §05 sitemap, UI/UX prompt §2.

| Persona | Core loop | First usable release | Shell |
|---|---|---|---|
| Receptionist | Calendar → register / dedup → check-in → queue → invoice → call next patient | P1 (R1 Front Desk) | Front-desk shell; **never sees clinical notes** |
| Practice owner / admin | Settings, staff, schedules, services, reports, day-end close, subscription | P1 | Admin shell, single facility |
| Solo doctor | Today → chart → consultation → signed note and Rx → follow-up → billing | P2 (R2 Consult) | Doctor shell, own practice |
| Clinic doctor | Same loop inside a shared clinic with a shared waiting room | P2 | Doctor shell |
| Nurse | Rooming list → identity check → vitals and allergies → handoff → delegated tasks | P2 | Nurse console |
| Care coordinator | Results inbox, overdue tasks, contact outcomes, escalations | P4 (R4 Clinic Ops) | Care shell |
| Patient / caregiver | Book, consent, see released records and instructions, act as proxy | P4 | Portal shell, mobile-first, 17px min text, 48px targets |
| Medvya operations | Onboard tenants, run migrations, support with audited access | P5 (R5 v1.0 GA) | Platform ops console (N-12) |
| Hospital clinician | HIS-linked list, governed chart, write-back status | P7 (R7 Enterprise) | Doctor shell, facility scoped |
| Hospital IT / privacy officer | Facilities, SSO, integrations, audit, retention, data requests | P7 | Enterprise shell |

## Synthetic cast used across the designs

These names are used in fixtures, tests and demos. All are synthetic.

| Name | Role in designs |
|---|---|
| Dr. Arjun Rao | Owner and doctor, Iyer Family Clinic, Adyar, Chennai (TNMC 84213) |
| Meera Iyer, 54 F, MRN DQ-10-4482, DOB 12 Mar 1972 | Returning diabetic patient (J2); penicillin allergy; HbA1c 7.9 % |
| Kavya Iyer | Meera's daughter, companion speaker and caregiver proxy |
| A. Joseph | Receptionist |
| S. Thomas RN | Nurse |
| Priya N. | Care coordinator |
| Deepak Nair, 47 M | India registration example (ABHA, Kochi) |
| Fatima Al Mansoori, 38 F | UAE registration example (Emirates ID, Abu Dhabi, Thiqa) |
| Dr. Omar Haddad | UAE doctor (DoH GD-29918), Al Noor Family Clinic |
| Rahul Menon, 61 M | Critical potassium 6.1 result (J5) |
| R. Varghese | Hospital integration admin (Kauvery Group, synthetic) |
| Dr. S. Menon | ED doctor, break-glass example |

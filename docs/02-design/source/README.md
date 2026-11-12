# Design source files (imported, read-only)

These files were imported on **20 Oct 2026** from the Claude Design project **"DOQVI UI mockups and foundations"** (`016b9679-2f02-42ad-8b73-e8e5b35f2cde`) through the `claude_design` connector. They are `.dc.html` design canvases and need `support.js` (the design runtime) plus React from a CDN to render in a browser.

**Do not edit these files.** They are the record of what was designed. Changes belong in the design project, followed by a fresh import.

## Known import limitation

| File | Issue | Impact |
|---|---|---|
| `Medvya Clinician App.dc.html` | **Truncated at the connector's 256 KiB cap** (260,689 of ~261,400 bytes). The HTML template is complete. The data script stops part-way through the P-14 pre-authorisation data. | Screen layouts and labels for every clinician screen are present. The **sample data and interaction logic** for the tail of the script is missing: insurance list tail, devices, practice settings tabs (F-08 profile/hours/holidays, F-09 add doctor, F-10 add staff and roles, F-11 services, F-13 formulary), messages, referral, teleconsult, Rx print, nurse quick view, search and notifications. Tickets that depend on those details say so and carry lower confidence. **Action:** export that file from the design project directly (download) and replace this copy. |

All other files were imported complete (`truncated: false`).

## Contents

See [../docs/README.md](../../README.md#source-files-design-project) for what each file contains and how it was used.

## Revamp import, 6 Nov 2026

Added `Medvya Admin.dc.html` (platform console) and `Medvya Mobile.dc.html` (two Expo apps), and `Medvya Web.dc.html` (one-flow web design with the approved pricing). The design project now also holds a newer `Medvya Foundations.dc.html`; the older copy here is unchanged and a fresh import is still to do. Planned in tickets MDV-P5-031 to P5-050 and MDV-P4-034.

## Web re-import, 7 Nov 2026

`Medvya Web.dc.html` was replaced with the current version from the design project: it adds the public site pages (home, Medvya Assist, for hospitals, pricing, trust centre, book a demo), the sign-in and create-account screens and the six-step set-up wizard. Planned in MDV-P1-053 to P1-061 and MDV-P5-050.

## Foundations re-import, 10 Nov 2026 (MDV-P5-001)

`Medvya Foundations.dc.html` was replaced with the newer version from the design project (links to Medvya Web, records the 6 Nov approvals). `Medvya Admin`, `Medvya Mobile` and `Medvya Web` were compared with the design project and are byte-identical, so they were not re-imported. **Screens N-04 to N-14 are not in the design project yet** (no migration, data-quality, EID, exchange, extraction, letters, voice-console, plans, ops-console, help or export design), so MDV-P5-001 stays open and the dependent ticket confidence is unchanged.

## Admin, Web, Mobile and Foundations re-import, 10 Nov 2026 (MDV-P5-001)

All four design files differ from the copies imported earlier the same day (the design project was republished with the ADR-016 organisation-approval design) and were replaced. Changes: **Admin** "Verifications" (doctor licence checks) became "Approvals" (organisation queue, detail, decision, suspend and reinstate, reviewer and finance roles); **Web** adds the Clinic or Hospital onboarding request, setup-while-waiting, approval states, Organisation and approval settings and owner licence attestation; **Mobile** adds a waiting-for-approval page and suspended banner; **Foundations** renames F-04, adds a second F-07 (approvals queue), ORGANISATION and an approval state list. Screens N-04 to N-14 (apart from the console approvals) still have no designs. Details: `../screen-specs.md`.

## Web, Admin, Mobile and Foundations re-import, 12 Nov 2026 (MDV-P2-030)

All four files replaced from the design project. **Web:** the patient record is a one-page chart with four sample patients (adult with diabetes, pregnancy at 24 weeks, a 4-year-old, a 76-year-old), life-stage modules, a one-page consultation (guided steps kept as an option) and an Add medicine drawer with patient-aware safety checks; demo dates moved to Friday 06 Nov 2026. **Mobile:** Records › Trackers (BP and sugar, a grandchild's growth, a private cycle tracker), family list. **Admin:** switches for the new chart sections and trackers, pending clinical review, renamed approval applicants, Clinic plan figures. **Foundations:** D-06, D-07, D-08 and PT-09, the new entities (pregnancy, cycle log, growth, immunisation, milestones, geriatric assessment, medication request with checks) and the AI, child, pregnancy and older-adult colours. `support.js` is unchanged. Planned in epic MDV-P2-E07 (MDV-P2-030 to P2-045) and MDV-P5-051, P5-052. Details: `../screen-specs.md`.

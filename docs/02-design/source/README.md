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

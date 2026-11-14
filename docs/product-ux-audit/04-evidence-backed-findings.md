# 04 · Evidence-backed findings

This is the consolidated list. Each finding merges what the three independent reviewers and the lead arbiter found, and names the reviewer findings it came from (A = clinical workflow, B = visual and interaction, C = design system). The reviewers' own entries hold more detail: [reviewer A](reviews/reviewer-a-clinical-workflow.md), [reviewer B](reviews/reviewer-b-visual-interaction.md), [reviewer C](reviews/reviewer-c-design-system.md).

**Severity.** P0 safety or blocker · P1 critical workflow · P2 major usability · P3 polish. Nothing is P0 because it looks dated.

**Status words.** *Verified defect*: seen in a browser and reproducible. *Usability observation*: measured, a judgement that it hurts. *Hypothesis*: not proven. *Absent feature*: not built. *Observed* means seen in the running app; *inferred* means concluded from code or from a partial run.

**Environments.** *Azure* = `app-medvya-dev-3afs7i` (dev, synthetic data). *Local* = current `main` on the throwaway full stack. Screenshot paths are relative to `docs/product-ux-audit/screenshots/`. Viewports are emulated in desktop Chromium, not physical devices.

No clinician has reviewed any of this. Every safety rating is the auditors' and needs the pilot doctor's confirmation ([11-pilot-doctor-validation-plan.md](11-pilot-doctor-validation-plan.md)).

---

## P0 · safety

### F-01 · A medicine typed as free text is not checked against the patient's allergies

| | |
|---|---|
| Where | `/consult/:id`, Prescription · doctor · 1440 · open consultation · Local |
| Status | Verified defect, observed end to end: the line was authorised as RX-CAEE099F with "No alerts from the checks that could run", no reason asked, and the print shows "Allergies: Penicillin" directly above "Augmentin 625 Duo". Hypothesis: how often a pilot doctor will type brands |
| Evidence | `local-stack/walkthroughs/doctor/free-text-allergy/01-free-text-line-ready.png`, `02-after-authorise.png`, `03-print.png` (script `tools/walkthrough-doctor-5-freetext.mjs`); `local-stack/walkthroughs/doctor/consult-sign/03-free-text-augmentin.png` (the screen lists "Augmentin 625 Duo: typed as free text, so not checked for allergies, interactions or dose" under "Not checked"); `local-stack/walkthroughs/admin/settings/09-medicines.png` (23 generic rows, no brands) |
| Problem | For a patient with a recorded penicillin allergy, "Augmentin 625 Duo" (a penicillin-class brand) found no formulary match and was added with "Add as free text". The only signal is one line in a "Not checked" list, worded the same for every free-text medicine. The same screen blocks amoxicillin picked from the formulary |
| Consequence | A penicillin-class brand can reach a prescription for an allergic patient without a stop. Indian outpatient prescribing is largely by brand, so free text is likely on day one |
| Reproduce | Start a consultation for Meera Iyer (penicillin allergy) → Prescription → type a brand name → Add as free text |
| Remedy | 1. Reuse the existing "check could not run" severe alert: any free-text line, when an allergy is recorded or allergies are not recorded, needs a reason before authorising, with wording that names the allergy ("Not checked against: Penicillin"). 2. Load the pilot clinic's common brands, mapped to generics. 3. Offer generic matches for typed text |
| Depends on | Backend, frontend, clinical validation, product decision |
| Confidence | High on behaviour, medium on frequency |
| Validation owner | Pilot doctor |
| From | A-02 |

All three reviewers rate this a pilot blocker for prescribing. The pilot doctor confirms the wording of the fix. The honest "not checked" wording already there is good and must stay.

---

## P1 · critical workflow

### F-02 · The patient's name and allergy scroll out of view while the doctor prescribes

| | |
|---|---|
| Where | `/consult/:id` (doctor, 1440 and 1024), `/nurse/:id` (nurse, 1024 touch) · all states · Local and Azure |
| Status | Verified defect (observed, measured) |
| Evidence | `local-stack/walkthroughs/doctor/consult/05-consult-scrolled-1600.png`; `local-stack/walkthroughs/nurse/intake-tablet/05-vitals-flagged.png`; `azure-dev/owner-doctor/consult/1440-default-full.png` |
| Problem | The identity banner is not pinned. The consultation is 3,600 to 5,350 px tall and the prescription table starts near 4,200 px. The section chips and the sign bar are pinned; the patient is not. The project's own design rule says the banner is "fixed on every clinical screen" (`docs/02-design/tokens.md`) |
| Consequence | Wrong-patient and missed-allergy risk after an interruption. NISTIR 7804-1 asks that patient identification stay displayed in the same place regardless of scrolling (as read by reviewer A) |
| Reproduce | Start any consultation and scroll to Prescription |
| Remedy | Pin a one-row strip (name, age and sex, MRN, date of birth, allergy) under the app header on the consultation, its sub-pages and nurse intake. One banner component for all three places |
| Depends on | Frontend; clinical validation of what the strip shows |
| Confidence | High |
| Validation owner | Pilot doctor, nurse |
| From | A-01, B-03, C-02 |

### F-03 · After a quick visit to another page, the note shows old or empty text although the new text was saved

| | |
|---|---|
| Where | `/consult/:id` ↔ `/consult/:id/problems` · doctor · 1440 · Local |
| Status | Verified defect (observed, 2 of 2 runs). The cause is a hypothesis |
| Evidence | `local-stack/walkthroughs/doctor/consult-2/02-after-quick-route-change.png`; `reviews/reviewer-a-evidence/walkthrough-a.log` |
| Problem | Type in Assessment, open "Problems and diagnoses" within about two seconds, come back: the field shows the cached text. A reload shows the server has the new text |
| Consequence | The doctor retypes, or reviews a note that differs from what will be signed. Diagnoses and reconciliation live on other pages, so doctors leave the note mid-sentence often |
| Reproduce | As above |
| Remedy | Refetch the note when the consultation mounts and write the flushed text into the cache on leaving. Keeping diagnoses inline (F-11) removes the trigger |
| Depends on | Frontend |
| Confidence | High on the symptom |
| Validation owner | Engineering |
| From | A-03 |

### F-04 · "End consultation" is one click, even with a prescription that was never authorised

| | |
|---|---|
| Where | `/consult/:id`, then `/today` · doctor · 1440 · Local |
| Status | Verified defect (observed) |
| Evidence | `local-stack/walkthroughs/doctor/consult-2/12-ended-with-draft-rx.png`, `13-rx-after-end.png`, `14-today-notes-to-sign.png`; `azure-dev/owner-doctor/consult/1440-default-full.png` ("The draft prescription can be read, not changed") |
| Problem | No dialog. A draft prescription or unplaced orders are left read-only for ever. Today lists notes to sign and says nothing about prescriptions |
| Consequence | The patient leaves without a valid prescription and nobody is told |
| Reproduce | Add a medicine, do not authorise, press End consultation |
| Remedy | On End and on Sign, if a draft exists ask: authorise now, discard, or end anyway. Add "prescriptions to authorise" to Today. Allow a draft to be finished after the visit ended |
| Depends on | Frontend, backend, product decision |
| Confidence | High |
| Validation owner | Pilot doctor |
| From | A-04 |

### F-05 · A visit can only move forward; the desk can finish it without the doctor; nothing can be undone

| | |
|---|---|
| Where | `/front-desk` cards · receptionist, nurse, doctor · 1440 · Local |
| Status | Verified defect (no undo). Usability observation (who may close) |
| Evidence | `local-stack/walkthroughs/receptionist/board-checkin-callin/04-call-in.png`, `06-desk-closed-consult.png`; `local-stack/walkthroughs/handoff/desk-closes-mid-consult/01-desk-pressed-consult-done.png` |
| Problem | "Call in" and "Consult done" are single taps with no confirmation. The desk closed a visit while the doctor's consultation stayed open. There is no undo, no cancel after check-in and no "left without being seen" |
| Consequence | One mis-tap is permanent. The queue display calls the wrong token and visit counts are wrong. A walk-in who leaves is recorded as seen |
| Reproduce | Board → a Ready for doctor card → Call in → Consult done |
| Remedy | A time-limited Undo for the last move, "Left without being seen" and "Cancel visit" with a reason. When a doctor uses the consultation screen, let In consult and Done follow the doctor's Start and End |
| Depends on | Backend, frontend, product decision |
| Confidence | High |
| Validation owner | Receptionist, owner |
| From | A-06 |

### F-06 · The follow-up the doctor sets never reaches the front desk

| | |
|---|---|
| Where | Consultation → Plan → Follow-up (doctor); board, desk record, schedule (receptionist) · Local |
| Status | Verified defect against ticket MDV-P2-043's acceptance line |
| Evidence | `local-stack/walkthroughs/doctor/consult-2/05-follow-up.png`; `local-stack/walkthroughs/handoff/follow-up-at-desk/01-board.png`, `02-patient-record.png` |
| Problem | The doctor sees "The front desk will book it from Tuesday, 17 November 2026." The desk sees nothing |
| Consequence | A promise on screen that does not happen. Chronic-care patients are not rebooked unless they ask |
| Reproduce | Set a follow-up, end the visit, sign in as the receptionist |
| Remedy | Show "Follow-up due from 17 Nov · Book" on the Done card and the desk record, opening the booking panel on that date |
| Depends on | Frontend, possibly backend |
| Confidence | High for the desk screens opened |
| Validation owner | Receptionist, pilot doctor |
| From | A-07 |

### F-07 · A nurse's escalation does not move the patient up the doctor's list, and Today shows no tokens

| | |
|---|---|
| Where | `/today` · doctor · 1440 · Local |
| Status | Verified defect (ordering). Usability observation (prominence) |
| Evidence | `local-stack/walkthroughs/doctor/consult/01-today.png`, `02-today-full.png` |
| Problem | A 72-year-old escalated for fever and BP 168/96 was row 11 of 11, below the first screen. Rows sort by slot time, and a walk-in holds a late nominal slot. Today shows no token numbers, while patients and the waiting-room display think in tokens |
| Consequence | An urgent patient can wait behind ten others. The doctor's order differs from the order patients believe in |
| Reproduce | Run `tools/walkthrough-handoff.mjs`, open Today |
| Remedy | Pin escalated visits above "Next patient" with the reason. Sort arrived patients by the clinic's chosen rule and show the token on every row |
| Depends on | Frontend, backend, product decision (queue rule) |
| Confidence | High |
| Validation owner | Pilot doctor, nurse |
| From | A-08 |

### F-08 · Two patients with the same name look identical when picking one

| | |
|---|---|
| Where | Walk-in, booking and waitlist pickers (receptionist); header search (doctor) · 1440 · Local, and Azure for the header search |
| Status | Verified defect (what the rows show). The wrong pick is a usability observation |
| Evidence | `local-stack/walkthroughs/receptionist/repeat-by-phone/01-search-4-digits.png`; `local-stack/walkthroughs/doctor/misc/02-global-search.png`; `azure-dev/owner-doctor/global-search/1440-results.png` |
| Problem | Rows show age, sex and masked MRN and phone. No date of birth, no last visit, no "possible duplicate" mark. Two "Deepak Nair, 47 y, Male" rows differed only in the last four MRN digits |
| Consequence | Visits, vitals and allergies split across two charts |
| Reproduce | Walk-in → type 0102 |
| Remedy | Date of birth and last visit on every picker row. Mark records with an open merge request and sort the main record first |
| Depends on | Frontend, backend (last visit), product decision on masking |
| Confidence | High |
| Validation owner | Receptionist, pilot doctor |
| From | A-05 |

### F-09 · The signing checklist says "Identity confirmed" before anyone confirmed anything

| | |
|---|---|
| Where | Consultation → Checklist · doctor · Local |
| Status | Verified defect (observed, and in `checklist.ts`) |
| Evidence | `local-stack/walkthroughs/doctor/consult-2/01-open-allergies-unknown.png` |
| Problem | "Identity confirmed (name and date of birth) · Done" on opening. In code the item means the banner was displayed. "Every AI suggestion accepted or dismissed · Done" shows although no AI drafting exists |
| Consequence | The record of what was checked before signing overstates what happened |
| Reproduce | Open any consultation and open Checklist |
| Remedy | Reword to "Patient shown: name, date of birth", or add a one-tap "Confirmed with the patient". Hide the AI line until AI drafting exists |
| Depends on | Product decision, frontend, clinical validation |
| Confidence | High |
| Validation owner | Pilot doctor |
| From | A-10 |

### F-10 · The pinned "Review and sign" bar covers the form

| | |
|---|---|
| Where | `/consult/:id` · doctor · all widths · Local and Azure |
| Status | Verified defect (observed). WCAG 2.2 SC 2.4.11 Focus Not Obscured (Minimum) is at risk: reviewer B found 2 of 45 tab stops fully hidden |
| Evidence | `azure-dev/owner-doctor/consult/1920-default.png` (the bar sits over section 3's template field); `local-stack/states/` focus captures (reviewer B, finding B-01) |
| Problem | The bar is about 100 px tall, floats over the page and no space is reserved for it |
| Consequence | Fields and the keyboard focus disappear under it. At 400 % zoom the pinned bars take about half the screen |
| Reproduce | Open a consultation at 1920×1080 and look at the bottom of the first screen; or tab through the note |
| Remedy | Make the bar part of the layout (a footer row), or reserve bottom padding equal to its height and add `scroll-padding-bottom` |
| Depends on | Frontend |
| Confidence | High |
| Validation owner | Engineering |
| From | B-01 |

### F-11 · The consultation is one very long column; diagnoses and medicines are on other pages

| | |
|---|---|
| Where | `/consult/:id` · doctor · 1440×900 and 1024×768 · Local and Azure |
| Status | Usability observation, measured |
| Evidence | `local-stack/doctor/consult/1440-default-full.png`, `1024-default-full.png`, `375-default-full.png`; `local-stack/walkthroughs/doctor/consult/04-consult-full.png`; `evidence/local-metrics.jsonl` |
| Problem | Page height 3,611 px at 1440, 4,093 at 1024, 4,941 at 375 (ended consultation), and up to 5,347 px for an open one. The first section card starts at 440 px; with nurse vitals present the first note field is at 1,138 px. At 1024 the Chart and Checklist panel drops to the bottom of the page. Prescription sits below the note, outside the numbered sections. One simple visit took about 24 clicks, 9 typed fields and 5 route changes |
| Consequence | Scrolling on every patient. On a tablet the chart is far from where the doctor types. Route changes trigger F-03 |
| Reproduce | Open a consultation at 1024×768 |
| Remedy | A workspace: pinned patient strip, the note as one scrolling column with section anchors, prescription directly after assessment, diagnosis added inline, the chart panel always reachable (side panel on desktop, drawer on tablet), a footer that is part of the layout. See the concept |
| Depends on | Frontend, design, clinical validation of section order |
| Confidence | High on measurements |
| Validation owner | Pilot doctor |
| From | A-13, B-04, C-10 |

### F-12 · On the reception board the first patient appears halfway down the screen; on a phone it does not work as a board

| | |
|---|---|
| Where | `/front-desk` · receptionist, doctor · 768 to 1440, and 375 · Local and Azure |
| Status | Usability observation, measured |
| Evidence | `local-stack/doctor/front-desk/1024-default.png`, `375-default.png`; `local-stack/receptionist/front-desk/1440-default.png`; reviewer B measurements (B-02, B-06) |
| Problem | Label, title, time-zone line, view switch, status line, three buttons and the doctor-workload card come before the columns. The first card starts at about 452 px at every width from 768 to 1440; 41 % of a 1024×768 screen is left for patients. At 375 the board is six 156 px columns in a sideways scroller with the first card at about 664 px |
| Consequence | The desk scrolls to see the queue it runs. A doctor checking the board on a phone sees headings |
| Reproduce | Open the board at 1024×768 with nine visits |
| Remedy | One 44 px header row, a one-line workload strip, columns whose headers stay visible. Below 768 px, one list with status tabs. The concept goes further and replaces the columns with a grouped list; that step needs a receptionist's view first (see [07](07-debate-and-arbitration.md)) |
| Depends on | Frontend, design |
| Confidence | High |
| Validation owner | Receptionist |
| From | B-02, B-06, C-23 |

---

## P2 · major usability

### F-13 · The Azure dev app is behind `main`: its header scrolls sideways at 768 and 375 and hides Sign out

| | |
|---|---|
| Where | Every signed-in page · both roles · 768 and 375 · Azure only |
| Status | Verified defect on Azure (page width 939 px at 768, 511 px at 375). Local `main`: no sideways overflow on any of 615 captured pages |
| Evidence | `azure-dev/owner-doctor/today/768-default.png`, `375-default.png`; `local-stack/doctor/front-desk/375-default.png` |
| Consequence | Whoever tests the pilot on a tablet today meets a broken header |
| Remedy | Deploy current `main` to Azure dev and capture again before the pilot |
| Depends on | Engineering (a tag deploy) |
| Confidence | High · Validation owner: engineering |
| From | Lead arbiter, A-27 |

### F-14 · Board cards do not name the doctor; names and buttons wrap; the time spills out of the card

| | |
|---|---|
| Where | `/front-desk` By status · all roles · 1024 to 1440 · Local |
| Status | Verified defect |
| Evidence | `local-stack/doctor/front-desk/1024-default.png`; `local-stack/walkthroughs/receptionist/walkin-new/07-board-after.png` |
| Problem | With two doctors the desk must guess from the token letter, which is wrong after a reassignment. Columns are about 150 px wide at 1024, so "Start consultation" takes two lines and the time sits 9 to 18 px outside the card |
| Remedy | Doctor's short name on the card when more than one doctor works. Wider minimum column, short action labels, empty columns collapsed |
| Depends on | Frontend · Confidence: high · Validation owner: receptionist |
| From | A-11, B-05 |

### F-15 · Tokens cannot be printed, reissued or cancelled, and the order shown is not the order of the numbers

| | |
|---|---|
| Where | Walk-in success, board, overview, queue display · Local |
| Status | Absent feature (print, reissue, cancel; no ticket found). Usability observation (order) |
| Evidence | `local-stack/walkthroughs/admin/settings/queue-tv-1920.png` (order A-05, B-02, A-06, A-09, A-07, A-08); `local-stack/walkthroughs/receptionist/walkin-new/06-checked-in-token.png` |
| What works | A token is issued at check-in, unique per clinic, day and letter, and is kept on reassignment (reviewer A, section 3.1) |
| Consequence | A patient holding A-07 watches A-09 go first and comes to the desk |
| Remedy | Agree the queue rule with the pilot clinic and make the display, the board and Today follow it. A small printable slip and "Reprint" |
| Depends on | Product decision, frontend, backend · Confidence: high · Validation owner: receptionist, owner |
| From | A-12 |

### F-16 · The nurse's chief complaint is not shown to the doctor

| | |
|---|---|
| Where | `/nurse/:id` → `/consult/:id` · Local |
| Status | Verified defect that it is not shown. Whether it is stored: untested |
| Evidence | `reviews/reviewer-a-evidence/walkthrough-a.log` |
| Remedy | Prefill Chief complaint, marked "from nurse intake", editable |
| Depends on | Frontend, maybe backend · Confidence: medium-high · Validation owner: nurse, pilot doctor |
| From | A-09 |

### F-17 · A screen the role may not open says "Page not found. The link may be old."

| | |
|---|---|
| Where | Receptionist `/today`, `/settings`, chart, consultation; nurse `/settings`; admin chart · Local and Azure |
| Status | Usability observation (the server refuses correctly). Severity P3 |
| Evidence | `azure-dev/receptionist/today-denied/1440-default.png`; `local-stack/receptionist/consult-denied/1440-default.png` |
| Remedy | "Your role does not include this screen", with the start-page button. The nurse on `/patients/new` already gets such a message |
| Depends on | Frontend · Confidence: high · Validation owner: engineering |
| From | Lead arbiter, A-21 |

### F-18 · The top bar is a row of permanent buttons: Language, My devices, Sign out

| | |
|---|---|
| Where | Every signed-in page · Azure (five controls plus Change facility) and Local (three) |
| Status | Usability observation |
| Evidence | `azure-dev/owner-doctor/today/1920-default.png`; `local-stack/doctor/today/1024-default.png` |
| Problem | Controls used once a day or less hold the most valuable row, beside patient search. Sign out sits next to Search. This row is what overflowed in F-13 |
| Remedy | One account menu (name and role, language, my devices, change facility, sign out). Top bar 48 px |
| Depends on | Frontend (add the shadcn dropdown menu) · Confidence: high · Validation owner: engineering |
| From | B-07, C-08 |

### F-19 · The same pattern is built several times and looks different each time

| | |
|---|---|
| Where | System-wide, code |
| Status | Verified (code counts by reviewer C) |
| Evidence | Reviewer C section 2.2: 3 patient identity headers, 10 copies of `Message`, 5 `Field`, 97 inline card class strings, about 12 chip variants, 20 hand-built view switches in 5 styles |
| Consequence | The allergy shows as a red pill on the chart and as plain text in the consultation. Every new screen drifts further |
| Remedy | Move the patterns into `packages/ui`: PatientBanner, Field, Chip, Card, PageMessage, SegmentedControl. See [08](08-design-system-blueprint.md) |
| Depends on | Frontend · Confidence: high · Validation owner: engineering; pilot doctor for the allergy presentation |
| From | B-08, B-09, C-02, C-04 |

### F-20 · Library defaults are wrong, so every screen patches them; clinical status colours bypass the tokens

| | |
|---|---|
| Where | `packages/ui` and call sites |
| Status | Verified (code) |
| Evidence | Reviewer C: Input and Select default to 36 px and 164 call sites patch them to 44 px; 188 Buttons repeat a redundant minimum height; 103 raw hex colours in `packages/ui/src/primitives.tsx` |
| Consequence | A missed patch gives a small control (seen in the platform console). Dark and high-contrast themes cannot be built on raw colours |
| Remedy | Fix the defaults, delete the overrides by codemod, add a tone layer to the tokens |
| Depends on | Frontend · Confidence: high · Validation owner: engineering |
| From | C-01, C-03 |

### F-21 · Type and line length drift from the project's own spec

| | |
|---|---|
| Where | System-wide · Local, 1440 |
| Status | Verified (measured) |
| Evidence | `evidence/local-metrics.jsonl`: 12 rendered font sizes at 1440 against a documented scale of 7; 14 px is 65 % of text (spec: 15 px, weight 500). Paragraphs up to 1,160 px wide on the consultation and chart. Reviewer C: no maximum width on fluid pages |
| Consequence | Weight and size stop ranking anything; long lines are slow to read |
| Remedy | Name the scale the app really renders (9 tokens), remove the strays, cap fluid pages at 1,600 px and reading text at about 72 characters |
| Depends on | Frontend · Confidence: high · Validation owner: engineering |
| From | Lead arbiter, B-11, C-05, C-16 |

### F-22 · From 768 to 1279 px the navigation is icons only, with no labels or tooltips

| | |
|---|---|
| Where | App shell · all roles · tablets and small laptops · Local and Azure |
| Status | Verified |
| Evidence | `local-stack/doctor/today/1024-default.png`; `azure-dev/owner-doctor/today/768-default.png` |
| Remedy | An 11 px label under each icon in the 72 px rail, or the labelled sidebar from 1024 px |
| Depends on | Frontend · Confidence: high · Validation owner: receptionist (first-day test) |
| From | B-12, C-09 |

### F-23 · After a failed submit, focus stays on the button and there is no summary of errors

| | |
|---|---|
| Where | `/patients/new` and other forms · Local |
| Status | Verified defect (reviewer B state capture). Relates to WCAG 2.2 SC 3.3.1 Error Identification and 3.3.3 Error Suggestion |
| Evidence | `local-stack/states/` registration captures (B-13); reviewer C section 2.5: three error styles, three required/optional conventions |
| Problem | At 375 the errors are off screen after an empty submit |
| Remedy | An error summary that takes focus and links to each field; one Field component |
| Depends on | Frontend · Confidence: high · Validation owner: engineering, receptionist |
| From | B-13, C-12 |

### F-24 · Tables show few rows, mostly cannot be sorted, and scroll sideways on phones

| | |
|---|---|
| Where | Staff, audit log, directory, formulary · Local |
| Status | Verified (code and screenshots) |
| Evidence | Reviewer C: staff rows about 69 px (7 per screen), audit table starts about 800 px down, 2 of 19 tables sortable, 10 rely on horizontal scroll, 4 scroll regions have no label. `local-stack/doctor/settings-audit/1440-default-full.png` (4,192 px) |
| Remedy | A compact density for pointer-only reference tables, shared sort and paging, stacked rows below 768 px |
| Depends on | Frontend · Confidence: high · Validation owner: owner, engineering |
| From | C-06, C-13 |

### F-25 · After the identity step-up, the doctor lands at the top of the page and must find the button again

| | |
|---|---|
| Where | Authorise prescription → identity provider → back · Local (dev provider). Real Entra: untested |
| Status | Verified defect |
| Evidence | `reviews/reviewer-a-evidence/walkthrough-a.log` ("back from step-up: scrollY=0 rx in view=false") |
| Remedy | Return to the section that asked and complete the pending action, or focus its button with "Identity confirmed. Press Authorise." |
| Depends on | Frontend · Confidence: high locally · Validation owner: engineering, pilot doctor |
| From | A-14 |

### F-26 · A reload within about two seconds of typing loses the last edit

| | |
|---|---|
| Where | Consultation note · Local |
| Status | Verified defect (one reproduction) |
| Remedy | Flush on `pagehide` with `keepalive`; keep a local unsent draft per note (inside the limits ADR-011 and AGENTS.md 1.2 set for patient data in browser storage: this needs a decision) |
| Depends on | Frontend, security decision · Confidence: medium-high · Validation owner: engineering |
| From | A-15 |

### F-27 · The signed note's print view omits vitals, allergies, diagnosis and medicines, and has no heading

| | |
|---|---|
| Where | `/consult/:id/note/print` · Local |
| Status | Usability observation needing clinical validation. The missing `h1` is a verified axe finding (`page-has-heading-one`, moderate, 5 of 5 widths) |
| Evidence | `local-stack/walkthroughs/doctor/consult-sign/14-note-print.png`; `evidence/local-metrics.jsonl` |
| Remedy | Ask the pilot doctor what the visit record must hold; add vitals, allergies and diagnoses by default; add the page heading |
| Depends on | Clinical validation, backend · Confidence: medium · Validation owner: pilot doctor |
| From | A-16, lead arbiter |

### F-28 · Nurse intake needs a tap per flagged value and allows no correction; with intake off the nurse has no clinical screen

| | |
|---|---|
| Where | `/nurse/:id` at 1024 touch; nurse role with the setting off · Local |
| Status | Usability observation |
| Evidence | `local-stack/walkthroughs/nurse/intake-tablet/05-vitals-flagged.png`, `09-intake-reopened-after-handoff.png`; `local-stack/nurse/today/1440-default.png`; `local-stack/nurse/consult/1440-default.png` (two 403 answers) |
| Remedy | One "I re-checked these readings" for all flags. A correction with a reason until the doctor starts. Decide whether a clinic with a nurse and no intake step is real |
| Depends on | Clinical validation (vitals ranges await sign-off, issue #956), product decision · Confidence: medium · Validation owner: nurse, owner |
| From | A-18, A-20 |

### F-29 · The owner cannot see the business, and one pilot metric always reads "0 s, target met"

| | |
|---|---|
| Where | Settings → Practice → Pilot metrics; board "By doctor" · Local and Azure |
| Status | Absent feature, planned for phase 4 (owner dashboard). Verified defect (the check-in metric) |
| Evidence | `azure-dev/owner-doctor/settings-metrics/1440-default-full.png`; `local-stack/doctor/settings-metrics/1440-default-full.png` |
| Problem | Four 14-day numbers inside Settings. Nothing on visits per day, waits, consult length or money. A walk-in is created and checked in by one action, so its timed check-in is zero |
| What is good | Missing data says "No data yet", not zero |
| Remedy | Time check-in from opening Walk-in to the token. One "Clinic today" page from data that exists (see the concept); revenue stays "not tracked" until billing is on |
| Depends on | Backend, product decision · Confidence: high · Validation owner: owner |
| From | A-19 |

### F-30 · Staff: "Offboard" is offered on the owner; a role cannot be changed; Invite is reachable only by URL

| | |
|---|---|
| Where | Settings → People → Staff; `/admin/staff` · practice admin and owner · Local |
| Status | Hypothesis (the offboarding was not submitted; the code refuses only the last administrator). Absent feature (change role). Verified (URL-only route) |
| Evidence | `local-stack/walkthroughs/admin/settings/02-offboard-dr-arjun-rao.png` |
| Remedy | Only an owner offboards an owner; step-up on any offboarding; "Change role"; link or remove `/admin/staff` |
| Depends on | Product decision, backend · Confidence: medium · Validation owner: owner, engineering |
| From | A-23, C-14, C-15 |

### F-31 · The audit log mixes plain names with developer codes and fills with routine reads

| | |
|---|---|
| Where | Settings → Security → Audit log · Local and Azure |
| Status | Verified |
| Evidence | `azure-dev/owner-doctor/settings-audit/1440-default-full.png` |
| Remedy | Name every action; default to "opened or changed a chart" |
| Depends on | Frontend, translations · Confidence: high · Validation owner: owner |
| From | A-24, C-24 |

### F-32 · One word names several screens, and one screen has several names

| | |
|---|---|
| Where | Navigation and page titles · system-wide |
| Status | Verified |
| Problem | "Today" is the doctor's day for doctors and the reception board for nurses. The reception surface is "Front desk", "Reception board" and "Clinic overview". A patient has a "record", a "Patient 360" and a "directory" entry |
| Remedy | "My day" for doctors; "Front desk" everywhere for the desk; one "Patient record" with tabs by capability; Register patient becomes a button, not a menu item |
| Depends on | Frontend, translations in three languages, product decision · Confidence: high · Validation owner: receptionist, pilot doctor |
| From | C-07, A-20 |

### F-33 · Some controls are far below the project's 44 px rule

| | |
|---|---|
| Where | Switches (32×18), native radios and checkboxes (16×16), patient-name links (19 px tall) · touch widths · Local |
| Status | Verified by metric; whether a larger label extends the hit area was not checked for each (WCAG 2.2 SC 2.5.8 sets 24×24 with exceptions) |
| Evidence | `evidence/local-metrics.jsonl`: about 10 % of controls under 44 px and 6 % under 24 px at 375, 768 and 1024 |
| Remedy | Enlarge switch and radio hit areas; make the whole row the target for name links |
| Depends on | Frontend · Confidence: medium · Validation owner: engineering |
| From | Lead arbiter, A-17, B |

### F-34 · The mobile apps cannot be started on this machine and hold development-only data

| | |
|---|---|
| Where | `mobile/clinician`, `mobile/patient` |
| Status | Verified defect (developer experience): `pnpm dev:clinician` then opening in Expo Go fails with "Cannot find module '@babel/helper-define-polyfill-provider'". By design the apps read a fixed synthetic cast until MDV-P5-033 |
| Evidence | `mobile-expo/clinician/375-bundle-error.png`; reviewer C finding C-20 |
| Consequence | No device evidence for mobile in this audit. Not a pilot blocker: the pilot runs on the web app |
| Remedy | Fix the dependency resolution; then run the same audit on a simulator |
| Depends on | Dependency change · Confidence: high · Validation owner: engineering |
| From | Lead arbiter, C-20, C-21 |

Follow-up (14 Nov, MDV-P5-053): the error did not reproduce on a clean install, and the simulator audit is in [13-mobile-simulator-audit.md](13-mobile-simulator-audit.md).

### F-35 · The schedule's day view fills with slots that have passed

| | |
|---|---|
| Where | `/schedule` Day · Azure and Local · 1440, 1024 |
| Status | Usability observation |
| Evidence | `azure-dev/owner-doctor/schedule/1440-default.png` (every visible row is "Passed"); reviewer B: 38 % of a 1024×768 screen is working content |
| Remedy | Open the day at the current time, collapse the past into one row, and let one doctor's column leave room for the booking panel |
| Depends on | Frontend · Confidence: high · Validation owner: receptionist |
| From | Lead arbiter, B |

---

## P3 · polish

| ID | Finding | Where and evidence | Status | Remedy | Owner | From |
|---|---|---|---|---|---|---|
| F-36 | Pages show a "Loading…" card, then the layout appears and the height jumps | All list pages · reviewer B section 5; load times in [05](05-accessibility-responsive-and-reliability.md) | Usability observation | Skeletons in the final layout | Engineering | B |
| F-37 | Walk-in success names the wrong column when nurse intake is off | `local-stack/walkthroughs/receptionist/walkin-new/06-checked-in-token.png` | Verified defect | Name the real column | Engineering | A-22 |
| F-38 | Settings → Terms says booking is paused while booking works | Local | Hypothesis | Check on Azure | Engineering | A-26 |
| F-39 | Switching nurse intake has no confirmation and applies mid-day | Local | Usability observation | Confirm, and say what happens to patients with the nurse | Owner | A-25 |
| F-40 | `/organisation` for an approved clinic calls an endpoint that answers 404 and shows an empty request | Azure and Local · `azure-dev/owner-doctor/organisation/1440-default.png` | Verified | Show "Approved" or hide the page | Engineering | Lead arbiter |
| F-41 | The clinic picker ignored the first click once in three tries | Azure `/choose-facility` | Hypothesis (not reproduced) | Watch for it in the pilot | Engineering | Lead arbiter |
| F-42 | Dark mode and high contrast are documented and not built; native date inputs ignore the app language | Code | Verified | After the tone tokens | Engineering | C-18, C-19 |
| F-43 | Settings has two tab rows in two styles | `azure-dev/owner-doctor/settings-metrics/1440-default-full.png` | Usability observation | A left sub-navigation at 1024 px and wider | Owner | C-17 |
| F-44 | Platform console: sign-in overflows at 375 px, 30 px filter tabs (mocked API only) | `platform-admin-mocked/` | Verified in a mock | Fix with the library defaults | Engineering | C-22 |
| F-45 | No written product or design brief (`PRODUCT.md`, `DESIGN.md`) | Repository | Verified | Write them after the direction is agreed | Founder | B |
| F-46 | Azure synthetic patients "Test Patient 2" lack the "(synthetic)" marker the project rule asks for | `azure-dev/owner-doctor/today/1920-default.png` | Verified | Rename or reseed | Engineering | Lead arbiter |
| F-47 | A few transitions use `transition-all`; the focus ring fades in | `packages/ui` switch, tabs, button | Verified (code) | Name the properties | Engineering | B |

---

## What is verified as good

These were measured, not assumed, and a redesign must not break them.

- **Accessibility floor.** axe found no violation on 610 of 615 captured pages on `main`; the 5 hits are one missing heading on the note print view. Text contrast passed wherever reviewer B computed it, except disabled labels. Status never relies on colour alone. Keyboard focus is visible, the skip link works, and focus returns to the search button after the search dialog closes (checked on Azure).
- **No sideways overflow** on any of 615 pages on `main`, at any of five widths.
- **The walk-in path.** New patient to token in 6 clicks and 3 typed fields with no scrolling; a returning patient in 3 clicks.
- **Allergy handling.** "Not recorded" is never shown as "none"; amoxicillin for a penicillin-allergic patient is blocked in the dialog and at authorisation, with a logged override.
- **Duplicate prevention** at registration, **note conflict detection** between two tabs, **signing** with identity restated and addenda as new versions.
- **The waiting-room display** shows tokens only.
- **Server-side refusals** for every role tested.
- **Honest empty states.** "No data yet" and "Coming in a later phase", not invented zeros.
- **Foundations.** Token pipeline, layered front end enforced by lint, three languages with right-to-left, contrast and axe tests in CI.

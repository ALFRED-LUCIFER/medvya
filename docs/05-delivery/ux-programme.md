# UX programme (epic MDV-P1-E12)

**Epic.** UX programme: safe, cohesive clinic workspace, from the 13 Nov 2026 product UX audit ([docs/product-ux-audit](../product-ux-audit/00-executive-verdict.md)). Source of the tickets: `docs/05-delivery/tickets/_source/P1.tickets`, `P2.tickets`, `P5.tickets` (key MDV-P1-E12). The same text is the body of the epic issue.

## Decision

Option B, cohesive modernisation, delivered safety-first ([09](../product-ux-audit/09-redesign-options-and-recommendation.md), arbitration [07](../product-ux-audit/07-debate-and-arbitration.md)). Keep the brand, the routes and the logic. Close the safety and trust gaps as small separate changes; do three system changes and one bounded consultation layout pass before the pilot; leave the rest of the design-system work until after the first pilot weeks. No reset anywhere. Two tracks run in parallel: **Track D** (design rules and UI component setup, started first) and **Track S** (safety, trust and handoff behaviour, from day one). The audit's own ids (UX-01 to UX-35) are mapped to MDV keys below; they are not used as keys.

52 tickets: Track D 32 (D0 2, D1 10, D2 8, D3 12), Track S 19 (S1 5, S2 7, S3 7), people gate 1.

## Non-negotiables (from the arbitration)

1. The patient strip is pinned on every clinical write screen, with the allergy in words, never clipped or collapsed to a colour.
2. Sign the note, authorise the prescription, place orders and end the consultation stay four separate, recorded acts. One identity step-up may cover them.
3. The walk-in path must not get longer: at most 6 clicks for a new patient and 3 for a returning one. Re-measure after every desk change.
4. No tick, Saved, or number the system did not earn.
5. Every control that changes state is comfortable, 44 px (48 px rows on a coarse pointer). Compact rows only on the audit log, formulary, order sets and staff tables.
6. No palette or typeface change.
7. No second component library, no new state library.

## Do not change without clinical validation

Allergy rules, wording and the unknown, none and not recorded distinction; the Add medicine dialog and the safety-check wording (the dialog stays until the free-text rule is closed and a clinician has seen a replacement; no inline free-text medicine box); the sign dialog and the separation of sign, authorise and orders; the duplicate block at registration and the identity confirmation at check-in; the walk-in path; consultation section order and required fields; vitals ranges and flags; what a receptionist can see of clinical data; the palette and the typeface; the board stays for the pilot (the list is a third view later); renames land at a training boundary; the phone blocks prescribing, authorising, reconciliation, orders and signing a new note; the owner page shows only the five trustworthy counts and the needs-attention list; the rail is labelled, never icon-only by default; the queue rail shows tokens and ages only.

## Colour legend

| Label | Colour | Meaning |
|---|---|---|
| `epic: ux programme` | violet `6E56CF` | Every ticket of this programme and the epic itself |
| `track: design` | blue `1F6FEB` | Track D: design rules and UI component setup |
| `track: safety` | red `D1242F` | Track S: safety, trust and handoff behaviour |
| `wave: D0` | dark blue `0A3069` | Design rules |
| `wave: D1` | blue `0969DA` | Component setup in packages/ui |
| `wave: D2` | light blue `54AEFF` | Screens rebuilt on the new components |
| `wave: D3` | grey `6E7781` | After the first pilot weeks (later) |
| `wave: S1` | dark red `A40E26` | Safety blockers |
| `wave: S2` | orange `E16F24` | Desk and handoff |
| `wave: S3` | light grey `8C959F` | Later trust and handoff (later) |
| `needs: clinical sign-off` | amber `BF8700` | Must not merge before the pilot doctor or clinician has agreed, dated on the issue |

Existing labels keep their colours: `phase:` blue `1D76DB`, `type:` purple `5319E7`, `priority:` red `B60205`, `status:` green `0E8A16`, area labels `C5DEF5`, `epic` yellow `FBCA04`.

## Tracks and waves, every ticket in order

**Track D · Wave D0 · design rules**

- MDV-P1-075 (#1171) Design rules: write PRODUCT.md and DESIGN.md from the audit blueprint and amend AGENTS.md section 4
- MDV-P1-076 (#1172) Guardrails: lint rules and a CI count that may only fall, so shared patterns stop drifting

**Track D · Wave D1 · component setup in packages/ui**

- a MDV-P1-077 (#1173) Library defaults: 44 px Input, Select and Textarea, a required Table label, and 44 px hit areas for Switch
- a MDV-P1-078 (#1174) Codemod: delete about 350 redundant control-height patches (merges alone), then give radios, checkboxes and name-link rows a 44 px target
- a MDV-P1-079 (#1175) Tokens: nine type sizes, the spacing scale, page and panel widths, the 1024 tier, density and two motion values
- a MDV-P1-080 (#1176) Tone tokens: one colour pair per meaning; no raw hex left in the shared library; the contrast test reads the tokens
- b MDV-P1-081 (#1177) PatientBanner in the shared library: full, strip and desk variants, shown in the gallery · needs clinical sign-off
- b MDV-P1-082 (#1178) StickyActionBar as a layout footer and SaveStatus, in the shared library
- b MDV-P1-083 (#1179) Chip, Tag and Banner in the shared library, with tone, icon and size; the allergy has one look · needs clinical sign-off
- c MDV-P1-084 (#1180) Shell components in the library: AccountMenu, the labelled rail item, PageHeader with back and meta slots, and Tooltip
- c MDV-P1-085 (#1181) Field and ErrorSummary in the shared library: one label, hint, error and required convention; a summary that takes focus
- c MDV-P1-086 (#1182) Page components in the library: Card and Section, PageMessage and EmptyState, SegmentedControl, SplitLayout and Sheet

**Track D · Wave D2 · screens rebuilt on the new components**

- MDV-P1-092 (#1188) Pinned patient strip and layout footer on the consultation, its sub-pages, nurse intake and the patient record · needs clinical sign-off
- MDV-P1-093 (#1189) App shell: one account menu, a 48 px top bar and a labelled rail from 768 to 1279 px
- MDV-P1-094 (#1190) Consultation workspace layout: section anchors, vitals in one row, prescription after assessment, diagnosis inline, chart panel beside the note · needs clinical sign-off
- MDV-P1-095 (#1191) Front-desk board: one header row, a one-line workload strip, pinned column headers and the doctor on the card
- MDV-P1-096 (#1192) A screen the role may not open says so, with a button to the start page
- MDV-P1-097 (#1193) Schedule day view opens at the current time; the past collapses into one row
- MDV-P1-098 (#1194) Phone rule: under 768 px clinical writes say Open this on a tablet or computer · needs clinical sign-off
- MDV-P1-099 (#1195) Queue in the consultation: tokens and ages only, escalations first, and a Next button on tablets

**Track D · Wave D3 · after the first pilot weeks, or before staff training**

- MDV-P2-051 (#1210) Navigation names in one batch: My day, Front desk, one Patient record with tabs, Register patient as a button
- MDV-P2-052 (#1211) Front desk: a list view as a third view beside By status and By doctor; the receptionist chooses the default
- MDV-P2-053 (#1212) Clinic today for the owner: five counts the system can stand behind and a needs-attention list
- MDV-P2-054 (#1213) Tables: one controller for sort and paging, stacked rows on phones, and compact density for four reference tables only
- MDV-P2-055 (#1214) Settings sub-navigation and small copy and state fixes: walk-in column name, Terms status, nurse-intake switch, organisation page
- MDV-P2-056 (#1215) Truthful status and small motion: an Updated mark on live board changes, skeletons, an offline strip, named transition properties
- MDV-P2-057 (#1216) Apply the type and width tokens: stray font sizes removed, body weight 500, page maximum 1,600 px, reading width, no sm:grid-cols
- MDV-P2-058 (#1217) Move the forms to Field and ErrorSummary, one feature at a time: registration first
- MDV-P2-059 (#1218) Adopt PageHeader, Card and Section, PageMessage and EmptyState across the clinic app and the platform console
- MDV-P2-060 (#1219) Adopt Chip, Tag, Banner and SegmentedControl in features: one chip vocabulary, one view switch
- MDV-P2-061 (#1220) Patient record and list chrome found only in the reviewers' reports: empty sections collapse, one-row filters, sideways scrolling inside tables
- MDV-P5-054 (#1221) Later: date and time pickers and a combobox in the app language; dark and high contrast only after the tone tokens

**Track S · Wave S1 · safety blockers**

- MDV-P1-087 (#1183) A free-text medicine needs a reason before authorising when the patient has, or may have, an allergy · needs clinical sign-off
- MDV-P1-088 (#1184) Load the pilot clinic's brand names into the formulary, mapped to generic and class; offer generic matches for typed text · needs clinical sign-off
- MDV-P1-089 (#1185) The note always shows what the server holds; the last keystrokes survive a page change and a reload
- MDV-P1-090 (#1186) Ending or signing with an unfinished prescription or order asks first; unfinished ones show on the doctor's day · needs clinical sign-off
- MDV-P1-091 (#1187) The signing checklist reports only what happened: no Identity confirmed tick the doctor did not make · needs clinical sign-off

**Track S · Wave S2 · desk and handoff**

- MDV-P1-100 (#1196) Front desk: undo the last move, Left without being seen, cancel a checked-in visit; the desk cannot close a visit under the doctor
- MDV-P1-101 (#1197) Patient picker rows show date of birth and last visit, and mark possible duplicates
- MDV-P1-102 (#1198) The doctor's follow-up reaches the front desk: Follow-up due on the Done card and the desk record, with Book
- MDV-P1-103 (#1199) One queue rule for the clinic: the doctor's day follows it, shows tokens and puts nurse escalations first
- MDV-P1-104 (#1200) The nurse's chief complaint prefills the doctor's note, marked from nurse intake · needs clinical sign-off
- MDV-P1-105 (#1201) Redeploy Azure dev from main, capture it again at 768 and 375 px, and mark its synthetic patients
- MDV-P1-106 (#1202) Check-in time is measured from opening Walk-in to the token, so the pilot metric cannot read 0 s, target met

**Track S · Wave S3 · later trust and handoff**

- MDV-P1-107 (#1203) Staff: only an owner can offboard an owner, step-up on any offboarding, change a role in place; link or remove /admin/staff
- MDV-P2-046 (#1204) Token slip: a small printable slip and Reprint; the display names the doctor per token after a reassignment
- MDV-P2-047 (#1205) After the identity step-up the doctor returns to the section that asked, with focus on its button
- MDV-P2-048 (#1206) Nurse intake: one confirmation for all flagged readings, and a correction with a reason until the doctor starts · needs clinical sign-off
- MDV-P2-049 (#1207) Signed note print: vitals, allergies and diagnoses by default, and a page heading · needs clinical sign-off
- MDV-P2-050 (#1208) Audit log: every action has a plain name; the default view is chart opens and changes
- MDV-P1-109 (#1288) The server refuses to sign a consultation note until the doctor has confirmed identity with the patient
- MDV-P5-053 (#1209) Fix the Expo bundling error, then run the same UX audit on the mobile apps in a simulator
- MDV-P5-055 (#1299) Native rows and tabs give screen readers the flag's words, not icon glyphs; each Mark reviewed names its result
- MDV-P5-056 (#1300) Arabic lays out right to left in both Expo apps
- MDV-P5-057 (#1301) One status icon map shared by web and native; allergy has its own icon and says Allergy
- MDV-P5-058 (#1302) Disabled mobile actions say why on screen; the message field is disabled when sending is
- MDV-P5-059 (#1303) Biometric wording per platform, with a Not now on the offer
- MDV-P5-060 (#1304) Mobile footer actions stay above the keyboard
- MDV-P5-061 (#1305) Patient choices on Profile: honest wording until they can be changed in the app
- MDV-P5-062 (#1306) Development cast dates relative to today in both mobile apps
- MDV-P5-063 (#1307) Patient tab names match their screens; mobile icons scale with font size

**People gate (not code)**

- MDV-P1-108 (#1222) UX validation sessions with the pilot doctor, nurse, receptionist and owner: the 21 decisions, the brand list, the training date and the device check

**Start order and who can run in parallel**

Day one, three agents at once, no shared files:

1. Track D agent: design rules ticket, then the guardrails ticket (owns PRODUCT.md, DESIGN.md, AGENTS.md section 4, tools/check-ui-drift, package.json, the PR workflow).
2. Track S agent A: the free-text allergy rule (owns the prescription safety check and authorise guard in the prescriptions module, SafetyChecks and AddMedicinePanel wording). The brand list (people) runs beside it.
3. Track S agent B: the note shows what the server holds (owns the note controller hook), then the honest checklist (owns the checklist domain rule). The Azure redeploy is a person or agent task with no file ownership.

Then, in this order:

- Wave S1 continues: End or sign with an unfinished prescription starts only after the free-text rule merges (both change the authorise guard).
- Wave D1a, two agents in parallel: control defaults (owns input, select, textarea, table, switch in packages/ui) and the token ticket (owns packages/tokens and styles.css). The tone-token ticket starts after the token ticket merges (same two files). The control-height codemod merges alone, in a quiet window; every open pull request rebases after it, and a ready track S pull request merges first.
- Waves D1b and D1c, up to four agents in parallel once D1a has merged: each owns its own new component files, tests and one gallery section file, plus one export line in packages/ui/src/index.ts and one import line in web/clinic/src/dev/UiGallery.tsx. The clinical components (PatientBanner, Chip, Tag, Banner) need the tone tokens first.
- Wave S2 runs beside D1: undo and left-without-being-seen (owns the visit state machine and board actions), picker rows (owns search results and picker rows), queue rule (owns the ordered-queue query, Today and the display), nurse complaint prefill (owns the nurse-to-note hand-off). The follow-up on the desk card edits the same board card as the undo ticket: start it after the undo ticket merges.
- Wave D2 after the matching D1 tickets and the track S tickets they name: pinned strip and footer first (owns ConsultStudio.tsx), then the consultation layout (same file, sequential), board chrome (owns BoardPage.tsx and FrontDeskViews.tsx; after undo), the app shell (owns AppShell.tsx), the refused screen, the schedule opening at now, the phone rule.
- Wave S3 any time after S1 and S2 are quiet; staff offboarding guard is authorisation work and goes first.
- Wave D3 only after the first pilot weeks or before the first staff training session, and never between training and the first two pilot weeks.

## Completeness proof

**Table 1: every finding to the ticket that closes it**

| Finding | Closed by |
|---|---|
| F-01 | MDV-P1-087 (#1183), MDV-P1-088 (#1184) |
| F-02 | MDV-P1-081 (#1177), MDV-P1-092 (#1188) |
| F-03 | MDV-P1-089 (#1185), MDV-P1-082 (#1178), MDV-P1-094 (#1190) |
| F-04 | MDV-P1-090 (#1186) |
| F-05 | MDV-P1-100 (#1196), MDV-P2-052 (#1211) |
| F-06 | MDV-P1-102 (#1198) |
| F-07 | MDV-P1-103 (#1199), MDV-P1-099 (#1195) |
| F-08 | MDV-P1-101 (#1197) |
| F-09 | MDV-P1-091 (#1187) |
| F-10 | MDV-P1-082 (#1178), MDV-P1-092 (#1188) |
| F-11 | MDV-P1-094 (#1190), MDV-P1-098 (#1194), MDV-P2-061 (#1220) |
| F-12 | MDV-P1-095 (#1191), MDV-P2-052 (#1211), MDV-P1-098 (#1194) |
| F-13 | MDV-P1-105 (#1201), MDV-P1-093 (#1189) |
| F-14 | MDV-P1-095 (#1191) |
| F-15 | MDV-P1-103 (#1199), MDV-P2-046 (#1204) |
| F-16 | MDV-P1-104 (#1200) |
| F-17 | MDV-P1-096 (#1192) |
| F-18 | MDV-P1-084 (#1180), MDV-P1-093 (#1189) |
| F-19 | MDV-P1-075 (#1171), MDV-P1-076 (#1172), MDV-P1-081 (#1177), MDV-P1-083 (#1179), MDV-P1-086 (#1182), MDV-P2-060 (#1219), MDV-P2-059 (#1218) |
| F-20 | MDV-P1-076 (#1172), MDV-P1-077 (#1173), MDV-P1-078 (#1174), MDV-P1-080 (#1176) |
| F-21 | MDV-P1-079 (#1175), MDV-P2-057 (#1216) |
| F-22 | MDV-P1-084 (#1180), MDV-P1-093 (#1189) |
| F-23 | MDV-P1-085 (#1181), MDV-P2-058 (#1217) |
| F-24 | MDV-P1-077 (#1173), MDV-P2-054 (#1213) |
| F-25 | MDV-P2-047 (#1205) |
| F-26 | MDV-P1-089 (#1185) |
| F-27 | MDV-P2-049 (#1207) |
| F-28 | MDV-P2-048 (#1206), MDV-P1-096 (#1192), MDV-P2-055 (#1214) |
| F-29 | MDV-P1-106 (#1202), MDV-P2-053 (#1212) |
| F-30 | MDV-P1-107 (#1203) |
| F-31 | MDV-P2-050 (#1208) |
| F-32 | MDV-P2-051 (#1210) |
| F-33 | MDV-P1-077 (#1173), MDV-P1-078 (#1174) |
| F-34 | MDV-P5-053 (#1209) |
| F-35 | MDV-P1-097 (#1193) |
| F-36 | MDV-P2-056 (#1215) |
| F-37 | MDV-P2-055 (#1214) |
| F-38 | MDV-P2-055 (#1214), MDV-P1-105 (#1201) |
| F-39 | MDV-P2-055 (#1214) |
| F-40 | MDV-P2-055 (#1214) |
| F-41 | no ticket: an unreproduced hypothesis (the clinic picker ignored the first click once in three tries on Azure). It is on the watch list below and is checked again on the redeployed app by the Azure redeploy ticket; a ticket is raised the first time it is reproduced. |
| F-42 | MDV-P5-054 (#1221) |
| F-43 | MDV-P2-055 (#1214) |
| F-44 | MDV-P1-077 (#1173), MDV-P2-059 (#1218) |
| F-45 | MDV-P1-075 (#1171) |
| F-46 | MDV-P1-105 (#1201) |
| F-47 | MDV-P2-056 (#1215) |

**Table 2: every audit backlog id to MDV key, issue, track and wave, and blocked-by**

| Audit backlog id | MDV key (issue) | Track and wave | Blocked by |
|---|---|---|---|
| UX-01 | MDV-P1-087 (#1183) | Track S: S1 | none |
| UX-02 | MDV-P1-088 (#1184) | Track S: S1 | none |
| UX-03 | MDV-P1-081 (#1177), MDV-P1-082 (#1178), MDV-P1-092 (#1188) | Track D: D1b; D: D2 | MDV-P1-077 (#1173), MDV-P1-079 (#1175), MDV-P1-080 (#1176) |
| UX-04 | MDV-P1-089 (#1185) | Track S: S1 | none |
| UX-05 | MDV-P1-090 (#1186) | Track S: S1 | none |
| UX-06 | MDV-P1-091 (#1187) | Track S: S1 | none |
| UX-07 | MDV-P1-100 (#1196) | Track S: S2 | none |
| UX-08 | MDV-P1-101 (#1197) | Track S: S2 | none |
| UX-09 | MDV-P1-102 (#1198) | Track S: S2 | none |
| UX-10 | MDV-P1-103 (#1199) | Track S: S2 | none |
| UX-11 | MDV-P1-104 (#1200) | Track S: S2 | none |
| UX-12 | MDV-P1-105 (#1201) | Track S: S2 | none |
| UX-13 | MDV-P1-077 (#1173), MDV-P1-078 (#1174) | Track D: D1a | MDV-P1-076 (#1172) |
| UX-14 | MDV-P1-094 (#1190) | Track D: D2 | MDV-P1-092 (#1188), MDV-P1-089 (#1185), MDV-P1-078 (#1174), MDV-P1-086 (#1182), MDV-P1-090 (#1186), MDV-P1-091 (#1187) |
| UX-15 | MDV-P1-095 (#1191) | Track D: D2 | MDV-P1-078 (#1174), MDV-P1-084 (#1180), MDV-P1-086 (#1182), MDV-P1-100 (#1196) |
| UX-16 | MDV-P1-084 (#1180), MDV-P1-093 (#1189) | Track D: D1c; D: D2 | MDV-P1-077 (#1173), MDV-P1-079 (#1175) |
| UX-17 | MDV-P1-096 (#1192) | Track D: D2 | MDV-P1-086 (#1182) |
| UX-18 | MDV-P2-051 (#1210) | Track D: D3 | MDV-P1-093 (#1189) |
| UX-19 | MDV-P2-052 (#1211) | Track D: D3 | MDV-P1-100 (#1196), MDV-P1-095 (#1191), MDV-P1-103 (#1199), MDV-P1-101 (#1197), MDV-P1-086 (#1182) |
| UX-20 | MDV-P1-085 (#1181), MDV-P2-058 (#1217) | Track D: D1c; D: D3 | MDV-P1-077 (#1173), MDV-P1-079 (#1175) |
| UX-21 | MDV-P1-080 (#1176), MDV-P1-083 (#1179), MDV-P2-060 (#1219) | Track D: D1a; D: D1b; D: D3 | MDV-P1-079 (#1175), MDV-P1-086 (#1182) |
| UX-22 | MDV-P1-106 (#1202), MDV-P2-053 (#1212) | Track S: S2; D: D3 | MDV-P1-090 (#1186), MDV-P1-084 (#1180), MDV-P1-086 (#1182) |
| UX-23 | MDV-P2-046 (#1204) | Track S: S3 | MDV-P1-103 (#1199) |
| UX-24 | MDV-P2-047 (#1205) | Track S: S3 | none |
| UX-25 | MDV-P2-048 (#1206) | Track S: S3 | none |
| UX-26 | MDV-P2-049 (#1207) | Track S: S3 | none |
| UX-27 | MDV-P1-107 (#1203) | Track S: S3 | none |
| UX-28 | MDV-P2-050 (#1208) | Track S: S3 | none |
| UX-29 | MDV-P1-079 (#1175), MDV-P2-057 (#1216) | Track D: D1a; D: D3 | MDV-P1-076 (#1172), MDV-P1-078 (#1174) |
| UX-30 | MDV-P2-054 (#1213) | Track D: D3 | MDV-P1-077 (#1173), MDV-P1-079 (#1175) |
| UX-31 | MDV-P1-097 (#1193) | Track D: D2 | MDV-P1-078 (#1174) |
| UX-32 | MDV-P2-056 (#1215) | Track D: D3 | MDV-P1-082 (#1178), MDV-P1-079 (#1175), MDV-P1-095 (#1191) |
| UX-33 | MDV-P5-053 (#1209) | Track S: S3 | none |
| UX-34 | MDV-P2-055 (#1214) | Track D: D3 | MDV-P1-086 (#1182) |
| UX-35 | MDV-P1-075 (#1171), MDV-P1-076 (#1172) | Track D: D0 | none |

Tickets with no audit backlog id (created while checking the audit against the tickets, see Completeness proof):

| MDV key (issue) | Title | Track and wave | Blocked by |
|---|---|---|---|
| MDV-P1-086 (#1182) | Page components in the library: Card and Section, PageMessage and EmptyState, SegmentedControl, SplitLayout and Sheet | Track D, D1c | MDV-P1-077 (#1173), MDV-P1-079 (#1175) |
| MDV-P1-098 (#1194) | Phone rule: under 768 px clinical writes say Open this on a tablet or computer | Track D, D2 | MDV-P1-086 (#1182), MDV-P1-096 (#1192) |
| MDV-P1-099 (#1195) | Queue in the consultation: tokens and ages only, escalations first, and a Next button on tablets | Track D, D2 | MDV-P1-094 (#1190), MDV-P1-103 (#1199) |
| MDV-P2-059 (#1218) | Adopt PageHeader, Card and Section, PageMessage and EmptyState across the clinic app and the platform console | Track D, D3 | MDV-P1-084 (#1180), MDV-P1-086 (#1182) |
| MDV-P2-061 (#1220) | Patient record and list chrome found only in the reviewers' reports: empty sections collapse, one-row filters, sideways scrolling inside tables | Track D, D3 | MDV-P1-086 (#1182), MDV-P1-083 (#1179) |
| MDV-P5-054 (#1221) | Later: date and time pickers and a combobox in the app language; dark and high contrast only after the tone tokens | Track D, D3 | MDV-P1-080 (#1176), MDV-P2-054 (#1213) |
| MDV-P1-108 (#1222) | UX validation sessions with the pilot doctor, nurse, receptionist and owner: the 21 decisions, the brand list, the training date and the device check | People gate | MDV-P1-105 (#1201) |

**Table 3: blueprint, accessibility, guardrails and the 21 decisions**

**Blueprint section 7, components**

| Component | Ticket |
|---|---|
| PatientBanner (full, strip, desk) | MDV-P1-081 (#1177), MDV-P1-092 (#1188) |
| Input, Select, Textarea 44 px defaults and the codemod | MDV-P1-077 (#1173), MDV-P1-078 (#1174) |
| StickyActionBar as a layout footer | MDV-P1-082 (#1178), MDV-P1-092 (#1188) |
| ConfirmDialog use on End consultation | MDV-P1-090 (#1186) |
| Table label required | MDV-P1-077 (#1173) |
| AccountMenu | MDV-P1-084 (#1180), MDV-P1-093 (#1189) |
| PageHeader with back and meta slots | MDV-P1-084 (#1180), MDV-P2-059 (#1218) |
| Field and ErrorSummary | MDV-P1-085 (#1181), MDV-P2-058 (#1217) |
| Chip and Tag (and Banner) | MDV-P1-083 (#1179), MDV-P2-060 (#1219) |
| Card and Section, PageMessage and EmptyState | MDV-P1-086 (#1182), MDV-P2-059 (#1218) |
| SegmentedControl | MDV-P1-086 (#1182), MDV-P2-060 (#1219) |
| SaveStatus | MDV-P1-082 (#1178), MDV-P1-089 (#1185) |
| SplitLayout and Sheet | MDV-P1-086 (#1182), MDV-P1-094 (#1190) |
| Tooltip | MDV-P1-084 (#1180) |
| Later: table controller, paging, stacked rows, compact density | MDV-P2-054 (#1213) |
| Later: Combobox, date and time pickers | MDV-P5-054 (#1221) |
| Later: Skeleton | MDV-P2-056 (#1215) |
| Later: dark and high-contrast themes | MDV-P5-023 (existing, now blocked by the tone-token ticket MDV-P1-080 (#1176)) |

**Blueprint section 13, accessibility requirements**

| Criterion | Ticket |
|---|---|
| 1.3.1 Info and Relationships (one h1 per page; tables labelled) | MDV-P2-049 (#1207), MDV-P1-077 (#1173), MDV-P2-059 (#1218) |
| 1.4.1 Use of Color | MDV-P1-081 (#1177), MDV-P1-083 (#1179), MDV-P1-080 (#1176) |
| 1.4.3 Contrast and 1.4.11 Non-text Contrast | MDV-P1-080 (#1176), MDV-P5-053 (#1209) |
| 1.4.4 Resize Text and 1.4.10 Reflow (strip and footer un-pin at 400 %) | MDV-P1-082 (#1178), MDV-P1-092 (#1188), MDV-P2-057 (#1216) |
| 2.1.1 Keyboard, 2.4.3 Focus Order, 2.4.7 Focus Visible | MDV-P1-084 (#1180), MDV-P1-086 (#1182), MDV-P2-056 (#1215) |
| 2.4.11 Focus Not Obscured (Minimum) | MDV-P1-082 (#1178), MDV-P1-092 (#1188), MDV-P1-094 (#1190) |
| 2.5.8 Target Size (Minimum) and the 44 px rule | MDV-P1-077 (#1173), MDV-P1-078 (#1174) |
| 3.2.3 and 3.2.4 Consistent Navigation and Identification | MDV-P2-051 (#1210) |
| 3.3.1 and 3.3.3 Error Identification and Suggestion | MDV-P1-085 (#1181), MDV-P2-058 (#1217) |
| 3.3.4 Error Prevention (undo or confirm on visit moves, End, offboarding) | MDV-P1-090 (#1186), MDV-P1-100 (#1196), MDV-P1-107 (#1203), MDV-P2-055 (#1214) |
| 3.3.7 Redundant Entry | MDV-P1-104 (#1200) |
| 4.1.3 Status Messages | MDV-P1-082 (#1178), MDV-P2-056 (#1215), MDV-P1-089 (#1185) |

**Blueprint section 14, guardrails**

| Guardrail | Ticket |
|---|---|
| Lint: no h-11 patches, arbitrary font sizes, raw hex, sm:grid-cols, raw h1 | MDV-P1-076 (#1172) |
| CI count of local Message, Field, Card, Empty that may only fall | MDV-P1-076 (#1172) |
| Browser test, mutation-checked: the patient strip is visible after scrolling | MDV-P1-092 (#1188) |
| The contrast test reads the tone tokens | MDV-P1-080 (#1176) |
| PRODUCT.md and DESIGN.md written | MDV-P1-075 (#1171) |
| The Do not do list (palette, second library, shell rebuild, state library, early dark mode) | MDV-P1-075 (#1171) |

**Validation plan, the 21 decisions** (each is recorded on the ticket it blocks; the sessions that collect them are MDV-P1-108 (#1222))

| Decision | Blocks |
|---|---|
| 1 | MDV-P1-087 (#1183), MDV-P1-088 (#1184) |
| 2 | MDV-P1-087 (#1183) |
| 3 | MDV-P1-081 (#1177), MDV-P1-092 (#1188) |
| 4 | MDV-P1-081 (#1177), MDV-P1-083 (#1179) |
| 5 | MDV-P1-091 (#1187) |
| 6 | MDV-P1-090 (#1186) |
| 7 | MDV-P1-094 (#1190) |
| 8 | MDV-P1-104 (#1200) |
| 9 | MDV-P2-049 (#1207) |
| 10 | MDV-P1-103 (#1199) |
| 11 | MDV-P1-100 (#1196) |
| 12 | MDV-P1-100 (#1196) |
| 13 | MDV-P2-046 (#1204) |
| 14 | MDV-P2-052 (#1211) |
| 15 | MDV-P1-101 (#1197) |
| 16 | MDV-P2-048 (#1206) |
| 17 | MDV-P2-053 (#1212) |
| 18 | MDV-P1-107 (#1203) |
| 19 | MDV-P1-098 (#1194) |
| 20 | MDV-P1-108 (#1222), MDV-P1-077 (#1173), MDV-P1-095 (#1191), MDV-P2-054 (#1213) |
| 21 | MDV-P2-051 (#1210) |

**Person tasks (not code)**

- The pilot doctor, nurse, receptionist and owner sessions and the 21 decisions: MDV-P1-108 (#1222).
- The doctor's brand list of about 100 medicines with a named reviewer for the class mapping: MDV-P1-088 (#1184).
- The training date, and whether screens may change shape before it or only after the first pilot weeks (decision 21): written on MDV-P2-051 (#1210) by MDV-P1-108 (#1222).
- The device check on site (mouse or touch at the desk, tablet in the room, tablet for the nurse), including one run on a real tablet: MDV-P1-108 (#1222).
- The Azure sign-in by hand for the redeployed capture (MFA): MDV-P1-105 (#1201).
- The vitals ranges sign-off (issue #956) before MDV-P2-048 (#1206).

**Watch list.** F-41 (the clinic picker ignored the first click once in three tries on Azure) is an unreproduced hypothesis. No ticket: it is re-checked on the redeployed app and raised as a ticket the first time it is seen again.

Deliberately excluded from the programme, as the arbitration rules: an inline free-text medicine box, replacing the board before the pilot, 40 px automatic rows on the desk list, a wide owner dashboard, a phone prescribing flow, a palette or typeface change, a second component library.

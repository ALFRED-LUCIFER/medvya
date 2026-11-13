# 10 · Prioritised implementation backlog

Nothing here is implemented. These are proposals for tickets; none has a GitHub issue or an `MDV-` key yet. Each is sized so one agent or developer can do it alone and test it alone. Order is the recommended order.

Effort: **S** hours · **M** a day or two · **L** several days. Regression risk: low / medium / high. Every ticket also carries the project's Definition of Done (tests on real PostgreSQL, a journey step, security check, browser check in English and Arabic, axe clean), which is not repeated below. "Concept" means [concepts/medvya-workspace-concept.html](concepts/medvya-workspace-concept.html).

Tickets marked **Clinical sign-off** must not merge before the pilot doctor has agreed the wording or behaviour ([11](11-pilot-doctor-validation-plan.md)).

---

## Batch 1 · Safety and trust (before any pilot prescribing)

### UX-01 · A free-text medicine needs a reason when the patient has, or may have, an allergy

| | |
|---|---|
| Route, persona | `/consult/:id` Prescription · doctor |
| Findings | F-01 |
| Rationale | A penicillin-class brand was authorised for a penicillin-allergic patient with "No alerts" |
| Behaviour | When a line is free text and the patient has any recorded allergy, or allergies are not recorded, Safety checks raises the existing severe "check could not run" alert for that line: "Not checked against: Penicillin. Augmentin 625 Duo was typed as free text." Authorise stays blocked until the doctor gives a reason, as for other severe alerts. The print and the audit record carry the reason. With "No known allergies" confirmed, today's amber note stays |
| Prototype | Concept, doctor view: type a brand in the medicine box (screenshot `concepts/screenshots/doctor-1440-free-text-ack.png`) |
| Components | `SafetyChecks`, `AddMedicinePanel`; no new component |
| Split | Backend: rule in the prescription safety check and the authorise guard. Frontend: wording, the reason step |
| Prerequisite | Pilot doctor agrees the wording. **Clinical sign-off** |
| Acceptance | Authorise refuses a free-text line without a reason when an allergy is recorded or unknown (server-side). The alert names every recorded allergy in words, not by colour. The reason is in the audit log. Works by keyboard; announced to screen readers. Mutation check: removing the server rule fails the test |
| Playwright | J2: Meera Iyer (penicillin) → free-text "Augmentin 625 Duo" → Authorise is refused with the named alert → give a reason → authorised, reason on the print. Second case: patient with "no known allergies" → no severe alert |
| Effort, risk | M · low |

### UX-02 · Load the pilot clinic's brand names into the formulary (data)

| | |
|---|---|
| Route, persona | Settings → Clinical → Medicines · owner; then doctor |
| Findings | F-01 |
| Behaviour | The pilot doctor's usual brands are added, each mapped to its generic and class, so a brand search finds a checked medicine. Typing free text offers "Did you mean" generic matches |
| Split | Data and a clinical review of the mapping; small frontend change for the suggestion |
| Prerequisite | The doctor's list of about 100 medicines. **Clinical sign-off** on class mapping (ADR-015 already asks for it) |
| Acceptance | Searching a listed brand returns its generic row; an allergy to the class blocks it as it does the generic |
| Playwright | Search "Augmentin" → the amoxicillin and clavulanate row → blocked for a penicillin allergy |
| Effort, risk | M (mostly people work) · low |

### UX-03 · One pinned patient strip on the consultation, its sub-pages and nurse intake; the sign bar becomes a footer

| | |
|---|---|
| Route, persona | `/consult/:id`, `/consult/:id/medicines`, `/problems`, `/nurse/:id` · doctor, nurse |
| Findings | F-02, F-10, F-19 |
| Behaviour | One `PatientBanner` in `packages/ui` with a `strip` variant: one row at 1024 and wider, two at most below; name, age and sex, MRN, date of birth, allergy block in words, consent. It stays under the top bar while the page scrolls. The "Review and sign" bar becomes a layout footer that reserves its space. Nothing focusable is ever under pinned chrome. At 400 % zoom the strip and footer un-pin |
| Prototype | Concept, doctor view, note 2 and note 7 |
| Components | New `PatientBanner`; removes `encounters/IdentityBanner` and the primitive `IdentityBanner`; `StickyActionBar` |
| Split | Frontend only |
| Prerequisite | **Clinical sign-off** on what the strip shows and the allergy look |
| Acceptance | After scrolling to the prescription at 1440×900, 1024×768 and 768×1024 the patient's name and allergy text are in the viewport. Tabbing through the note never puts focus under the strip or footer. Arabic right-to-left mirrors. Axe clean. Mutation check on the "visible after scroll" test |
| Playwright | Open a consultation, scroll to Prescription, assert the strip is visible and names the allergy; tab through all controls asserting each focused element is fully in view |
| Effort, risk | M · medium (13 test files touch the consultation) |

### UX-04 · The note always shows what the server holds; the last keystrokes survive a reload

| | |
|---|---|
| Route, persona | `/consult/:id` · doctor |
| Findings | F-03, F-26 |
| Behaviour | On mount the consultation refetches the note. Leaving the page flushes pending text and writes it to the cache. On `pagehide` the pending text is sent with `keepalive`. Save status reads "Saving…" then "Saved 10:42", or "Not saved. Trying again." No local draft in browser storage unless a security decision allows it (AGENTS.md 1.2) |
| Components | Note controller hook; `SaveStatus` |
| Split | Frontend only |
| Acceptance | Type, open Problems within 500 ms, return: the field shows the new text. Type, reload within 500 ms: the new text is there. The status never says "Saved" before the server answered |
| Playwright | Both sequences above, on the real stack |
| Effort, risk | S to M · low |

### UX-05 · Ending or signing with an unfinished prescription or order asks first; unfinished ones show on My day

| | |
|---|---|
| Route, persona | `/consult/:id`, `/today` · doctor |
| Findings | F-04 |
| Behaviour | "End consultation" (and "Sign note") with a draft prescription or unplaced orders opens a dialog that names the patient: "Authorise now", "Discard the draft", "End anyway". Today lists "Prescriptions to authorise" beside "Notes to sign". A draft can be completed and authorised after the visit ended, until it is discarded |
| Prototype | Concept, "To finish" in the queue rail |
| Split | Frontend: dialog, Today list. Backend: allow editing a draft after end; a pending-prescriptions query |
| Prerequisite | Product decision: may a draft be finished after the visit? **Clinical sign-off** |
| Acceptance | End with a draft never completes without a choice. The pending list shows the patient and opens the consultation at the prescription. Server-side authorisation unchanged |
| Playwright | Add a medicine → End → dialog → "End anyway" → Today shows it → open → authorise |
| Effort, risk | M · medium |

### UX-06 · The signing checklist reports only what happened

| | |
|---|---|
| Route, persona | Consultation → Checklist · doctor |
| Findings | F-09 |
| Behaviour | "Identity confirmed (name and date of birth)" becomes either "Patient shown: name, date of birth" (no tick), or a real one-tap "Confirmed with the patient" at the start. The AI line is hidden until AI drafting exists |
| Split | Frontend; a stored confirmation if the tap is chosen (backend) |
| Prerequisite | Pilot doctor chooses. **Clinical sign-off** |
| Acceptance | On opening a consultation no item about identity is ticked unless the doctor acted |
| Playwright | Open → checklist → assert the item is not "Done" → act → "Done" |
| Effort, risk | S · low |

---

## Batch 2 · Desk and handoff

### UX-07 · Undo the last move; "Left without being seen"; cancel a checked-in visit

| | |
|---|---|
| Route, persona | `/front-desk` · receptionist, nurse |
| Findings | F-05 |
| Behaviour | After any move a status line offers "Undo" for 30 seconds (or until the next move). A card menu offers "Left without being seen" and "Cancel visit", each with a reason; both remove the token from the display and are counted apart from "Done". When a consultation is open for the visit, the desk's "Consult done" is replaced by "With the doctor" (no action) |
| Prototype | Concept, receptionist view, "Undo last move" |
| Split | Backend: reverse transitions with rules and audit, two new end states. Frontend: undo line, card menu |
| Prerequisite | Product decision on who marks "in the room" and "done" ([11](11-pilot-doctor-validation-plan.md)) |
| Acceptance | Undo restores the previous state and token. The queue display and Today follow within one live update. Every reversal is audited. Server refuses an undo after the window |
| Playwright | Call in → Undo → card back in Ready for doctor; mark left → card gone from the display list; desk cannot close a visit with an open consultation |
| Effort, risk | L · medium (state machine) |

### UX-08 · Picker rows show date of birth and last visit, and mark possible duplicates

| | |
|---|---|
| Route, persona | Walk-in, booking and waitlist pickers; header search · receptionist, doctor |
| Findings | F-08 |
| Behaviour | Every result row: name, age and sex, **date of birth**, masked MRN, masked phone, last visit date. A record with an open merge request shows "Possible duplicate"; the older record sorts first as "Main record" |
| Prototype | Concept, receptionist view: type 90000 |
| Split | Frontend; backend adds last visit and the merge flag to search results |
| Prerequisite | Privacy decision: date of birth in a list the desk already may search |
| Acceptance | Two same-name patients differ visibly in at least two fields. No patient data in URLs. The reveal of full numbers stays logged |
| Playwright | Search "0102" → two rows with different dates of birth or a duplicate mark |
| Effort, risk | M · low |

### UX-09 · The doctor's follow-up reaches the desk

| | |
|---|---|
| Route, persona | Board Done card, desk patient record, schedule · receptionist |
| Findings | F-06 |
| Behaviour | "Follow-up due from 17 Nov · Book" on the Done card and the desk record; it opens the booking panel on that date with the patient chosen. It clears when booked or dismissed with a reason |
| Split | Frontend; backend if the follow-up is not yet exposed to desk roles (no clinical text) |
| Acceptance | Matches MDV-P2-043's line. The desk sees the date only, never the note |
| Playwright | Doctor sets 2 weeks → receptionist sees and books it |
| Effort, risk | M · low |

### UX-10 · My day follows the clinic's queue rule, shows tokens, and puts escalations first

| | |
|---|---|
| Route, persona | `/today` · doctor |
| Findings | F-07, F-15 |
| Behaviour | Arrived patients sort by the clinic's rule (token, arrival or appointment: a setting). Each row shows the token. A nurse escalation is pinned above "Next patient" with its reason. The queue display and the board use the same order |
| Prototype | Concept, doctor view, queue rail |
| Split | Backend: ordering rule and setting. Frontend: Today, display, board |
| Prerequisite | The pilot clinic's queue rule |
| Acceptance | An escalated visit is in the first viewport at 1024×768. The display's "Next" list equals Today's order |
| Playwright | Run the handoff walkthrough → the escalated patient is row 1 |
| Effort, risk | M · medium |

### UX-11 · The nurse's complaint prefills the doctor's note

| | |
|---|---|
| Route, persona | `/nurse/:id` → `/consult/:id` |
| Findings | F-16 |
| Behaviour | Chief complaint is prefilled, marked "from nurse intake", editable; the doctor's edit is the doctor's |
| Split | Frontend, maybe backend |
| Prerequisite | **Clinical sign-off** |
| Playwright | Nurse types a complaint → doctor's field holds it with the source mark |
| Effort, risk | S · low |

### UX-12 · Redeploy Azure dev from `main` and re-capture

| | |
|---|---|
| Findings | F-13 |
| Behaviour | Tag deploy; then run the audit's Azure capture again at 768 and 375 |
| Acceptance | Page width equals viewport width on every captured page; Sign out reachable |
| Effort, risk | S · low |

---

## Batch 3 · System changes the layout pass needs

### UX-13 · Library control defaults at 44 px; remove the call-site patches

| | |
|---|---|
| Findings | F-20, F-33 |
| Behaviour | Input, Select trigger and Textarea default to 44 px in `packages/ui`; a codemod deletes `min-h-11` / `h-11` at about 350 call sites; switch and radio hit areas reach 44 px; a lint rule forbids the patch |
| Split | Frontend only |
| Acceptance | No visual change at the patched sites (screenshot comparison). The platform console's missed controls reach 44 px. Every table has a label (type-required) |
| Playwright | Existing suites stay green; one metric check: no control under 24 px on touch widths |
| Effort, risk | S · low |

### UX-14 · Consultation workspace layout pass

| | |
|---|---|
| Route, persona | `/consult/:id` · doctor · 1024 and wider first |
| Findings | F-11, F-03 |
| Behaviour | Per [08, section 8](08-design-system-blueprint.md): strip (UX-03), pinned section anchors with completion marks, vitals as one row, prescription directly after assessment (still added through the existing dialog), a diagnosis added inline, chart panel beside the note at 1024 and wider and as a drawer below, layout footer. The controllers and the save model do not change. The seven guarantees in [07, ruling 3](07-debate-and-arbitration.md) are acceptance criteria |
| Prototype | Concept, doctor view at 1440, 1024 and 768 |
| Split | Frontend only. The queue rail is a separate ticket (UX-10 first) |
| Prerequisite | UX-03, UX-04, UX-13. **Clinical sign-off** on section order |
| Acceptance | First text box at or above 200 px at 1440×900 and 1024×768. No route change to add a diagnosis. Chart panel reachable in one action at 768. Page height for an empty note under 2 viewports at 1440. Sign and authorise remain separate acts |
| Playwright | J2 steps 9 to 12 rewritten against the new layout; the scroll and focus tests from UX-03 |
| Effort, risk | L · high (13 test files, 79 references) |

### UX-15 · Front-desk board chrome; doctor on the card

| | |
|---|---|
| Route, persona | `/front-desk` · receptionist |
| Findings | F-12, F-14 |
| Behaviour | One 44 px header row (title and date, view switch, live, actions). A one-line workload strip. Column headers pinned; columns at least 208 px; an empty column collapses to a 48 px stub. Card: name, time, doctor's short name when more than one doctor works, flags on one line, one action with a short label. Under 768 px: one list with status tabs |
| Split | Frontend only. The board stays; the list view is UX-19 |
| Acceptance | First card at or above 220 px at 1024×768. No text outside its card at any width from 768 to 1440. Walk-in still takes 6 clicks for a new patient and 3 for a returning one |
| Playwright | J3 green; a metric check on the first card's position |
| Effort, risk | M · medium (19 test files reference the board) |

---

## Batch 4 · After the first pilot weeks

| ID | Title | Findings | Behaviour in one line | Split | Effort, risk |
|---|---|---|---|---|---|
| UX-16 | Account menu and 48 px top bar; labelled rail | F-18, F-22 | Language, devices, facility and sign out move into one menu; labels under rail icons | Frontend; add the shadcn dropdown menu | S to M · low (10 locators) |
| UX-17 | A refused screen says so | F-17 | "Your role does not include this screen" with the start-page button | Frontend | S · low |
| UX-18 | Navigation names in one batch | F-32 | My day; Front desk; one Patient record with tabs; Register patient as a button | Frontend, three languages; land at a training boundary | M · medium |
| UX-19 | List view for the front desk, as a third view | F-12 | Status counts as filters; grouped rows; Undo in the toolbar; the receptionist chooses the default | Frontend (`QueueTable` and `groupByColumn` exist) | M · low |
| UX-20 | Field and ErrorSummary | F-23 | One field pattern; a summary that takes focus after a failed submit | Frontend, one feature per change | M · medium |
| UX-21 | Tone tokens; Chip, Tag, Banner | F-19, F-20 | A tone layer in the tokens; no raw hex in components; allergy has one look | Frontend; **Clinical sign-off** on the allergy look | M · low |
| UX-22 | "Clinic today" for the owner | F-29 | Five counts that exist and a needs-attention list; revenue "Not tracked yet"; fix the check-in timer | Frontend for live counts; backend for the timer and pending lists | M · low |
| UX-23 | Token slip and reprint | F-15 | A small printable slip; "Reprint"; the display names the doctor per token after reassignment | Frontend, small backend | M · low |
| UX-24 | Step-up returns to the section that asked | F-25 | After confirming identity the page returns to the prescription and focuses Authorise | Frontend; test with real Entra on Azure | S · low |
| UX-25 | Nurse intake: one confirmation for all flags; correction until the doctor starts | F-28 | **Clinical sign-off**; vitals ranges first (issue #956) | Frontend, backend | M · medium |
| UX-26 | Signed note print: vitals, allergies, diagnoses; a page heading | F-27 | **Clinical sign-off** on the contents | Backend, frontend | M · low |
| UX-27 | Staff: owner-only offboarding of an owner, step-up, change role; link Invite | F-30 | First test what the server does today | Backend, frontend | M · medium |
| UX-28 | Audit log: plain names; default to chart opens and changes | F-31 | Translations in three languages | Frontend | S · low |
| UX-29 | Type and spacing tokens; page and reading widths | F-21 | Nine type tokens; codemod stray sizes; body weight 500; 1,600 px page maximum | Frontend; visual check in English and Arabic | M · medium |
| UX-30 | Table controller, paging, stacked rows, compact density for four reference tables | F-24 | Amend AGENTS.md section 4 in the same change | Frontend | L · medium |
| UX-31 | Schedule opens at the current time; the past collapses | F-35 | | Frontend | S · low |
| UX-32 | Truthful status and small motion | F-36, F-47 | "Updated" mark on live board changes; skeletons; named transition properties | Frontend | S to M · low |
| UX-33 | Fix Expo bundling; then audit the mobile apps on a simulator | F-34 | Dependency resolution only | Dependency | S · low |
| UX-34 | Small copy and state fixes | F-37 to F-41, F-43, F-46 | Walk-in column name; Terms status; nurse-intake confirm; organisation page; settings sub-navigation; synthetic names on Azure | Frontend | S each · low |
| UX-35 | Write `PRODUCT.md` and `DESIGN.md`; add the lint rules and the CI count | F-45, F-19 | The guardrails in [08, section 14](08-design-system-blueprint.md) | Docs, lint | S · low |

Each row in batch 4 needs the full field set (acceptance criteria, Playwright scenarios) written when it is picked up; the findings and the blueprint hold the detail.

---

## The smallest safe first batch

UX-01, UX-03, UX-04, UX-06 and UX-12, with UX-02 started in parallel as people work. See [00-executive-verdict.md](00-executive-verdict.md).


---

## Ticket map

Added when the proposals above became tickets. The epic is MDV-P1-E12 (issue #1170); the full programme text, with the waves, start order and non-negotiables, is [docs/05-delivery/ux-programme.md](../05-delivery/ux-programme.md). The ids UX-01 to UX-35 are this document's own and are not ticket keys.

### Every finding to the ticket that closes it

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

### Every proposed ticket to its MDV key

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

### Other audit content covered

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

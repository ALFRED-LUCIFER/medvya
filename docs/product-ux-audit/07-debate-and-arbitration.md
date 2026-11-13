# 07 · Debate and arbitration

The Lead Design Arbiter is the main Claude session that ran the audit. It gathered the Azure and local evidence, read the three reports and the three rebuttals, built the concept, and makes the rulings below. The reviewers did answer each other in a real second round ([06](06-three-independent-reviews.md)); they did not converse beyond that one exchange.

## Agreements

All three found these independently, before reading each other.

| Agreement | Why it holds |
|---|---|
| The patient's identity and allergy must stay pinned on every clinical write screen (A-01, B-03, C-02 → F-02) | Measured: nothing names the patient at 1,600 px of scroll, on a page up to 5,350 px tall. The project's own design rule says the banner is fixed |
| No reset | 615 pages with no sideways overflow, axe nearly clean, careful server-side safety rules, and a walk-in path of 6 clicks. A reset throws tested behaviour away for no pilot gain |
| Keep the palette, the typeface and the components | The identity is credible for a clinic; the defects are composition and duplication |
| The consultation needs a layout pass | 3 % of a 1024×768 screen is working content; 5 route changes for a simple visit |
| Nothing a pilot clinic will struggle with is caused by missing animation | See [08](08-design-system-blueprint.md#12--motion) |

After the second round they also agree on the order: safety behaviour first, system work second.

## Disagreements and rulings

### 1 · Surgical fixes or cohesive modernisation?

| | |
|---|---|
| Positions | A: surgical; seven of the top ten defects are behaviour that no component fixes; a solo founder cannot afford more before a pilot. B and C: the defects are systemic (three patient headers, about 350 size patches), so screen-by-screen fixes drift apart again |
| Evidence | A's walkthroughs (F-01, F-03 to F-09). C's code counts (F-19, F-20) |
| Risk of A alone | The banner gets fixed three times and diverges; the consultation keeps its route hops, which cause F-03 |
| Risk of B first | Weeks of consolidation while a prescription that ignores an allergy can still be authorised |
| Deciding principle | Patient safety first, then the smallest change that removes a class of defect |
| **Ruling** | **B is the destination; A is the order.** Safety behaviour ships first and separately. Only three system changes come before the pilot: one pinned patient banner, the 44 px control defaults with their codemod, and required table labels. Everything else in B waits |
| Rejected | Option B as a gate for the pilot. Option A as the end state |
| Open assumption | That the pilot date allows the bounded consultation layout pass. If not, the pinned strip and the sign-bar fix alone are acceptable for the first weeks |

### 2 · Dense interface or readable, touch-friendly controls?

| | |
|---|---|
| Positions | C first proposed a compact tier of 32 to 36 px rows for pointer-only reference tables. The arbiter's concept uses 40 px rows with a pointer and 48 px on touch. B: compact is unsafe on anything that changes state. A: never on rows with a patient action or in patient pickers; "pointer-only" cannot be detected on a touch laptop |
| Evidence | Staff table shows 7 rows per screen and the audit table starts 800 px down (C). 10 % of controls on touch widths are already under 44 px (metrics). Board actions cannot be undone today (F-05) |
| Risk | Mis-taps on a shared desk PC or a tablet, with irreversible actions |
| Deciding principle | Density is chosen by task. A write is never compact |
| **Ruling** | Two densities. **Comfortable (44 px controls, 48 px rows) is the default and is mandatory** for every control that changes state, every patient-picking row, medicine lines, and any device that reports a coarse pointer (`any-pointer: coarse`). **Compact (36 px rows) is an explicit per-table choice** for read-mostly reference tables: audit log, formulary, staff, order sets. Tighter page chrome is accepted everywhere. Typed note text is 16 px. C withdrew its 32 px tier. Any row height under 44 px needs AGENTS.md section 4 amended in the same change |
| Rejected | The concept's automatic 40 px rows on the front-desk list. Compact on the patient directory |
| Open assumption | That the pilot desk uses a mouse. To be observed |

### 3 · One-screen consultation or safe, deliberate steps?

| | |
|---|---|
| Positions | Arbiter and B: one workspace with the prescription inline and the chart open. A: it removes missed-allergy and stale-text risk, and adds two risks: a rail of other patients beside the open note, and signing without reaching the bottom. C: an inline free-text medicine box makes the F-01 hole the fastest path; the three-region layout touches 13 test files |
| Evidence | F-02, F-03, F-11; F-01 for the free-text risk |
| Deciding principle | Fewer route changes, never fewer deliberate acts |
| **Ruling** | One workspace, with seven guarantees: (1) pinned patient strip with the allergy in words; (2) sign the note, authorise the prescription, place orders and end the consultation stay four separate, recorded acts, and one identity step-up may cover them; (3) End asks when a draft prescription or unplaced order exists; (4) nothing focusable sits under pinned chrome; (5) the allergy result shows beside the medicine line; (6) diagnoses and medicines need no route change; (7) no tick the doctor did not earn. **The existing Add medicine dialog stays** until F-01 is closed and a clinician has seen a replacement. The queue rail shows tokens and ages, never names or reasons, and switching patient keeps today's "another consultation is open" challenge |
| Rejected | The first concept's rail with patient names and "chest pain" in view of the patient in the room (changed in the concept). An inline free-text box without an acknowledgement (changed in the concept) |
| Open assumption | Section order and what the strip shows need the pilot doctor |

### 4 · Kanban board or grouped list at the front desk?

| | |
|---|---|
| Positions | Arbiter: replace six columns with a status-grouped list whose counts are filters. A: safer for a real desk if undo ships first, check-in from search keeps the identity confirm, a By-doctor view remains and order matches the display. B: yes, after a 40-row test. C: keep the board and fix its chrome before the pilot; add the list as a third view and let the receptionist decide; 19 test files reference the board |
| Evidence | First card at 452 px; one and a half cards per column on a first screen; no drag exists, movement is by button (A) |
| Risk | Changing the desk's main screen between training and the first pilot weeks |
| Deciding principle | Do not move the receptionist's furniture mid-pilot; let the user choose between working options |
| **Ruling** | **Before the pilot: keep the board, fix its chrome** (first card at about 200 px), add the doctor to the card, add Undo and "Left without being seen". **After the first pilot weeks: add the list as a third view** next to By status and By doctor, and let the pilot receptionist's use decide which is the default |
| Rejected | Replacing the board before the pilot |
| Open assumption | That a list is faster with 30 or more patients. Untested |

### 5 · Phone access or clinical safety?

| | |
|---|---|
| Positions | All three lean the same way with different lists. A: block or make read-only prescribing, orders, reconciliation and signing a new note on a phone. B: also full registration, schedule editing and settings. C: comfortable sizing always for clinical writes |
| Evidence | The prescription table scrolls sideways inside itself at 375 and hides dose columns (B). The consultation is 4,941 px at 375. No phone flow was walked by anyone |
| Deciding principle | A phone is for looking and for urgent small acts; a prescription is neither |
| **Ruling** | Below 768 px doctors get My day, the queue, a read-only chart, search, signing an addendum and seeing an escalation. Composing or authorising a prescription, reconciliation, orders, signing a new note, merge approval and settings show "Open this on a tablet or computer". Reception gets the queue list and quick registration. Tablet (768 and up) is a first-class device for the nurse and the doctor |
| Rejected | Making every doctor workflow identical on a phone |
| Open assumption | Whether the pilot doctor wants to prescribe from a phone at home. Ask |

### 6 · Modernise now or protect what staff have learned?

| | |
|---|---|
| Positions | C: rename navigation ("My day", one "Patient record", Register out of the menu). A: route addresses and role names staff were told must not move mid-pilot; "change shape before the first training session or after the first two pilot weeks, not in between" |
| Evidence | F-32; whether pilot staff have seen the current screens is unknown |
| **Ruling** | Renames and moved navigation land in one batch, either before the first training session or after the first pilot weeks. The account menu and rail labels are low-risk and can go earlier |
| Open assumption | The training date. The founder knows it; the audit does not |

### 7 · A broad owner dashboard or a short actionable page?

| | |
|---|---|
| Positions | Arbiter's concept: flow counts, needs-attention list, per-doctor table. A: build five counts and the list; "seen", "median consult", "running late" and "check-in time" are not trustworthy yet, because the desk can press Consult done without a doctor (F-05) and the estimate is a shortcut in code. C: live counts need no backend; business figures do |
| Deciding principle | No number the system did not earn |
| **Ruling** | A small "Clinic today": booked and still to arrive; arrived, split walk-in and booked; waiting now and longest wait; notes to sign and prescriptions to authorise per doctor; incomplete registrations; and a needs-attention list with one action each. Revenue reads "Not tracked yet". Per-doctor consult times wait until F-05 is fixed. Built after the safety fixes; it is the buyer's screen, so before the pilot review meeting if time allows |
| Rejected | A wall of KPI cards; any figure derived from the unreliable "Done" state |

### 8 · The navigation rail: icons only, or labelled?

| | |
|---|---|
| Positions | Arbiter's first concept: a 56 px icon-only rail at every width. B and C: a recognition problem, the same one they found in the product (F-22) |
| **Ruling** | The arbiter was wrong. Labels under icons in a 72 to 76 px rail; the full sidebar may be collapsed by the user's own choice only. The concept was changed |

## The arbiter's final choice

**Option B, cohesive modernisation, delivered in the clinical reviewer's order.** In plain terms:

1. Close the safety and trust gaps as small separate changes (F-01 to F-09).
2. Do three system changes that those fixes need: one pinned patient banner, correct control defaults, a layout footer in place of the floating sign bar.
3. Redeploy Azure dev from `main`.
4. Do one bounded layout pass on the consultation and trim the board's chrome.
5. After the first pilot weeks: the rest of the library consolidation, navigation renames, the list view for the desk, table density, and the small owner page.

A full reset is not warranted anywhere, including the consultation page: its page file is composition, and the hard logic sits in hooks that must not break.

## Red-team critique of this choice

- **It may still be too much for one person before a pilot.** Nine behaviour fixes plus a layout pass is weeks of work with tests, mutation checks and three languages. If the date is fixed, steps 1 to 3 are the real minimum and step 4 slips.
- **The evidence is synthetic.** No doctor, nurse or receptionist used the product. Click counts come from scripts. The ranking of F-05 against F-12, for example, could invert after one morning at a real desk.
- **Three reviewers, one model.** Their agreement on the pinned banner and on option B may be a shared habit, not three confirmations. They also shared one set of screenshots.
- **The concept is unproven.** It looks tidier; nobody has typed a real note in it. The strip may be too thin for a patient with five allergies. The 200 px queue rail may be noise. B noted the strip clips the date of birth at 375.
- **Sequencing A-first delays the visible improvement.** A founder showing the product to a second clinic will still show a long consultation page for some weeks.
- **Azure is behind `main`.** Some Azure findings may already be fixed, and some `main` behaviour may regress when deployed with real Entra sign-in (the step-up, F-25, was only seen with the development provider).
- **The free-text fix may annoy.** If the pilot doctor prescribes mostly by brand, an acknowledgement on every line trains dismissal. The brand list (data, not code) matters as much as the alert.

## Assumptions nobody could settle

| Assumption | Who can settle it |
|---|---|
| Brand or generic prescribing, and the top 100 medicines | Pilot doctor |
| Queue order: token, arrival or appointment | Pilot doctor and receptionist |
| Who marks "in the room" and "done" | Pilot doctor and receptionist |
| Whether staff have already been trained on the current screens | Founder |
| Mouse, tablet or both at the desk and in the room | Founder, on a site visit |
| What the visit record and the printed prescription must contain | Pilot doctor |

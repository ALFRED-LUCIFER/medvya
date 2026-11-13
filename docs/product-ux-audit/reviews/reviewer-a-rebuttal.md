# Reviewer A · Rebuttal

Written after reading `reviewer-b-visual-interaction.md`, `reviewer-c-design-system.md` and the Lead Arbiter's concept screenshots (`concepts/screenshots/`). My original report is unchanged. Audit only; the one new browser run is noted under question 7.

Screenshot paths are under `docs/product-ux-audit/screenshots/local-stack/walkthroughs/` unless they start with `concepts/`.

## 1 · "The defects are systemic, so surgical fixes will not hold"

**Verdict: agree with conditions.**

They are right about cause. My A-01 (banner scrolls away) exists because there are three patient headers, not one; fixing it three times is the wrong fix. My A-13 already asked for a layout pass on the consultation. So B's scope (layout, density, component consolidation) is the correct way to deliver several of my findings.

They are wrong if "B" becomes the gate for the pilot. Seven of my ten top findings are behaviour, not layout: A-02, A-03, A-04, A-06, A-07, A-08, A-10. No token layer or banner component fixes any of them.

**What option B must not touch before the pilot:**

- The walk-in path: 6 clicks and 3 fields for a new patient, 3 clicks for a returning one, no scrolling (`receptionist/walkin-new/`).
- The Add medicine dialog: patient and allergy on top, chips, the block with a logged reason (`doctor/consult-orders/`).
- The allergy rules: "not recorded" is never "none"; the deliberate "Confirm: no known allergies".
- The sign dialog, the read-only signed note and addenda.
- The duplicate block at registration and the check-in identity confirm.
- Route addresses and the role names staff have been told.

**Retraining risk.** I do not know whether the pilot staff have been shown the current screens: **untested**. If they have, the cost falls on the receptionist first. She works from where things are, under queue pressure, and a moved "Walk-in" button or a board that changed shape on day 3 will cost more than the pixels saved. A doctor tolerates a tidier page; he does not tolerate the prescription moving. So: change shape before the first training session or after the first two pilot weeks, not in between.

## 2 · Kanban board → dense status-grouped list

**Verdict: agree with conditions.** Safer for a real desk, if four things are kept.

Evidence for the list: at 1440 the board showed one and a half cards per column on the first screen; names wrapped; the card did not name the doctor (A-11). The concept row shows token, name, tags, wait and doctor on one line (`concepts/screenshots/desk-1440-phone-lookup.png`), and puts the nurse-flagged patient first, which answers part of A-08.

What the receptionist loses is small. I found no drag on the board; movement is by button. The "spatial model" is really the counts per status, and the concept keeps those as filters.

Conditions:

1. **Rows that act must not sit 40px apart with a one-tap, no-undo action.** Today "Call in" is irreversible (A-06). Adjacent "Call in" buttons in a dense list raise the mis-tap rate. Ship undo and "left without being seen" first, or with it.
2. **Check-in from the search result keeps the identity confirm** (name and date of birth) and shows the doctor being assigned. One tap that both picks a person and checks them in is how the wrong Deepak Nair gets a visit (A-05). The concept's "last visit" and "booked 11:20" in the result are exactly what A-05 asked for.
3. **Keep a "By doctor" view.** Reassignment lives there (7 clicks today).
4. **Order within a group follows the clinic's queue rule** and matches the TV (A-12).

Do this before the first training or after the pilot's first weeks (question 1).

## 3 · One-screen consultation, prescription inline, chart always open

**Verdict: agree with conditions.** It removes more risk than it adds.

| Risk | Effect of the concept |
|---|---|
| Missed allergy | Removed: the allergy sits in a one-row pinned strip (answers A-01). |
| Stale or vanished text | Removed at the cause: no route change to add a diagnosis (A-03 appeared only after a page hop). |
| Retyped data | Removed if "vitals above are already part of the note" is real (A-09, A-16). |
| Wrong patient | **New:** a rail of other patients' names beside the open note. NISTIR 7804-1 guidance 2.4 and 2.5 ask that moving to another chart be a deliberate, visually distinct action. Keep today's "You have another consultation open" challenge on every rail click, and style the rail unlike the section anchors. |
| Unreviewed section | **New:** a single-scroll note makes it easy to sign without reaching the bottom. Keep the checklist count in the footer, but fix A-10 first so it counts only what the doctor did. |
| Free text in an inline box | **Worse** unless A-02 is fixed: an always-visible "Add medicine" text box invites typed brands. |

What must stay a deliberate, separate step:

- **Signing the note**: its own dialog with name, MRN and date of birth, as now.
- **Authorising the prescription**: its own explicit confirmation and its own audit record. One identity step-up may cover both (it does today, inside five minutes), and one review screen may hold both, but "Review and sign" must not authorise medicines as a side effect.
- **Placing orders**: explicit, as now.
- **Ending the consultation**: must ask when a draft prescription or unplaced order exists (A-04).

I also accept B-01. The sign bar should be a layout row, not an overlay; at 1024×768 I measured the first note field at 1,138px with about 100px of bar over the bottom.

## 4 · Density: compact rows and tighter chrome

**Verdict: agree with conditions.**

Tighter chrome: agree without reservation. 440px before the first section on the consultation and 452px before the first board card is waste.

Compact 32–36px rows: acceptable only where C puts them, pointer-only reference tables (audit log, formulary, staff). Clinical objection to going further:

- Any row that carries a patient action (check in, call in, start consultation, pick a patient in search) is a write. A shared desk PC with a worn mouse and a tablet in the nurse's hand are both mis-tap settings, and today those actions cannot be undone.
- Patient pickers need two lines per row (name, then date of birth and last visit). Squeezing them to one compact line recreates A-05.
- "Pointer-only" cannot be detected reliably on a touch laptop. Make compact an explicit per-table choice, never automatic.

## 5 · Phone and tablet

**Verdict on "comfortable always for clinical writes": agree.**

On a phone (under 768px):

- **Allow:** Today and the queue, reading a chart, allergies and last vitals, seeing an escalation, signing an addendum, the nurse's escalate-to-doctor.
- **Read-only or blocked:** composing or authorising a prescription, reconciliation, placing orders, signing a new note, merge approval, staff and settings changes. The prescription table already scrolls sideways inside itself at 375 (B's measurement), which hides dose columns; that is a data availability error waiting to happen.
- **Nurse vitals entry:** allow on a tablet; on a phone only with the pinned identity strip and one vital group per screen. Until then, block.

I did not walk any flow at phone width: **untested** by me, so this rests on B's measurements and the page lengths I measured.

## 6 · Owner "Clinic today"

**Verdict: agree with conditions.** Worth a small version before the pilot, because the owner is the buyer and has nothing today (A-19). The full concept is breadth without data.

Trustworthy today, from what the system already records:

1. Booked today and still to arrive.
2. Arrived, split walk-in and booked (check-ins issue tokens in one transaction).
3. Waiting now and longest wait (state-change time on each visit).
4. Notes not signed and prescriptions not authorised, per doctor.
5. Registrations incomplete.

Not trustworthy yet:

- **Seen** and **median consult**: the desk can press "Call in" and "Consult done" without a doctor (A-06), and doctors may never press End.
- **No-show**: only as good as the desk's habit of marking it.
- **"Running 25 min late"**: the wait estimate in code is place in queue times slot length, marked as a shortcut.
- **Check-in time**: reads 0 s for every walk-in (A-19).
- **Revenue**: billing is off. The concept's "Not tracked yet" is the right wording.

The "Needs attention" list with actions is the best part; build that and the five counts, nothing else.

## 7 · A-02, precisely

**Now fully observed.** My original report inferred the last step. For this rebuttal I ran it end to end (`tools/walkthrough-doctor-5-freetext.mjs`; `doctor/free-text-allergy/01-free-text-line-ready.png`, `02-after-authorise.png`, `03-print.png`).

Steps:

1. Doctor starts a consultation (Anjali Varma, synthetic) and records an allergy: Penicillin, rash. The banner shows "Allergies: Penicillin (Patient reported)".
2. In Prescription, type "Augmentin 625 Duo". No formulary match (23 generic rows, no brands). Press "Add as free text".
3. Fill dose 1, Tablet, By mouth, Twice a day.
4. Safety checks say: "Allergies: listed on the chart · **No alerts from the checks that could run.**" and, under "Not checked", "typed as free text, so not checked for allergies, interactions or dose".
5. Press Authorise, confirm identity, press Authorise again.

Result: "Authorised · version 1 · RX-CAEE099F". The print page shows "Allergies: Penicillin" directly above "Medicines: Augmentin 625 Duo". No reason was asked for.

Still inferred: how often a pilot doctor will type a brand. That needs the doctor's answer.

**Smallest safe fix.** Reuse the rule that already exists for "allergies not recorded": there, the check "could not run" and the system raises a severe alert that needs a reason before authorising. Apply the same to any free-text line when the patient has a recorded allergy or allergies are not recorded, with wording that names the allergy: "Not checked against: Penicillin". No new screen, no new component.

**Second step, data not code:** load the pilot doctor's usual brands into the clinic formulary mapped to generics, so most lines are checked.

**Does it block the pilot?** Yes. A printed prescription that lists the allergy and a medicine of that class together, with "No alerts", is the single output I would not put in front of a patient. The fix above is small.

## Direction vote

**Changed from A to B, sequenced.** B as Reviewer B defines it: layout, density and component consolidation. Not the navigation renames or the board replacement before the pilot unless they land before the first training session.

Five non-negotiables, whichever direction wins:

1. **Safety behaviour ships first and separately:** A-02, A-04, A-03, A-06 (undo and left-without-being-seen), A-07. None waits for a component.
2. **One pinned patient strip with the allergy in words on every clinical write surface,** including any inline prescription and the nurse intake.
3. **Sign, authorise prescription and place orders stay three explicit acts,** each recorded; one step-up may cover them.
4. **The walk-in path does not get longer.** Re-measure after any change: 6 clicks and 3 fields new, 3 clicks returning.
5. **No number or tick that the system did not earn:** fix "Identity confirmed" (A-10) and the 0 s metric (A-19) before adding any dashboard, and show "not tracked" in place of zero.

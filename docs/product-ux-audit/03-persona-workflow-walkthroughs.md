# 03 · Persona workflow walkthroughs

The walkthroughs were run by reviewer A as scripts against the local full stack (current `main`, synthetic data), and counted by the scripts: clicks, typed fields, route changes and scroll distance. The full account, with the gold-standard path written before each walk, is in [reviewer A's report, section 3](reviews/reviewer-a-clinical-workflow.md). This page is the summary and the arbiter's reading of it.

**Read the numbers with care.** They are script counts, not a person's. A script does not hesitate, and it scrolls more than a person who knows the page. No timings are given, because none were measured with a person. Screenshots are under `screenshots/local-stack/walkthroughs/`. The scripts are `tools/walkthrough-*.mjs`.

## Receptionist

**Gold path for a walk-in.** Type the phone number. If the patient exists, one tap picks them, with enough on the row to tell two family members apart. If not, four fields. One action issues a token the patient can hold, numbered in the order patients will be called. Mistakes can be undone.

| Task | Clicks | Typed fields | Route changes | Scroll | Outcome |
|---|---|---|---|---|---|
| Walk-in, new patient, to a token | 6 | 3 | 3 | 0 px | Token issued |
| Walk-in, returning patient by 4 phone digits | 3 | 1 | 1 | 0 px | Token issued |
| Check in a booked patient (identity confirm) | 2 | 0 | 0 | 0 px | Token issued |
| Book from the schedule | 4 | 1 | 1 | 188 px | Booked |
| Reschedule to the next day | 5 | 0 | 1 | 92 px | Moved, with history |
| Move a waiting patient to another doctor | 7 | 0 | 1 | 494 px | Moved; token unchanged |
| Add to the waitlist | 4 | 1 | 1 | 162 px | Added |
| Walk-in with a chosen doctor, then send to nurse | 10 | 3 | 4 | 60 px | With nurse |

**Works well.** The walk-in is short and needs no scrolling. Search by four phone digits works as you type. A second same-day visit is caught. A duplicate registration is blocked until the desk chooses. Check-in asks to confirm name and date of birth.

**Friction and risk.**

| Gap | Finding |
|---|---|
| Two same-name records look the same in the picker; the script picked the duplicate | F-08 |
| No undo, no cancel after check-in, no "left without being seen" | F-05 |
| The desk can put a visit "In consult" and "Done" with no doctor involved | F-05 |
| A token cannot be printed, reissued or cancelled; after reassignment the letter no longer says which doctor | F-15 |
| The doctor's follow-up never appears | F-06 |
| The board's first card is halfway down the screen; the card does not name the doctor | F-12, F-14 |
| End-of-day close, billing and payment handoff | Absent, planned for phase 4 |
| Patient messages (confirmation, reminder) | Not testable: no provider |

**Tokens.** Issued at check-in in one transaction; a letter per doctor per day and a number per letter; unique per clinic, day, letter and number (code, not load-tested); kept on reassignment; shown on the board, the overview and the waiting-room display. The display's order followed slot time, not token number: A-05, B-02, A-06, A-09, A-07, A-08.

## Nurse (1024×768, touch emulation)

**Gold path.** A queue, longest wait first. The patient's identity stays in view. Vitals on a number pad with previous values beside them. Abnormal values confirmed once. Escalate in one step, and the doctor sees it first. Correct a typo after hand-off.

**Observed.** One click to open the intake. Then 10 typed fields and 10 clicks to record six vitals, keep four flagged values, add one allergy and escalate; one more click to hand off. 1,528 px of scrolling on a 1,778 px page.

**Works well.** Numeric keypad hints. "No known allergies" is a deliberate button, and hand-off is locked until allergy status is set. A reversed blood pressure is refused in words. Escalation needs a reason.

| Gap | Finding |
|---|---|
| Identity scrolls away | F-02 |
| The complaint is not shown to the doctor | F-16 |
| The escalation does not move the patient up | F-07 |
| One tap per flagged value; no correction after hand-off | F-28 |
| With nurse intake off, the nurse has no vitals screen | F-28 |

The nurse role exists and works when the owner switches nurse intake on (Settings → Clinical → Nurse intake).

## Doctor (1440×900, and the consultation again at 1024×768)

**Gold path.** My day shows who is next in the order patients are seen, urgent first. One action opens the visit. Identity and allergies stay pinned. The nurse's complaint and vitals are already in the note. Complaint to plan on one screen; a diagnosis without leaving; prescribing with dose, frequency and duration, stopped by an allergy conflict however the medicine was entered. The system will not let the patient leave with a half-finished prescription unnoticed. The follow-up lands with the desk.

| Step | Clicks | Typed fields | Route changes | Scroll | Note |
|---|---|---|---|---|---|
| Today → Start consultation | 1 | 0 | 1 | – | No identity step |
| Note: complaint, subjective, objective, assessment, plan | 0 | 5 | 0 | 3,263 px | First note field at 1,138 px |
| Add one diagnosis | 4 | 1 | 2 | 0 | A separate page |
| Reconcile medicines | 2 | 0 | 2 | – | A separate page |
| Add one medicine (dialog) | 6 | 1 | 0 | 0 | Chips for frequency and duration |
| Order a test, set follow-up, advice | 5 | 2 | 0 | 3,514 px | Orders in a closed section near the bottom |
| Authorise with step-up | 4 | 0 | 2 | 1,071 px | Returns to the top of the page |
| Sign the note | 2 | 0 | 0 | 829 px | Within the 5-minute window |
| End consultation | 1 | 0 | 0 | – | No dialog |
| Addendum after signing | 2 | 2 | 0 | 2,548 px | Signed as version 2 |

**One simple visit: about 24 clicks, 9 typed fields, 5 route changes.**

**Vertical space, with nurse vitals present.** App header to 60 px; identity banner 84 to 176 px; buttons, hint and heading 192 to 360 px; section chips 368 to 424 px; the first section card at 440 px (49 % of a 900 px screen, 57 % of 768); the first note field at 1,138 px. At 1024 the Chart and Checklist panel is at 4,104 px. Page height 4,801 px at 1440 and 5,347 px at 1024.

**Works well.** Autosave says so. A second tab on the same note is told. A second open consultation is challenged. Amoxicillin for a penicillin allergy is blocked, with a logged override. Signing restates the patient. Signed notes are read-only and addenda are new versions.

| Gap | Finding |
|---|---|
| A free-text brand was authorised for an allergic patient with "No alerts" | F-01 |
| Identity not pinned | F-02 |
| Stale text after a page hop; a quick reload loses the last edit | F-03, F-26 |
| End with an unauthorised prescription | F-04 |
| Escalated patient last on Today; no tokens there | F-07 |
| "Identity confirmed" pre-ticked | F-09 |
| The sign bar covers fields | F-10 |
| Length, route changes, chart far away on a tablet | F-11 |
| Step-up returns to the top | F-25 |
| The print leaves out vitals, allergies, diagnosis | F-27 |
| Drug interaction beyond one allergy class, paediatric dosing, pregnancy | Untested |

## Owner or clinic manager

**Gold path.** One screen: today's bookings, arrivals, waits and who is behind; the week's visits per doctor, no-shows, new against returning; and money.

**Observed.** The board's "By doctor" view gives live counts per state per doctor and a waiting-room table. Settings → Practice → Pilot metrics gives four 14-day numbers. Nothing shows visits per day, average wait, consultation length or revenue; the owner dashboard is ticketed for phase 4.

**Decisions possible today:** rebalance a queue now; see who is waiting too long now. **Not possible:** staffing, hours, which doctor is overloaded across a week, whether no-shows are falling. One pilot metric always reads "0 s, target met" (F-29).

## Administrator

There is no separate administrator product: it is Practice settings, for the owner doctor and the practice admin.

| Task | Result |
|---|---|
| Invite staff | A three-step wizard; step 1 is clear. Not submitted: untested |
| Change a person's role | Absent |
| Offboard | The dialog explains, needs a reason, asks what happens to a doctor's booked patients. Offered on the owner's row (F-30). Not submitted |
| Doctors and licences | Owner attestation with a date; a "Correct licence" action |
| Hours, holidays, leave | Forms exist. Not submitted |
| Formulary | 23 generic rows, no brands, marked as a draft (F-01) |
| Audit log | Filters, patient search, CSV export. Half the action names are raw codes (F-31) |
| Queue displays | A one-time link; revoke exists |
| Data export | A placeholder that says so |
| Nurse intake switch | 4 clicks; no confirmation; immediate (F-39) |

## Cross-role handoffs

| Handoff | What the receiver sees | Gap |
|---|---|---|
| Desk → nurse | A card "With nurse"; a nurse queue row with wait and token | None found |
| Nurse → doctor | Vitals with who measured them; the allergy in the banner; the escalation text on the Today row | Complaint missing (F-16); escalation not prioritised (F-07) |
| Doctor → desk, during the consultation | "Called to Room 3" and a "Consult done" button | The desk can close the visit under the doctor (F-05) |
| Doctor → desk, after | The card moves to Done | Follow-up, prescription state and billing are invisible (F-06, F-04) |
| Two people, same note | The second writer is told and keeps their text | Good |
| Two people, same visit state | The desk's action wins silently | F-05 |
| A screen left open | The note can show stale text after a page hop | F-03 |

## The arbiter's reading

The front desk is already efficient by count and should be protected. The consultation is where the product costs a doctor most, and where the two safety gaps sit. The handoffs work forwards and fail backwards: nothing can be undone, and what the doctor decides (follow-up, an unfinished prescription) does not come back to the desk.

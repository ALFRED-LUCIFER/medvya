# Reviewer B · Rebuttal

Audit only. My original report is unchanged. For this round I read Reviewer A's findings A-01 to A-09 and section 8, Reviewer C's density, shell and banner sections (found by search, not the whole 850 lines), and five of the 21 concept screenshots: `doctor-1440`, `doctor-1024`, `doctor-375`, `desk-1440`, `desk-768`. I did not open the owner page or the dialog states, so I say nothing about them.

## 1 · Is A right that behaviour comes before layout?

**Agree.** A's top findings are things the screen does wrong; mine are things the screen does slowly.

- A-02 (a brand typed as free text skips the allergy check), A-04 (ending with an unauthorised draft prescription), A-06 (forward-only status, the desk can close a doctor's consult) and A-07 (follow-up never reaches the desk) can each harm a patient or lose a visit. Chrome height cannot.
- A measured a new walk-in at 6 clicks and 3 fields. That is evidence the board works despite its 452px of chrome. My B-02 is real, but it is a cost per glance, not a blocker.

Which of my P1s still belong before the pilot:

| Mine | Before pilot? | Why |
|---|---|---|
| B-03 banner not sticky | **Yes** | Same defect as A-01, found independently by both of us. A's NIST citation makes it stronger than I rated it |
| B-01 sign bar covers the form | **Yes, the small version** | Bottom padding, `scroll-padding-bottom`, and a one-row bar. A few lines of CSS. It hides focused controls today |
| B-02 board chrome | **No, demote to P2 for sequencing** | Do only the free part now: date out of the title so the actions stay on the title row (about 60px back, and it removes the header jump on load) |

The full layout pass on the consultation comes after A-01 to A-04, as A proposes.

## 2 · The arbiter's prototype

**Agree with conditions.** The consultation concept is the best thing proposed in this audit. The desk list is right in principle and unproven in detail.

What is strong (`doctor-1440.png`):

- One-row patient strip with the allergy as the only saturated element on the screen. First field at about 340px, against 589px today.
- "Saved 10:42" and "4 to finish before signing" in a fixed footer. This answers my B-01 and my autosave point at once.
- The chart panel stays open. Prescription sits inline, so the route hop behind A-03 goes away.

What is weak or unproven:

| Issue | Evidence | What I would require |
|---|---|---|
| The 56px rail has four unlabeled icons at every width | all five screenshots | This is my B-12 made permanent. Labels under the icons, or keep the labelled sidebar outside the consultation |
| "My queue" shows other patients' names and "Nurse flag: chest pain" on a screen the patient in the room can see | `doctor-1440.png` | Default to the collapsed form the 1024 layout already uses ("Next A-07 · 3 waiting"). Open on demand |
| The strip is clipped at 375: "DOB 12 Mar 19" is cut off | `doctor-375.png` | The identity strip must wrap, never clip |
| One short allergy fits. Three allergies, or a long one, are not shown | `doctor-1440.png` | Design the worst case before building: show a count plus the first two in words, never an ellipsis alone |
| The remove-medicine "×" is a bare glyph of about 16px | `doctor-1440.png` | 44px target, and an undo |
| Desk list: every row repeats its group name as a status chip | `desk-1440.png` | Drop the status column or the grouping, not both |
| Desk list: "Booked" comes first | `desk-1440.png` | With 20 morning bookings, the waiting patients fall below the fold. Order by who needs action: waiting, with nurse, in consult, then booked, then done (collapsed) |
| Desk list: "Call in" is about 30px tall at 1440 | `desk-1440.png` | See section 4. With A-06 (no undo) this is a mis-tap that cannot be reversed |
| "Registration incomplete" is still on 5 of 9 rows | `desk-1440.png` | Same noise as today. Move it to the "Needs the desk" panel, which already counts it |
| Nine rows of data prove nothing about a 40-patient day | – | Test with 40 rows and long names before committing |

**14px body and 40px rows.** Partly right. 14px is already the build's real body size (21,593 of about 35,000 text elements), so this is not a regression, and it suits data rows read on a monitor. It is wrong for two things: the text a doctor types and re-reads in the note (use 16px, which also avoids the phone browser's zoom-on-focus), and sentences of guidance (15px). 40px rows are fine for a mouse. The switch to 48px must key on `any-pointer: coarse`, not `pointer: coarse`, because a reception PC with a touchscreen reports a fine primary pointer.

**Kanban to list.** Agree with conditions. The list shows 9 patients in the space where the board shows 2 cards, ends the name wrapping (B-05) and works on a phone (B-06). The board's one advantage, seeing flow as position, is weak here because two of six columns are usually empty. Conditions: the row order above, a 40-row test, and A-06's undo landing first. Replace the board, do not keep both views.

## 3 · C's "no shell change" against my top bar and rail proposal

**Reconciled; I disagree only with the arbiter's version.** C's own blueprint asks for labels under the rail icons, a user-collapsible sidebar, a 56px header and an account menu. That is the same list as my 11.1. I read C's guardrail as "keep the navigation model and the breakpoints", and I accept that: sidebar, rail and bottom tabs stay.

What changes is the contents of the top bar (Language and Sign out into an account menu) and its height. I will take C's 56px over my 48px; the 8px is not worth an argument. The arbiter's permanent 56px unlabeled rail is a shell change, and I do not support it outside the consultation workspace.

## 4 · Where compact density becomes unsafe

**Agree with C's model, with a sharper line.** C's rule (comfortable by default; compact opt-in, pointer-only, 1024px and wider, reference tables) is correct. Compact is unsafe:

1. On any control that changes a visit, a prescription or a record. A-06 shows these moves cannot be undone today.
2. On any row whose job is choosing a patient. C lists the patient directory as compact. I disagree: A-05 shows same-name rows are already confused, and those rows need more information (date of birth, last visit), not less height. Use 40px there, with the whole row as the target.
3. On medicine lines (dose, frequency, days). A misread line is a prescribing error.
4. Wherever two different actions sit in adjacent rows closer than 8px.
5. On any device where `any-pointer: coarse` is true, whatever the width.
6. On destructive actions inside compact tables (offboard, remove). Keep 44px or require a confirm.

Compact is fine for the audit log, formulary, staff list, timeline and day sheet, as C says. WCAG 2.5.8's 24px is the floor everywhere.

## 5 · One-screen consultation and A's safety concerns

**Agree with conditions.** The layout guarantees I require:

1. The patient strip is pinned on the consultation, its sub-views and nurse intake. The allergy is in words, never collapsed, and repeated in the add-medicine and sign dialogs (A says those exist; keep them).
2. Sign note and authorise prescription stay two separate, deliberate actions in a fixed footer. Never inside a row, never moving. Disabled with a stated reason, as now.
3. Ending with a draft prescription or unplaced orders opens a dialog (A-04). One screen must not make ending easier than finishing.
4. One scroll container. A focused control is never under sticky chrome. Sticky chrome is at most 30 % of the height (C's budget).
5. The allergy and interaction result sits beside the medicine line. A free-text line needs an acknowledgement that names the allergy (A-02).
6. Problems and reconciliation open in the side panel, not on another route. That removes the cause of A-03.
7. "Identity confirmed" is never pre-ticked (A-10).

## 6 · Does anything in A change my motion answer?

**The answer stands; two items move earlier, as text.**

- A-03 and A-15 make save status a pre-pilot item. That is my item B: "Saving…", then "Saved 10:42", then "Not saved, retrying". It is a truthful sentence, not an animation.
- A-06 (the desk closes a consult and the doctor's screen shows nothing) makes a live-change notice a pre-pilot item. That is my item A: a tint plus an "Updated" tag, with no movement.

Animation in the sense the founder asked about is still not needed now.

## 7 · What a doctor should not be offered at 375px

**A product decision; my recommendation.** The phone surface for clinicians is planned as the native app, and the web app at 375 is a fallback.

- Do not offer: writing the note, prescribing, authorising, signing, medicine reconciliation, merge review, full registration, schedule and leave editing, settings.
- Offer: Today, the queue as a list, the chart summary (read-only, allergy first), patient search, notifications, quick registration.
- A blocked screen says "Open this on a tablet or computer", with the patient's name. It must not say "Page not found" (A-21) or show a clipped layout.

## Vote and non-negotiables

**My vote is still B as the destination. I adopt A's order.** Fix A-02, A-01/B-03, A-04, A-03, A-06 (undo), A-07, A-08 and my small B-01 fix first. Then do B in C's small steps, with the arbiter's consultation layout as the target and the conditions above. Not C.

Five non-negotiables:

1. The patient strip is pinned on every clinical screen, with the allergy in words, never truncated or clipped.
2. Nothing focused or being typed is ever covered by sticky chrome, and sticky chrome stays within 30 % of the height.
3. Every control that changes a visit, record or prescription is 44px on any touch-capable device and on the reception list. Compact is for read-only reference tables.
4. One component per pattern (patient banner, view switch, page header). Colours, font and the icon-plus-words status rule do not change.
5. System status is truthful and in words (saved, updated by someone else, offline) before any animation is added. No decorative motion.

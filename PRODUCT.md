# Medvya: product brief

Who uses Medvya, what each person must be able to do all day, and the principles every screen follows. The rules for building screens are in [DESIGN.md](DESIGN.md). Code rules are in [AGENTS.md](AGENTS.md).

Source: the 13 Nov 2026 product UX audit, [blueprint](docs/product-ux-audit/08-design-system-blueprint.md) sections 1 and 11, and the [arbitration rulings](docs/product-ux-audit/07-debate-and-arbitration.md). This file states targets. Where the build does not meet one yet, [DESIGN.md](DESIGN.md) names the ticket that makes it true (epic MDV-P1-E12, [programme](docs/05-delivery/ux-programme.md)).

## Who uses it

| Person | Must be able to do, all day | Main device |
|---|---|---|
| Doctor (and owner doctor) | See my day and my queue, read the chart, write the note, prescribe, order, sign, see a nurse escalation | Tablet or computer in the room; phone to look |
| Nurse | Work the nurse queue, record intake and vitals, escalate to the doctor | Tablet |
| Receptionist | Register, book, check in, give a walk-in a token, run the queue and the board | Desk computer |
| Owner | See the clinic today; manage the practice in Settings | Any, read on a phone |
| Practice admin | Front desk work plus Settings, staff and the audit log | Desk computer |

The synthetic cast is in [personas](docs/00-overview/personas.md).

## Principles

1. **Identity before content.** On any screen that can change a patient's record, name, age and sex, MRN, date of birth and allergy state are visible without scrolling, in one form everywhere.
2. **Words, then icon, then colour.** Every status has a text label; colour only reinforces.
3. **Unknown is not none.** "Not recorded" and "None known" never look alike.
4. **No tick the user did not earn.** A checklist item, a "Saved" or a metric reports something that happened.
5. **Fewer route changes, never fewer deliberate acts.** Sign, authorise, place orders and end stay separate.
6. **Density is chosen by task.** A write is never compact.
7. **The library is the only place a pattern is styled.** A feature composes.
8. **AI output is provisional until accepted:** violet, dashed, the word "draft". Never the primary colour.

## Device strategy

Phone is under 768 px, tablet 768 to 1279 px, desktop 1280 px and up.

| Task | Phone | Tablet | Desktop |
|---|---|---|---|
| Doctor: my day, queue, read a chart, see an escalation, sign an addendum | Yes | Yes | Yes |
| Doctor: write a note, prescribe, authorise, order, reconcile, sign a new note | No: "Open this on a tablet or computer" | **Designed for** | Yes |
| Nurse: intake and vitals | No, until the strip is pinned and one group shows per screen | **Designed for** | Yes |
| Reception: queue, walk-in, quick registration, check-in | Queue list and quick registration | Yes | **Designed for** |
| Reception: full registration, schedule editing, merge | No | Yes | **Designed for** |
| Owner: clinic today | Yes (read) | Yes | Yes |
| Settings, staff, audit | No | Read | **Designed for** |

**The phone rule (ruling 5).** A phone is for looking and for urgent small acts. Below 768 px a doctor gets My day, the queue, a read-only chart, search, signing an addendum and seeing an escalation. Composing or authorising a prescription, reconciliation, orders, signing a new note, merge approval and settings show "Open this on a tablet or computer". Reception gets the queue list and quick registration. Tablet is a first-class device for the nurse and the doctor. Built by MDV-P1-098 (#1194), which needs clinical sign-off.

Not yet tested on physical devices. The tablet claims need a real tablet before the pilot (MDV-P1-108, #1222).

## Non-negotiables

1. The patient strip is pinned on every clinical write screen, with the allergy in words, never clipped or collapsed to a colour.
2. Sign the note, authorise the prescription, place orders and end the consultation stay four separate, recorded acts. One identity step-up may cover them.
3. The walk-in path must not get longer: at most 6 clicks for a new patient and 3 for a returning one. Re-measure after every desk change.
4. No tick, Saved, or number the system did not earn.
5. Every control that changes state is comfortable, 44 px (48 px rows on a coarse pointer). Compact rows only on the audit log, formulary, order sets and staff tables.
6. No palette or typeface change.
7. No second component library, no new state library.

## Do not change without clinical validation

The full list is in [DESIGN.md](DESIGN.md#do-not-change-without-clinical-validation). In short: allergy rules and wording, the unknown / none / not recorded distinction, the Add medicine dialog and safety-check wording, the sign dialog and the separation of sign, authorise and orders, the duplicate block and the identity confirmation at check-in, the walk-in path, consultation section order and required fields, prescription line fields and units, vitals ranges and flags, and what a receptionist can see of clinical data.

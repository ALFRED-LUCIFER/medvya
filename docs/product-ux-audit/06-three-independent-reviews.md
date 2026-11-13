# 06 · Three independent reviews

## How the reviews were actually run

| | Reviewer A | Reviewer B | Reviewer C |
|---|---|---|---|
| Role | Clinical product and workflow | Visual and interaction design | Design system and SaaS architecture |
| What it really was | A general-purpose Claude subagent with a written brief. **No skill named "medical-product-ux-director" is installed**, so none was used | A general-purpose Claude subagent that read the Impeccable skill (critique, audit, operate, layout, typeset, animate references) and ran its detector once | A general-purpose Claude subagent that read the UI UX Pro Max skills (ui-ux-pro-max, design-system, ui-styling) and ran 11 of their read-only searches and the token validator |
| Model | Claude (the session's model) | same | same |
| Ran in parallel | Yes: all three were started together and worked at the same time | | |
| Independence | Each was told not to read the other two reports. Each wrote its report before seeing the others. They shared the same captured screenshots and metrics, which is a common-evidence limit | | |
| Own browser work | 10 Playwright walkthrough scripts on the local stack, 133 screenshots, then one more run in the rebuttal | 130 state screenshots (focus, errors, zoom, right-to-left), read-only | 30 screenshots of the platform console with a mocked API |
| Full report | [reviewer-a-clinical-workflow.md](reviews/reviewer-a-clinical-workflow.md) | [reviewer-b-visual-interaction.md](reviews/reviewer-b-visual-interaction.md) | [reviewer-c-design-system.md](reviews/reviewer-c-design-system.md) |
| Rebuttal (second round, after reading the others) | [reviewer-a-rebuttal.md](reviews/reviewer-a-rebuttal.md) | [reviewer-b-rebuttal.md](reviews/reviewer-b-rebuttal.md) | [reviewer-c-rebuttal.md](reviews/reviewer-c-rebuttal.md) |

These are three passes by the same underlying model with different briefs and methods. They are not three people, and none is a clinician, a receptionist or a practising designer. Agreement between them is weaker evidence than agreement between independent humans.

Limits each reviewer reported:

- **A:** no clinician reviewed the work. Arabic, Hindi, phone widths, offline, the real Entra step-up and UAE rules were not walked. Staff invite, offboarding, leave and holidays were opened and not submitted.
- **B:** the skill's two isolated sub-reviews ran as one pass (its report says "degraded"). About 55 screenshots were opened by eye; the rest are covered by metrics. Nurse intake was not reviewed (the setting was off). No screen reader, no real devices, no dark mode.
- **C:** NISTIR 7804 could not be read (PDF). WCAG wording is paraphrased. Its metrics covered doctor and receptionist only, because the capture was still running. The skill's "design system" search returned a marketing-page pattern and a cyan palette, which it rejected.

## Reviewer A · clinical product and workflow

**Headline.** The model underneath is careful: states, permissions, safety checks and conflict handling are server-side. The serious problems are gaps at the seams.

Top findings (its IDs, with the consolidated ID):

| A | Severity | Finding | Consolidated |
|---|---|---|---|
| A-02 | P0 | A free-text medicine skips the allergy check; a penicillin-class brand was authorised for a penicillin-allergic patient with "No alerts" | F-01 |
| A-01 | P1 | Identity and allergy banner not pinned on the consultation or nurse intake | F-02 |
| A-03 | P1 | Note shows old or empty text after a quick page change, though the server saved it | F-03 |
| A-04 | P1 | End consultation is one click with an unauthorised draft prescription; no reopen | F-04 |
| A-06 | P1 | Visit status only moves forward; the desk can finish a visit; no undo | F-05 |
| A-07 | P1 | The doctor's follow-up never reaches the desk | F-06 |
| A-08 | P1 | A nurse escalation does not reorder Today | F-07 |
| A-05 | P1 | Same-name patients cannot be told apart in pickers | F-08 |
| A-10 | P1 | "Identity confirmed" is pre-ticked | F-09 |
| A-13 | P2 | First note field at 1,138 px; about 24 clicks and 5 route changes for a simple visit | F-11 |

Measured strengths: a new walk-in reaches a token in 6 clicks and 3 fields without scrolling; a returning patient in 3 clicks. Duplicate registration is blocked. A two-tab note conflict is detected. The waiting-room display shows tokens only.

**First vote: A, surgical fixes**, with one bounded layout pass on the consultation. Subscores: workflow efficiency 15/25, clinical safety and trust 11/20, information architecture 9/15 (all provisional).

## Reviewer B · visual and interaction design

**Headline.** The foundation is good and should be kept; the page compositions are the problem. At 1024×768 the share of the first screen left for real work is 3 % on the consultation, 38 % on the schedule and 41 % on the reception board.

| B | Severity | Finding | Consolidated |
|---|---|---|---|
| B-01 | P1 | Sticky "Review and sign" bar covers the form; 2 of 45 tab stops hidden | F-10 |
| B-02 | P1 | Reception board: first card at 452 px from 768 to 1440 | F-12 |
| B-03 | P1 | Patient banner scrolls away | F-02 |
| B-04 | P2 | Consultation is one 3,611 to 4,941 px column; the prescription table scrolls sideways inside itself | F-11 |
| B-05 | P2 | Board cards wrap; the time spills outside the card | F-14 |
| B-06 | P2 | On a phone the board is six 156 px columns | F-12 |
| B-07 | P2 | Language, My devices and Sign out sit permanently in the top bar | F-18 |
| B-08, B-09 | P2 | Three patient headers, five view-switch styles | F-19 |
| B-13 | P2 | Registration errors: focus stays on the button, errors off screen at 375 | F-23 |
| B-11, B-12 | P2 | Type scale drift; icon-only rail | F-21, F-22 |

28 findings, none P0. **Motion:** not needed now; about six small functional uses later; never page transitions or motion during clinical entry (details in [08](08-design-system-blueprint.md#12--motion)).

**Vote: B**, limited to layout, density and component consolidation. Subscores: visual and design system 9/15, responsiveness and tablet 5/10, accessibility 7/10 (provisional).

## Reviewer C · design system and architecture

**Headline.** The foundations are sound, but the shared library stops at primitives, so features rebuild the layer above it 3 to 10 times.

| C | Severity | Finding | Consolidated |
|---|---|---|---|
| C-02 | P1 | Three patient identity headers; the allergy looks different in the chart and the consultation | F-02, F-19 |
| C-01 | P2 | Input and Select default to 36 px; 164 call sites patch them | F-20 |
| C-04 | P2 | 10 `Message`, 5 `Field`, 97 inline cards, about 12 chips, 20 view switches | F-19 |
| C-03 | P2 | 103 raw hex colours in the shared primitives | F-20 |
| C-07 | P2 | "Patients" and "Today" each name several screens | F-32 |
| C-12 | P2 | No error summary; three error styles | F-23 |
| C-05 | P2 | Documented and rendered type scales disagree | F-21 |
| C-06, C-13 | P2 | No density mode; 2 of 19 tables sortable | F-24 |
| C-09 | P2 | Icon-only rail from 768 to 1279 | F-22 |
| C-14 | P2 | "Offboard" offered on the owner's own row (server not tested) | F-30 |

24 findings, none P0. One correction to the brief: Settings is already grouped into five groups with sub-tabs, not 17 flat tabs.

**Vote: B**, delivered as about 12 small pull requests, 6 before the pilot. Subscores: visual and design system 8/15, information architecture 8/15 (provisional).

## What changed in the second round

Each reviewer read the other two reports and the arbiter's concept, then answered seven questions.

| | First vote | After the rebuttal |
|---|---|---|
| A | A, surgical | **B, sequenced**: reviewer B's scope only; safety behaviour first |
| B | B | **B as the destination, in A's order**; its own B-02 drops to P2 |
| C | B, 12 pull requests | **B, A-first**: "I ranked it wrong." Only three system changes stay before the pilot |

Reviewer A also completed its inferred step: the free-text prescription was authorised end to end (F-01).

The disagreements that remained, and the arbiter's rulings, are in [07-debate-and-arbitration.md](07-debate-and-arbitration.md).

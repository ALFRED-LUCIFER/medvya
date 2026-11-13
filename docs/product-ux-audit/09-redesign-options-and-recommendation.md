# 09 · Redesign options and recommendation

## Recommendation

**Option B, cohesive modernisation, delivered in a safety-first order.** A full reset is not warranted anywhere.

## The three options

### A · Surgical improvements

Keep every screen where it is. Fix the highest-impact defects one by one.

| | |
|---|---|
| What it includes | F-01 to F-09 (behaviour), a pinned patient strip, the sign-bar fix, the Azure redeploy, small copy fixes |
| User benefit | The dangerous and trust-breaking gaps close. Screens stay familiar |
| Screens touched | Consultation, Today, front-desk cards, pickers |
| Clinical safety | Strongly positive. This is where the safety gain is |
| Effort | M to L in total: nine behaviour fixes with tests and mutation checks, several with backend work |
| Regression risk | Low: each change is small and maps to one journey step |
| Technical dependency | None new |
| Consistency gain | Low. Three patient headers, 20 view switches and 350 size patches remain |

### B · Cohesive modernisation

Keep the brand, the routes and the logic. Unify the design system in `packages/ui`, reorganise navigation, and redesign the high-friction screens: the consultation workspace, the front desk, a small owner page.

| | |
|---|---|
| What it includes | Everything in A, then [08](08-design-system-blueprint.md): one patient banner, correct defaults, account menu, page header, the consultation workspace, board chrome, list view, field and error pattern, tone tokens, table density, navigation names |
| User benefit | Doctor: first field at about 160 px, no route hops, chart beside the note. Receptionist: the queue on the first screen, undo, the doctor on every row. Owner: a page that says what needs attention. Tablet: a designed layout |
| Screens touched | All, mostly through the library; four screens by hand |
| Clinical safety | Positive if the seven guarantees hold ([07, ruling 3](07-debate-and-arbitration.md)). Neutral to negative if layout work goes before the behaviour fixes |
| Effort | L overall, in about 20 small changes. The consultation layout touches 13 test files |
| Regression risk | Medium. Controlled by keeping J1 to J6 green per change and one change per pattern |
| Technical dependency | One shadcn component (dropdown menu). No new library |
| Consistency gain | High, and kept by lint rules and a CI count |

### C · Selective or full reset

New information architecture and new screens, for the whole product or for the consultation only.

| | |
|---|---|
| User benefit | In theory the cleanest result |
| Clinical safety | Negative in the short term: careful, tested rules (allergy handling, signing, conflict detection, duplicate blocking) would be rebuilt |
| Effort | Very large for one person |
| Regression risk | High: 19 test files reference the board, 13 the consultation |
| Consistency gain | High, eventually |
| Is it warranted? | **No.** The evidence shows composition and duplication problems on a sound base: 615 pages with no overflow, axe nearly clean, a 6-click walk-in. Even the consultation page is 185 lines of composition over hooks that work. Nothing found needs a different structure |

## Weighted comparison

Scores 1 (poor) to 5 (good). The weights reflect a clinical product before a pilot with one developer.

| Criterion | Weight | A | B, safety-first | C |
|---|---|---|---|---|
| Patient safety and trust gained | 30 % | 5 | 5 | 2 |
| Daily efficiency gained (doctor, desk) | 20 % | 2 | 4 | 4 |
| Risk to the pilot date and to tested behaviour | 20 % | 5 | 4 | 1 |
| Consistency that lasts | 15 % | 1 | 5 | 5 |
| Fits a one-person team | 15 % | 5 | 3 | 1 |
| **Weighted total** | | **3.8** | **4.4** | **2.5** |

The numbers are judgements, not measurements. They show the shape: A and B tie on safety, B wins on what staff feel all day and on not drifting again, and C loses on risk.

**Why A loses.** It leaves the consultation long, the chart far from the note on a tablet, and every pattern duplicated. The clinical reviewer, who first voted A, changed to B once the order was settled.

**Why C loses.** It spends the most to replace things that work, and it puts the safety rules at risk to fix layout.

## What improves, by person

| Person | With the safety-first part | With the rest of B |
|---|---|---|
| Doctor | Patient and allergy always in view; a brand name cannot bypass the allergy check silently; the note does not show stale text; End warns about an unauthorised prescription; escalated patients come first | One workspace: first field near the top, diagnoses inline, chart beside the note, a footer that never covers a field |
| Receptionist | Undo; "left without being seen"; date of birth in every picker; follow-ups to book | The queue on the first screen; the doctor on every row; a list view to try; errors that point to the field |
| Nurse | Identity pinned on intake; one confirmation for flagged values; the complaint reaches the doctor | A tablet layout designed for intake |
| Owner | A check-in metric that is true | "Clinic today": counts that exist and a short list of things to act on |
| On a tablet | A header that fits (after the redeploy) | Labelled rail, chart as a drawer, 44 px controls by default |

## Trade-offs accepted

- The visible redesign comes after the invisible fixes.
- The board stays for the pilot; the list is added later as a choice.
- Renames wait for a training boundary.
- Phones lose prescribing and signing a new note.
- Compact rows are limited to four reference tables.

## Likely sequence

Relative order only; no dates.

1. **Safety and trust** (before any pilot prescribing): F-01; F-02 with F-10; F-03 and F-26; F-04 and F-09.
2. **Desk and handoff:** F-05; F-06, F-07, F-16; F-08. Redeploy Azure dev from `main` (F-13).
3. **Three system changes:** control defaults with the codemod; table labels; then the bounded consultation layout pass (F-11) and the board chrome (F-12, F-14).
4. **Pilot starts.** Watch the open assumptions in [11](11-pilot-doctor-validation-plan.md).
5. **After the first pilot weeks:** account menu and navigation names; field and error summary; tone tokens and chips; the list view; "Clinic today"; table density; settings sub-navigation.
6. **Later:** skeletons and the small motion items; dark and high contrast; mobile apps, once they bundle.

The tickets are in [10-prioritized-implementation-backlog.md](10-prioritized-implementation-backlog.md).

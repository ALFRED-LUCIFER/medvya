# 05 · Accessibility, responsive behaviour and reliability

**What this is not.** It is not a WCAG conformance audit and makes no claim of compliance. Automated checks and emulated viewports are evidence, not proof. No screen reader, no physical phone or tablet, and no person with a disability took part.

Sources: `evidence/local-metrics.jsonl` (615 pages on current `main`), the Azure captures, reviewer B's state captures (`screenshots/local-stack/states/`, `evidence/reviewer-b-*.json*`), and reviewer A's walkthroughs.

## 1 · Responsive behaviour

### Sideways overflow

| Environment | 1920 | 1440 | 1024 | 768 | 375 |
|---|---|---|---|---|---|
| Local (`main`), 123 role-screens | none | none | none | none | none |
| Azure dev (older deploy), 54 role-screens | none | none | none | **every page 939 px wide** | **every page 511 px wide** |

On Azure the top bar's buttons (Search, Language, Change facility, My devices, Sign out) do not fit; the page scrolls sideways and Sign out is off screen. Evidence: `screenshots/azure-dev/owner-doctor/today/768-default.png` and `375-default.png`. Fixed on `main`: `screenshots/local-stack/doctor/front-desk/375-default.png`. Finding F-13.

### How much of the first screen is working content

Measured by reviewer B on `main`.

| Screen | 1440×900 | 1024×768 | 375×812 |
|---|---|---|---|
| Consultation | low | **3 %** | first field far below |
| Schedule | – | **38 %** | – |
| Reception board | – | **41 %**; first card at 452 px | first card at about 664 px |

Positions from the audit's own capture (doctor role, px from the top of the page):

| Screen | First content at 1440 | at 1024 | at 375 | Page height at 1440 / 1024 / 375 |
|---|---|---|---|---|
| Today | 419 | 419 | 606 | 1,410 / 1,495 / 2,358 |
| Front desk | 345 (first region) | 345 | 509 | 2,074 / 2,168 / 2,469 |
| Schedule | 168 | 168 | 264 | 992 / 1,022 / 1,889 |
| Consultation (ended visit) | 138 (banner) | 138 | 186 | 3,611 / 4,093 / 4,941 |
| Patient 360 | 168 | 168 | 196 | 3,241 / 3,805 / 5,129 |
| Register patient | 164 | 228 | 268 | 1,040 / 1,504 / 2,080 |
| Settings · profile | 385 | 385 | 497 | 1,226 / 1,226 / 1,544 |

Tallest pages: the audit log (5,548 px at 375, 6.8 screens), Patient 360 (5,129 px), the consultation (4,941 px).

### By breakpoint

| Width | What happens | Problems |
|---|---|---|
| 1920 | Sidebar and fluid content | No maximum width: paragraphs reach 1,160 px (F-21). The consultation's floating sign bar covers section 3 (F-10) |
| 1440 | Same | Board card names and "Start consultation" wrap (F-14) |
| 1024 (tablet landscape, touch emulation) | Icon rail; two columns where they exist | No labels on the rail (F-22). The consultation's chart panel drops to the bottom (F-11). Board columns about 150 px; the time sits outside the card (F-14) |
| 768 (tablet portrait) | Icon rail; one column | Same rail problem. The schedule's booking panel becomes a drawer, which works |
| 375 (phone) | Bottom tabs with "More" | The board is six narrow columns in a sideways scroller (F-12). The prescription table scrolls sideways inside itself and hides dose columns (reviewer B, B-04). 10 of 19 tables rely on sideways scroll (reviewer C) |

Orientation changes, the on-screen keyboard and real touch were not tested. Printing: print views exist for the prescription, the investigation slip, the note and the day sheet; none was sent to a printer.

### Device strategy

Set out in [08, section 11](08-design-system-blueprint.md): tablet is a designed target for the doctor and the nurse, desktop for the desk and settings, and a phone is for looking and small urgent acts. Prescribing and signing a new note are not offered on a phone.

## 2 · Accessibility

### Automated: axe-core on 615 pages (`main`)

| Result | Pages |
|---|---|
| No violation | 610 |
| `page-has-heading-one` (moderate) | 5: the signed note's print view at all five widths |

Reviewer B's separate run reports the same order of result. This is a strong automated floor, and it is the product of the project's own axe tests in CI.

### Manual and measured, by WCAG 2.2 success criterion

Normative criteria (levels A and AA) where the audit has evidence. "Holds" means on the screens checked, not everywhere.

| Criterion | Result | Evidence |
|---|---|---|
| 1.3.1 Info and Relationships (A) | One page lacks an `h1`; 4 scrollable table regions lack a label (reviewer C) | F-27, F-24 |
| 1.4.1 Use of Color (A) | Holds: status and allergy carry words and icons | Reviewers A and B |
| 1.4.3 Contrast (Minimum) (AA) | Holds for every text pair reviewer B computed, except disabled labels (which the criterion exempts) | Reviewer B section 3 |
| 1.4.4 Resize Text, 1.4.10 Reflow (AA) | Holds at 200 % and 400 % without sideways scrolling; at 400 % the pinned bars take about half the screen | Reviewer B states |
| 2.1.1 Keyboard (A) | Holds on Today, search and notifications on Azure: 12 tab stops in a sensible order | `screenshots/azure-dev/owner-doctor/today/1440-focus-ring.png` |
| 2.4.1 Bypass Blocks (A) | Holds: "Skip to main content" is the first stop | `screenshots/azure-dev/owner-doctor/today/1440-focus-skip-link.png` |
| 2.4.3 Focus Order (A) | Holds. After the search dialog closes, focus returns to the search button | Azure check |
| 2.4.7 Focus Visible (AA) | Holds: 2 px solid outline on every stop checked. The ring fades in over 150 ms (F-47) | Azure check |
| **2.4.11 Focus Not Obscured (Minimum) (AA)** | **At risk:** 2 of 45 tab stops on the consultation were fully hidden under the sign bar | F-10 |
| 2.5.8 Target Size (Minimum) (AA) | Mostly holds. About 6 % of controls on touch widths are under 24 px: switches 32×18, native radios and checkboxes 16×16, name links 19 px tall. Exceptions (inline links, label hit areas) were not assessed one by one | F-33 |
| 3.2.3 / 3.2.4 Consistent Navigation and Identification (AA) | Weak: one word names several screens | F-32 |
| **3.3.1 Error Identification (A), 3.3.3 Error Suggestion (AA)** | **Weak:** after an empty submit on registration, focus stays on the button and at 375 the errors are off screen; no summary | F-23 |
| 3.3.4 Error Prevention (Legal, Financial, Data) (AA) | Weak: visit moves, End consultation and the nurse-intake switch are single taps with no undo or confirmation | F-04, F-05, F-39 |
| 3.3.7 Redundant Entry (A) | Weak across two users: the nurse's complaint is typed again by the doctor | F-16 |
| 4.1.3 Status Messages (AA) | Autosave "Saved" is a status region; live board changes were not checked with a screen reader | Untested |

Project rule, stricter than WCAG: 44 px targets. About 10 % of controls on touch widths are under 44 px in one dimension, 81 of them the skip link (32×24, visible only on focus).

Right-to-left: Arabic renders mirrored on the screens reviewer B captured, and the project's own tests check Arabic with axe. The audit did not walk any workflow in Arabic or Hindi.

Advisory guidance (not normative): the NHS design system's error summary and tag patterns, Vercel's Web Interface Guidelines, NISTIR 7804. What each reviewer opened is in their reports.

### Not checked

Screen readers; voice control; high contrast and dark mode (not built, F-42); real touch; cognitive load with real users; the public pages; the mobile apps.

## 3 · Reliability and trust

| Area | Observed | Finding |
|---|---|---|
| Loading | A "Loading…" card, then the layout appears and jumps. Azure pages took 1.4 to 3 s to settle after navigation; Settings once took 7 s and the consultation 6 s on first load (single samples from the capture script) | F-36 |
| Empty states | Honest and specific: "No visits today", "No data yet", "Coming in a later phase" | Good |
| Permission refused | The server refuses correctly. The screen says "Page not found" | F-17 |
| API errors seen | `GET /v1/organisation/request` 404 on the owner's organisation page; two 403s when a nurse opens a consultation, shown as two red lines | F-40, F-28 |
| Autosave | Works and says "Saved". A page hop shows stale text; a reload within about two seconds loses the last edit | F-03, F-26 |
| Concurrent edits | A second tab on the same note is told and keeps its text. Two people changing one visit's state: last tap wins silently | Good; F-05 |
| Duplicate submission | Booking and registration use server idempotency keys (project notes); the audit did not try double clicks | Untested |
| Irreversible actions | Sign has a dialog with identity. Call in, Consult done, End consultation and Send to nurse do not | F-04, F-05 |
| Session | Idle expiry shows a "Sign in again" page that returns to the same route (code). Sign-out works. The clinic picker ignored one click of three on Azure | F-41 (hypothesis) |
| Offline | An offline banner exists in code; not exercised | Untested |
| Live updates | The board and Today show "Live updates"; a change by someone else arrives with no cue | [08, motion](08-design-system-blueprint.md#12--motion) |
| Audit history | Present, filterable, exportable; half the action names are developer codes | F-31 |
| Truthfulness | Two places report something that did not happen: "Identity confirmed" and the "0 s" check-in metric | F-09, F-29 |

## 4 · Verification limits

- Viewports were emulated in desktop Chromium. Nothing here is physical-device validation.
- Azure was captured by resizing a desktop browser window: no touch or mobile emulation there.
- Azure had an empty clinic day, so its screenshots show sparse states.
- Load times are single samples through a script, not a performance test.
- Reviewer B opened about 55 of the screenshots by eye; the rest rest on metrics.

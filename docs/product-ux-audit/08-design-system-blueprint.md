# 08 · Design-system blueprint

This is the arbitrated system. It keeps Medvya's colours, typeface and component base and changes layout, density and where patterns live. The fuller code-level proposal, with file and line evidence, is in [reviewer C's report, section 3](reviews/reviewer-c-design-system.md); the measured layouts are in [reviewer B's report, section 11](reviews/reviewer-b-visual-interaction.md). Where they differed, [07](07-debate-and-arbitration.md) records the ruling used here.

It is a proposal. Nothing here is implemented, and nothing here claims WCAG conformance.

## 1 · Principles

1. **Identity before content.** On any screen that can change a patient's record, name, age and sex, MRN, date of birth and allergy state are visible without scrolling, in one form everywhere.
2. **Words, then icon, then colour.** Every status has a text label; colour only reinforces.
3. **Unknown is not none.** "Not recorded" and "None known" never look alike. The code already holds this line; the system must make it impossible to break.
4. **No tick the user did not earn.** A checklist item, a "Saved" or a metric reports something that happened.
5. **Fewer route changes, never fewer deliberate acts.** Sign, authorise, place orders and end stay separate.
6. **Density is chosen by task.** A write is never compact.
7. **The library is the only place a pattern is styled.** A feature composes.
8. **AI output is provisional until accepted:** violet, dashed, the word "draft". Never the primary colour.

## 2 · Layout grid and widths

| Token | Value | Why |
|---|---|---|
| Top bar | 48 px from 768 px up, 56 px on phones | Today 60 px. Returns space to content |
| Navigation rail (768 to 1279) | 72 px, icon with an 11 px label | Icons alone were not recognisable (F-22) |
| Sidebar (1280 and up) | 232 px, collapsible to the rail by the user | Today a clamp that always resolves to 232 |
| Page gutter | 16 / 24 / 32 px at phone / tablet / 1600 and up | |
| Page header | One 44 px row: title 20/28, secondary text, actions at the end; 12 px gap below | Today 100 to 180 px. Content starts at about 124 px on every page |
| Fluid page maximum | 1,600 px | Lists stop stretching at 1920 (F-21) |
| Reading maximum | 1,152 px; body text lines about 72 characters | Paragraphs reach 1,160 px today |
| Form maximum | 768 px | |
| Side panel | 320 px; 360 px for the consultation chart panel | One value in place of 288, 300, 320 and 340 |

Breakpoints: keep 360, 768, 1280 and 1600. Add **1024** as a named tier for two-pane layouts. Stop using the 360 px `sm:` step for column changes.

## 3 · Navigation model

| | Phone, under 768 | Tablet, 768 to 1279 | Desktop, 1280 and up |
|---|---|---|---|
| Primary | Bottom tabs: up to 4, then More | Labelled rail | Labelled sidebar |
| Account | In More | Avatar button opens the menu | Same |
| Top bar | Clinic name, search icon, bell | Clinic, search field, bell, avatar | Same |
| Patient search | Full-screen sheet | Ctrl+K dialog, opens instantly | Same |

Account menu: name and role, language, my devices, change facility, sign out (last, separated).

| Role | Items, in order |
|---|---|
| Doctor, owner doctor | My day · Patients · Schedule · Front desk · Settings (owner only) |
| Receptionist | Front desk · Schedule · Patients |
| Nurse | Nurse queue · Patients |
| Practice admin | Front desk · Schedule · Patients · Settings |

- "Today" becomes **My day** for doctors and is never the reception board.
- The desk surface is **Front desk** in the menu and the title. "By status", "By doctor" and later "List" are views inside it.
- **Patients** is a real page. A row opens one **Patient record** whose tabs depend on capability: Summary (clinicians), Details (desk), Timeline, Documents.
- **Register patient** is a button on Front desk and Patients, and a search action. It leaves the menu.
- A screen a role may not open says so (F-17).
- Settings keeps its five groups and its `?tab=` addresses; at 1024 and wider it uses a left sub-navigation.

Renames land in one batch, before the first training session or after the first pilot weeks ([07, ruling 6](07-debate-and-arbitration.md)).

## 4 · Density

| | Comfortable (default) | Compact (explicit, per table) |
|---|---|---|
| Control height | 44 px | 36 px |
| Table row | 48 px | 36 px |
| Body text | 14/20 | 14/20, never smaller |
| Typed clinical text | 16/24 | not allowed |

**Comfortable is mandatory** for every control that changes state, every row that picks a patient, medicine lines, sign and authorise, registration, nurse intake, the front-desk queue, and any device that reports a coarse pointer (`any-pointer: coarse`).

**Compact is allowed** only where a person reads and compares with a mouse: audit log, formulary, order sets, staff list.

The trade-off, plainly. A clinical screen wants many facts in view, because scrolling hides the allergy and the last result, and comparing rows across a scroll causes errors. A touch screen wants large targets, because a mis-tap on "Call in" or on the wrong patient is a clinical error too. The system resolves it by task: reading is dense, acting is large. Page chrome (headers, toolbars, empty cards) is cut everywhere, because it serves neither.

WCAG 2.2 sets 24 by 24 CSS pixels at level AA (SC 2.5.8 Target Size (Minimum)); 44 px is the project's own stricter rule. Any row under 44 px needs AGENTS.md section 4 amended in the same change.

## 5 · Spacing, type, radius, elevation

**Spacing (px):** 2, 4, 6, 8, 12, 16, 24, 32, 48, 64. Inside a control 6 to 8; between related controls 8; between fields 16; card padding 16; between sections 24.

**Type** (Manrope; Noto Sans Arabic; a mono face only for MRN, tokens, codes and times). Name the scale the product already renders, so nothing reflows:

| Token | Size / line | Weight | Use |
|---|---|---|---|
| display | 30/36 | 800 | Queue display, print headers |
| title | 20/28 | 700 | Page title in the 44 px header |
| heading | 18/26 | 700 | Section and card titles; patient name in the strip |
| subheading | 16/24 | 700 | Sub-sections; patient name in rows |
| body | 14/20 | 500 | Default, tables, controls |
| body-large | 16/24 | 500 | Typed clinical text, patient-facing pages |
| label | 14/20 | 600 | Field labels, table headers, buttons |
| caption | 12/16 | 600 | Hints, metadata, chips |
| mono | 13/20 | 500 | MRN, token, codes |

Body weight moves from 400 to 500, as the project's own spec says. Bold marks headings and the one thing that identifies a row. Tabular numerals on every number, vital, time and amount. Remove the stray sizes (11, 12.5, 13.5, 15, 17, 22 px).

**Radius:** 6 chips, 10 controls, 12 cards and panels, full for pills and avatars. **Elevation:** border only; one shadow for menus and popovers; one for dialogs, sheets and drawers.

## 6 · Colour and semantic rules

Keep today's palette. Add a **tone layer** to the tokens, so a meaning has one colour pair on web and native, and the contrast test reads from it:

| Tone | Means | Never used for |
|---|---|---|
| danger | Allergy, critical, blocked, destructive | Emphasis or branding |
| warning | Uncertain, attention, incomplete, not checked | "Error" |
| success | Confirmed, done, signed | Primary buttons |
| info | Booked, in progress, neutral system state | |
| brand (teal) | The one next action; the active navigation item | Status |
| ai (violet, dashed) | Provisional AI output only | Anything accepted by a clinician |
| neutral | Everything else | |

Rules:

- Status is never colour alone: label always, icon where it helps.
- The **allergy** has one presentation everywhere: a solid danger block with an icon and the words "Allergy: Penicillin (rash)". "Allergies not recorded" is a warning block with its own words. "No known allergies" is neutral text with a tick. This needs the pilot doctor's approval.
- One primary (filled) button per view: the single next action for the signed-in user.
- No raw hex colours in components (103 in the shared primitives today).
- Text contrast at least 4.5:1, large text and control borders at least 3:1 (WCAG 2.2 SC 1.4.3 and 1.4.11); disabled labels are the current gap.
- Dark and high-contrast themes come only after the tone layer exists.

## 7 · Components

Effort is relative: S hours, M a day or two, L several days.

**Before the pilot**

| Component | Purpose | Replaces | Effort |
|---|---|---|---|
| PatientBanner | One banner: `full`, `strip` (pinned, one row), `desk` (no clinical data). Must not cover focused elements | Three identity headers | M, needs clinical validation |
| Input, Select, Textarea defaults | 44 px in the library | About 350 call-site patches (codemod) | S |
| StickyActionBar (layout footer) | The commit action with status text; reserves its own space | The floating sign bar | S |
| ConfirmDialog use on End consultation | "Authorise now, discard, or end anyway" | Nothing today | S |
| Table `label` required | Names scrollable regions for keyboard users | Optional prop | S |

**Next**

| Component | Purpose | Replaces | Effort |
|---|---|---|---|
| AccountMenu | Language, devices, facility, sign out | Header buttons | S |
| PageHeader (with back and meta slots) | The one 44 px header row | 37 hand-written titles | S |
| Field and ErrorSummary | Label, required mark, hint, error; a summary that takes focus | 5 `Field`, 3 error styles | M |
| Chip / Tag | Tone, icon, size | About 12 chip variants | M |
| Card / Section, PageMessage / EmptyState | Title, intro, actions; busy, error, empty, denied | 97 inline cards, 10 `Message` | M, mostly codemod |
| SegmentedControl | One view switch | 20 hand-built switches in 5 styles | M |
| SaveStatus | "Saving…", "Saved 10:42", "Not saved" in one fixed place | Scattered text | S |
| SplitLayout and Sheet | Main and side pane; the side becomes a drawer under 1024 | Four hand-built two-pane grids | M |
| Tooltip | Icon buttons | None today | S |

**Later:** table controller with sort, paging and stacked rows on phones; compact density; Combobox; date and time pickers in the app language; Skeleton; dark and high-contrast themes.

Every interactive component defines: default, hover (pointer only), focus-visible (2 px ring, 2 px offset, at full colour at once), pressed or selected, disabled (grey fill, not opacity), loading (label kept, `aria-busy`), invalid (danger border and message), read-only.

## 8 · Consultation workspace anatomy

See the concept: [concepts/medvya-workspace-concept.html](concepts/medvya-workspace-concept.html).

```
top bar 48
patient strip 56, pinned: name · age sex · MRN · DOB · [Allergy: …] · consent · token · room
┌ my queue 200 ─┬ note, max 860 ──────────────────────┬ chart panel 340 ────────┐
│ tokens, ages  │ section anchors (pinned)            │ Chart · Past visits ·   │
│ escalations   │ Vitals (from nurse, one row)        │ Checklist               │
│ first         │ History · Examination · Assessment  │ allergies, medicines,   │
│ to finish     │ + diagnoses inline                  │ problems, results       │
│               │ Prescription (dialog to add)        │                         │
│               │ Plan · tests · follow-up            │                         │
└───────────────┴─────────────────────────────────────┴─────────────────────────┘
footer 56, part of the layout: Saved 10:42 · N to finish · Preview prescription · Review and sign
```

- **1280 and wider:** three regions. **1024 to 1279:** the queue becomes a "Next A-07 · 3 waiting" button in the strip; note and chart panel side by side. **768 to 1023:** the chart panel is a drawer opened from the strip. **Under 768:** read-only, with "Open this on a tablet or computer" on write actions.
- First text box at about 160 px (today 440 to 1,138 px).
- The queue rail shows tokens and ages only. Opening another patient keeps the "another consultation is open" challenge.
- Medicines are added through the existing Add medicine dialog. A free-text medicine needs an acknowledgement that names the allergy (F-01).
- Sign, authorise, place orders and end are four separate acts. End asks when a draft exists.
- The checklist counts only what the doctor did.

## 9 · Tables and forms

**Tables.** Always the library Table. A label on every table. Numbers right-aligned with tabular numerals. The identifying cell is bold; nothing else. Sort on the columns people sort by (name, time, wait). Paging over 50 rows. Under 768 px a table becomes stacked rows; it never scrolls sideways with hidden columns that carry dose or identity. Row actions: one visible, the rest in a menu.

**Forms.** One column under 768 px, two at most above. Label above the field. "Required" in words on required fields when most are optional, "optional" in words when most are required: one convention per form. Units inside or beside the field. Errors say what is wrong and what to do, appear beside the field and in a summary that takes focus after a failed submit (WCAG 2.2 SC 3.3.1 Error Identification, 3.3.3 Error Suggestion). Autosave for clinical notes with a visible SaveStatus; explicit Save for settings and registration. Information entered once is not asked again in the same process (SC 3.3.7 Redundant Entry): the nurse's complaint prefills the doctor's note.

## 10 · Pinned controls

| Pinned | Where | Rule |
|---|---|---|
| Top bar | Everywhere | 48 px |
| Patient strip | Consultation, its sub-pages, nurse intake, patient record | 56 px; two rows at most on a tablet |
| Section anchors | Consultation | 44 px |
| Table and column headers | Lists, the board | |
| Action footer | Consultation, long forms | Part of the layout, never an overlay |

Together, pinned chrome takes no more than 30 % of the viewport height, and no focused element is ever under it (WCAG 2.2 SC 2.4.11 Focus Not Obscured (Minimum)). At 400 % zoom the strip and footer un-pin.

## 11 · Responsive behaviour and device strategy

| Task | Phone | Tablet | Desktop |
|---|---|---|---|
| Doctor: my day, queue, read a chart, see an escalation, sign an addendum | Yes | Yes | Yes |
| Doctor: write a note, prescribe, authorise, order, reconcile, sign a new note | No: "Open this on a tablet or computer" | **Designed for** | Yes |
| Nurse: intake and vitals | No, until the strip is pinned and one group shows per screen | **Designed for** | Yes |
| Reception: queue, walk-in, quick registration, check-in | Queue list and quick registration | Yes | **Designed for** |
| Reception: full registration, schedule editing, merge | No | Yes | **Designed for** |
| Owner: clinic today | Yes (read) | Yes | Yes |
| Settings, staff, audit | No | Read | **Designed for** |

Physical devices were not tested in this audit. The tablet claims need a real tablet before the pilot.

## 12 · Motion

**The direct answer: no animation is needed now.** Nothing a pilot clinic will struggle with is caused by missing motion. Reviewer B's full assessment is in its report, section 5.

| When | What | Values | With reduced motion |
|---|---|---|---|
| With the next UI change | Replace `transition-all` on the switch and tabs with named properties; stop the focus ring fading in | | |
| Before the pilot, as words not movement | Truthful save status: "Saving…", then "Saved 10:42" | Opacity swap, 150 ms | Instant swap |
| Before the pilot if two people share a board | A card changed by someone else gets an "Updated" mark | Background from `#E7F6F4` to white over 1,200 ms, ease-out. No movement | A text tag for 5 seconds |
| Later | Skeletons in the final layout | Show after 200 ms; opacity pulse 1 → 0.6 → 1 over 1,200 ms | Static blocks |
| Later | Tablet drawer and confirm dialogs | Drawer: translate 200 ms `cubic-bezier(0.32, 0.72, 0, 1)`, exit 150 ms. Dialog: opacity and scale from 0.97, 150 ms | Opacity only, 100 ms |
| Later | Press feedback on touch | `scale(0.98)`, 100 ms, and a darker fill | Darker fill only |
| Later | Soften the global reduced-motion rule | Allow colour and opacity changes up to 150 ms; no transforms | |
| **Never** | Page transitions; sliding cards between columns; staggered lists; animating the Ctrl+K search; anything that moves while a doctor types; animated counters; toasts over the patient strip | | |

Two tokens first, so nothing invents numbers: an ease-out curve `cubic-bezier(0.23, 1, 0.32, 1)` and a fast duration of 150 ms.

## 13 · Accessibility requirements

Targets, by WCAG 2.2 success criterion. These are design requirements, not a conformance claim. Normative (level A and AA):

| Criterion | Requirement here |
|---|---|
| 1.3.1 Info and Relationships | One `h1` per page (the note print view lacks one); tables labelled |
| 1.4.1 Use of Color | Status in words |
| 1.4.3 Contrast (Minimum), 1.4.11 Non-text Contrast | 4.5:1 text; 3:1 borders and focus |
| 1.4.4 Resize Text, 1.4.10 Reflow | 200 % zoom; no sideways scroll at 320 px wide. Holds on `main` today |
| 2.1.1 Keyboard, 2.4.3 Focus Order, 2.4.7 Focus Visible | Holds on the screens checked |
| 2.4.11 Focus Not Obscured (Minimum) | Nothing under pinned chrome (F-10) |
| 2.5.8 Target Size (Minimum) | 24 px floor; 44 px project rule |
| 3.2.3, 3.2.4 Consistent Navigation and Identification | One name per screen (F-32) |
| 3.3.1, 3.3.3 Error Identification and Suggestion | Error summary (F-23) |
| 3.3.4 Error Prevention (Legal, Financial, Data) | Undo or confirm on visit moves, End, offboarding (F-04, F-05) |
| 3.3.7 Redundant Entry | Nurse's complaint prefilled (F-16) |
| 4.1.3 Status Messages | Save status and live board changes announced |

Advisory, not normative: the NHS design system's error summary and tag patterns; Vercel's Web Interface Guidelines on tabular numerals and focus; NISTIR 7804 and 7804-1 on patient identification staying in view. What each reviewer actually opened is listed in their reports.

## 14 · Guardrails, so it does not drift again

- Lint: no `min-h-11` or `h-11` on library controls in features; no arbitrary font sizes or raw hex colours; no `sm:grid-cols-*`; no raw `h1` in features.
- A count in CI of local `Message`, `Field`, `Card` and `Empty` components (10, 5, 2, 2 today) that may only fall.
- One browser test, mutation-checked: the patient strip is visible after scrolling the consultation.
- The contrast test reads the tone tokens.
- `PRODUCT.md` and `DESIGN.md` written once the direction is agreed.

**Do not change without clinical validation:** allergy presentation and wording; the unknown / none / not recorded distinction; consultation section order and required fields; sign and authorise gating and their separation; prescription line fields, units and safety-check text; vitals ranges and flags; anything that changes what a receptionist can see of clinical data.

**Do not do:** change the palette or typeface; add a second component library; rebuild the shell; add a state library; start dark mode before the tone layer.

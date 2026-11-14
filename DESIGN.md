# Medvya: design rules

The rules for every screen in `web/` and every component in `packages/ui`. Who the product is for, the principles and the device strategy are in [PRODUCT.md](PRODUCT.md). The short enforced form is [AGENTS.md](AGENTS.md) section 4.

Source: the 13 Nov 2026 product UX audit, [blueprint](docs/product-ux-audit/08-design-system-blueprint.md) sections 2 to 10 and 12 to 14, and the [arbitration rulings](docs/product-ux-audit/07-debate-and-arbitration.md). The picture of the target is the [workspace concept](docs/product-ux-audit/concepts/medvya-workspace-concept.html).

How to read it:

- **These are targets.** A rule the build does not meet yet names the ticket that makes it true (epic MDV-P1-E12, [programme](docs/05-delivery/ux-programme.md)). New code follows the rule now; old code is moved by the named ticket.
- **This file describes the build and wins** over [docs/02-design/tokens.md](docs/02-design/tokens.md) (generated from the original design files) where they differ.
- **Open decisions** are marked *awaiting*. Do not treat them as settled.

| Open decision | Written here as | Awaiting |
|---|---|---|
| Body text size | 14/20 at weight 500 (what the build renders, what the blueprint proposes; `tokens.md` says 15/22) | The founder's yes |
| Allergy presentation | Proposal in [Colour and tone](#colour-and-tone) | The pilot doctor, [question 4](docs/product-ux-audit/11-pilot-doctor-validation-plan.md) |
| Patient strip contents | Proposal in [Consultation workspace](#consultation-workspace) | The pilot doctor and nurse, [question 3](docs/product-ux-audit/11-pilot-doctor-validation-plan.md) |

## Layout grid and widths

| Token | Value |
|---|---|
| Top bar | 48 px from 768 px up, 56 px on phones (MDV-P1-093, #1189) |
| Navigation rail, 768 to 1279 | 72 px, icon with an 11 px label |
| Sidebar, 1280 and up | 232 px, collapsible to the rail by the user |
| Page gutter | 16 / 24 / 32 px at phone / tablet / 1600 and up |
| Page header | One 44 px row: title 20/28, secondary text, actions at the end; 12 px gap below |
| Fluid page maximum | 1,600 px |
| Reading maximum | 1,152 px; body text lines about 72 characters |
| Form maximum | 768 px |
| Side panel | 320 px; 360 px for the consultation chart panel |

Breakpoints: 360, 768, **1024**, 1280, 1600. 1024 is the named tier for two-pane layouts. The 360 px `sm:` step is not used for column changes: no `sm:grid-cols-*`. Tokens: MDV-P1-079 (#1175). Applied to screens: MDV-P2-057 (#1216).

## Navigation

| | Phone, under 768 | Tablet, 768 to 1279 | Desktop, 1280 and up |
|---|---|---|---|
| Primary | Bottom tabs: up to 4, then More | Labelled rail | Labelled sidebar |
| Account | In More | Avatar button opens the menu | Same |
| Top bar | Clinic name, search icon, bell | Clinic, search field, bell, avatar | Same |
| Patient search | Full-screen sheet | Ctrl+K dialog, opens instantly | Same |

**The rail is labelled (ruling 8).** Labels sit under the icons in a 72 to 76 px rail. It is never icon-only. The full sidebar collapses to the rail only by the user's own choice.

Account menu: name and role, language, my devices, change facility, sign out (last, separated).

| Role | Items, in order |
|---|---|
| Doctor, owner doctor | My day · Patients · Schedule · Front desk · Settings (owner only) |
| Receptionist | Front desk · Schedule · Patients |
| Nurse | Nurse queue · Patients |
| Practice admin | Front desk · Schedule · Patients · Settings |

- "Today" becomes **My day** for doctors and is never the reception board.
- The desk surface is **Front desk** in the menu and the title. "By status", "By doctor" and later "List" are views inside it. The board stays for the pilot (ruling 4); the list is a third view later (MDV-P2-052, #1211).
- **Patients** is a real page. A row opens one **Patient record** whose tabs depend on capability: Summary (clinicians), Details (desk), Timeline, Documents.
- **Register patient** is a button on Front desk and Patients, and a search action. It leaves the menu.
- A screen a role may not open says so (MDV-P1-096, #1192).
- Settings keeps its five groups and its `?tab=` addresses; at 1024 and wider it uses a left sub-navigation.
- Renames and moved navigation land in one batch, before the first training session or after the first pilot weeks, not in between (ruling 6; MDV-P2-051, #1210). The account menu and rail labels can go earlier (MDV-P1-084, #1180; MDV-P1-093, #1189).

## Density

Two densities (ruling 2). Density is chosen by task. A write is never compact.

| | Comfortable (default) | Compact (explicit, per table) |
|---|---|---|
| Control height | 44 px | 36 px |
| Table row | 48 px | 36 px |
| Body text | 14/20 | 14/20, never smaller |
| Typed clinical text | 16/24 | not allowed |

- **Comfortable is the default and is mandatory** for every control that changes state, every row that picks a patient, medicine lines, sign and authorise, registration, nurse intake, the front-desk queue, and any device that reports a coarse pointer (`any-pointer: coarse`).
- **Compact is an explicit per-table choice, allowed on four tables only:** the audit log, the formulary, order sets and the staff list. Not on the patient directory, not on the front-desk list. `<Table density="compact">`; lint (`medvya-ui/compact-table`) fails it anywhere else. On a coarse pointer a compact table's rows are 48 px. Built by MDV-P2-054 (#1213).
- Tighter page chrome (headers, toolbars, empty cards) is accepted everywhere.
- 44 px is the library default. A feature never patches a control height (defaults: MDV-P1-077, #1173; patches removed: MDV-P1-078, #1174).
- WCAG 2.2 SC 2.5.8 sets a 24 by 24 px floor; 44 px is the project's stricter rule. Any row height under 44 px outside the four tables needs AGENTS.md section 4 amended in the same change.

## Spacing, type, radius, elevation

**Spacing (px):** 2, 4, 6, 8, 12, 16, 24, 32, 48, 64. Inside a control 6 to 8; between related controls 8; between fields 16; card padding 16; between sections 24.

**Type.** Manrope; Noto Sans Arabic; a mono face only for MRN, tokens, codes and times. Nine sizes:

| Token | Size / line | Weight | Use |
|---|---|---|---|
| display | 30/36 | 800 | Queue display, print headers |
| title | 20/28 | 700 | Page title in the 44 px header |
| heading | 18/26 | 700 | Section and card titles; patient name in the strip |
| subheading | 16/24 | 700 | Sub-sections; patient name in rows |
| body | 14/20 | 500 | Default, tables, controls. *Awaiting the founder's yes* |
| body-large | 16/24 | 500 | Typed clinical text, patient-facing pages |
| label | 14/20 | 600 | Field labels, table headers, buttons |
| caption | 12/16 | 600 | Hints, metadata, chips |
| mono | 13/20 | 500 | MRN, token, codes |

Bold marks headings and the one thing that identifies a row. Tabular numerals on every number, vital, time and amount. No other sizes: 11, 12.5, 13.5, 15, 17 and 22 px are removed, and no arbitrary font size is added (the rail label in [Layout](#layout-grid-and-widths) is the one named exception). Tokens: MDV-P1-079 (#1175). Applied: MDV-P2-057 (#1216).

**Radius:** 6 chips, 10 controls, 12 cards and panels, full for pills and avatars.

**Elevation:** border only; one shadow for menus and popovers; one for dialogs, sheets and drawers.

## Colour and tone

The palette does not change. A tone layer gives each meaning one colour pair on web and native, and the contrast test reads from it (MDV-P1-080, #1176).

| Tone | Means | Never used for |
|---|---|---|
| danger | Allergy, critical, blocked, destructive | Emphasis or branding |
| warning | Uncertain, attention, incomplete, not checked | "Error" |
| success | Confirmed, done, signed | Primary buttons |
| info | Booked, in progress, neutral system state | |
| brand (teal) | The one next action; the active navigation item | Status |
| ai (violet, dashed) | Provisional AI output only | Anything accepted by a clinician |
| neutral | Everything else | |

- Status is never colour alone: label always, icon where it helps.
- One status map for every app: `packages/tokens/src/status.ts` gives each shared status its words, tone and glyph (Lucide on web, the same shape in Material on native). Web reads it through `statusIcons` in `@medvya/ui`; the Expo apps import it directly. Allergy has its own shield glyph, never the medicine or critical one (MDV-P5-057).
- **One primary (filled) button per view:** the single next action for the signed-in user.
- No raw hex colours in components.
- Text contrast at least 4.5:1; large text and control borders at least 3:1.
- Dark and high-contrast themes come only after the tone layer exists.

**Allergy presentation. Proposal, awaiting the pilot doctor; today's wording and rules stay until then.** One presentation everywhere: a solid danger block with an icon and the words "Allergy: Penicillin (rash)". "Allergies not recorded" is a warning block with its own words. "No known allergies" is neutral text with a tick. Built by MDV-P1-083 (#1179), which needs clinical sign-off.

## Components

A feature composes library components and does not style its own copy of a pattern. Once a component below exists, features must use it. A new shared component is not done until it is in the `/__ui` gallery in English and Arabic.

| Component | Purpose | Built by |
|---|---|---|
| Input, Select, Textarea | 44 px by default in the library | MDV-P1-077 (#1173) |
| Table | `label` is required; names the scrollable region | MDV-P1-077 (#1173) |
| Table density, stacked rows, TablePagination, `useTableController` | Compact on the four tables; labelled blocks under 768 px; sort announced; paged over 50 rows | MDV-P2-054 (#1213) |
| PatientBanner | One banner: `full`, `strip` (pinned, one row), `desk` (no clinical data). Never covers a focused element | MDV-P1-081 (#1177), clinical sign-off |
| StickyActionBar | Layout footer for the commit action and its status; reserves its own space | MDV-P1-082 (#1178) |
| SaveStatus | "Saving…", "Saved 10:42", "Not saved" in one fixed place | MDV-P1-082 (#1178) |
| Chip, Tag, Banner | Tone, icon, size; one chip vocabulary | MDV-P1-083 (#1179), clinical sign-off |
| AccountMenu, rail item, PageHeader, Tooltip | The shell; PageHeader is the one 44 px header row with back and meta slots, and the only place an `h1` is written | MDV-P1-084 (#1180) |
| Field, ErrorSummary | Label, required mark, hint, error; a summary that takes focus | MDV-P1-085 (#1181) |
| Card, Section, PageMessage, EmptyState | Title, intro, actions; busy, error, empty, denied | MDV-P1-086 (#1182) |
| SegmentedControl | The one view switch | MDV-P1-086 (#1182) |
| SplitLayout, Sheet | Main and side pane; the side becomes a drawer under 1024 | MDV-P1-086 (#1182) |
| ConfirmDialog on End consultation | "Authorise now, discard, or end anyway" | MDV-P1-090 (#1186), clinical sign-off |

Later: Combobox; date and time pickers in the app language; Skeleton; dark and high-contrast themes.

**States.** Every interactive component defines: default, hover (pointer only), focus-visible (2 px ring, 2 px offset, at full colour at once), pressed or selected, disabled (grey fill, not opacity), loading (label kept, `aria-busy`), invalid (danger border and message), read-only.

## Consultation workspace

One workspace (ruling 3), built by MDV-P1-094 (#1190) with clinical sign-off.

```
top bar 48
patient strip 56, pinned
┌ my queue 200 ─┬ note, max 860 ──────────────────────┬ chart panel ────────────┐
│ tokens, ages  │ section anchors (pinned)            │ Chart · Past visits ·   │
│ escalations   │ Vitals (from nurse, one row)        │ Checklist               │
│ first         │ History · Examination · Assessment  │ allergies, medicines,   │
│               │ + diagnoses inline                  │ problems, results       │
│               │ Prescription (dialog to add)        │                         │
│               │ Plan · tests · follow-up            │                         │
└───────────────┴─────────────────────────────────────┴─────────────────────────┘
footer 56, part of the layout: Saved 10:42 · N to finish · Preview prescription · Review and sign
```

- **Patient strip contents. Proposal, awaiting the pilot doctor and nurse:** name · age and sex · MRN · date of birth · allergy in words · consent · token · room. How several allergies show is part of the same question.
- 1280 and wider: three regions. 1024 to 1279: the queue becomes a "Next A-07 · 3 waiting" button in the strip. 768 to 1023: the chart panel is a drawer opened from the strip. Under 768: read-only.
- First text box at about 160 px from the top.
- The queue rail shows tokens and ages only, never names or reasons. Opening another patient keeps the "another consultation is open" challenge (MDV-P1-099, #1195).
- Medicines are added through the existing Add medicine dialog. No inline free-text medicine box. A free-text medicine needs an acknowledgement that names the allergy (MDV-P1-087, #1183).
- Sign, authorise, place orders and end are four separate, recorded acts. End asks when a draft exists.
- The allergy check result shows beside the medicine line. Diagnoses and medicines need no route change.
- The checklist counts only what the doctor did (MDV-P1-091, #1187).

## Tables and forms

**Tables.** Always the library Table, with a label. Numbers right-aligned with tabular numerals. The identifying cell is bold; nothing else. Sort on the columns people sort by (name, time, wait). Paging over 50 rows. Under 768 px a table becomes stacked rows; it never scrolls sideways with hidden columns that carry dose or identity. Row actions: one visible, the rest in a menu. In a compact table the visible one only opens a form and is 32 px with a mouse (44 px on a coarse pointer), so the row stays 36 px; an action that acts at once or ends access (Offboard, Revoke, Remove) is a 44 px menu item and asks to confirm where it cannot be undone.

**Forms.** One column under 768 px, two at most above. Label above the field. One convention per form: "required" in words when most fields are optional, "optional" in words when most are required. Units inside or beside the field. Errors say what is wrong and what to do, beside the field and in a summary that takes focus after a failed submit. Clinical notes autosave with a visible SaveStatus; settings and registration use an explicit Save. Information entered once is not asked again in the same process: the nurse's complaint prefills the doctor's note (MDV-P1-104, #1200).

## Pinned chrome

| Pinned | Where | Rule |
|---|---|---|
| Top bar | Everywhere | 48 px |
| Patient strip | Consultation, its sub-pages, nurse intake, patient record | 56 px; two rows at most on a tablet |
| Section anchors | Consultation | 44 px |
| Table and column headers | Lists, the board | |
| Action footer | Consultation, long forms | Part of the layout, never an overlay |

- Together, pinned chrome takes at most 30 % of the viewport height.
- Nothing focusable ever sits under it.
- At 400 % zoom the strip and footer un-pin.
- No toast over the patient strip.

Built by MDV-P1-082 (#1178) and MDV-P1-092 (#1188).

## Phone and tablet

**Phone rule (ruling 5).** Below 768 px a doctor gets My day, the queue, a read-only chart, search, signing an addendum and seeing an escalation. Composing or authorising a prescription, reconciliation, orders, signing a new note, merge approval and settings show "Open this on a tablet or computer". Reception gets the queue list and quick registration. Tablet (768 and up) is a first-class device for the nurse and the doctor. Built by MDV-P1-098 (#1194) with clinical sign-off. The task-by-device table is in [PRODUCT.md](PRODUCT.md#device-strategy).

## Owner page

**Clinic today (ruling 7): a short actionable page. No number the system did not earn.** It shows only:

1. booked and still to arrive;
2. arrived, split walk-in and booked;
3. waiting now and the longest wait;
4. notes to sign and prescriptions to authorise, per doctor;
5. incomplete registrations;

and a needs-attention list with one action each. Revenue reads "Not tracked yet". Per-doctor consult times wait until the desk can no longer close a visit under the doctor (MDV-P1-100, #1196). No wall of KPI cards; no figure derived from the "Done" state. Built by MDV-P2-053 (#1212).

## Motion

No animation is needed now. Motion is functional, small and rare.

Two tokens, so nothing invents numbers: ease-out `cubic-bezier(0.23, 1, 0.32, 1)` and a fast duration of 150 ms (MDV-P1-079, #1175).

| When | What | Values | With reduced motion |
|---|---|---|---|
| With the next UI change | Named transition properties, never `transition-all`; the focus ring does not fade in | | |
| Before the pilot | Save status: "Saving…", then "Saved 10:42" | Opacity swap, 150 ms | Instant swap |
| Before the pilot if two people share a board | A card changed by someone else gets an "Updated" mark | Background from `#E7F6F4` to white over 1,200 ms, ease-out. No movement | A text tag for 5 seconds |
| Later | Skeletons in the final layout | Show after 200 ms; opacity pulse 1 → 0.6 → 1 over 1,200 ms | Static blocks |
| Later | Tablet drawer and confirm dialogs | Drawer: translate 200 ms `cubic-bezier(0.32, 0.72, 0, 1)`, exit 150 ms. Dialog: opacity and scale from 0.97, 150 ms | Opacity only, 100 ms |
| Later | Press feedback on touch | `scale(0.98)`, 100 ms, and a darker fill | Darker fill only |
| Later | Soften the global reduced-motion rule | Colour and opacity changes up to 150 ms; no transforms | |

Small motion is built by MDV-P2-056 (#1215).

**Never:** page transitions; sliding cards between columns; staggered lists; animating the Ctrl+K search; anything that moves while a doctor types; animated counters; toasts over the patient strip.

## Accessibility

Targets, by WCAG 2.2 success criterion (level A and AA). These are design requirements. This file makes no claim of conformance, and no conformance audit has been done.

| Criterion | Requirement here |
|---|---|
| 1.3.1 Info and Relationships | One `h1` per page; tables labelled |
| 1.4.1 Use of Color | Status in words |
| 1.4.3 Contrast (Minimum), 1.4.11 Non-text Contrast | 4.5:1 text; 3:1 large text, borders and focus |
| 1.4.4 Resize Text, 1.4.10 Reflow | 200 % zoom; no sideways scroll at 320 px wide |
| 2.1.1 Keyboard, 2.4.3 Focus Order, 2.4.7 Focus Visible | Every path by keyboard, in order, with a visible ring |
| 2.4.11 Focus Not Obscured (Minimum) | Nothing focusable under pinned chrome |
| 2.5.8 Target Size (Minimum) | 24 px floor; 44 px project rule (48 px in the patient portal) |
| 3.2.3 Consistent Navigation, 3.2.4 Consistent Identification | One name per screen |
| 3.3.1 Error Identification, 3.3.3 Error Suggestion | Error beside the field and in a summary that takes focus |
| 3.3.4 Error Prevention (Legal, Financial, Data) | Undo or confirm on visit moves, End and offboarding |
| 3.3.7 Redundant Entry | The nurse's complaint is prefilled |
| 4.1.3 Status Messages | Save status and live board changes are announced |

Also required: Arabic right-to-left with logical properties, and reduced motion respected.

## Guardrails

- No `min-h-11` or `h-11` on library controls in features: oxlint `medvya-ui/no-height-patch` fails it (MDV-P1-078, #1174; re-run `node tools/codemods/remove-height-patches.mjs` on a branch). No arbitrary font sizes or raw hex colours; no `sm:grid-cols-*`; no raw `h1` in features (MDV-P1-076, #1172).
- Counts in CI that may only fall: `pnpm check:ui-drift` compares the patterns above, the `border bg-card` string and local `Message`, `Field`, `Card` and `Empty` components with `tools/check-ui-drift/baseline.json`. A rise fails; a fall is committed with `--update` in the same pull request. When a count reaches zero, that ticket turns the matching lint rule to error and removes the count.
- One browser test, mutation-checked: the patient strip is visible after scrolling the consultation (MDV-P1-092, #1188).
- The contrast test reads the tone tokens (MDV-P1-080, #1176).

## Non-negotiables

1. The patient strip is pinned on every clinical write screen, with the allergy in words, never clipped or collapsed to a colour.
2. Sign the note, authorise the prescription, place orders and end the consultation stay four separate, recorded acts. One identity step-up may cover them.
3. The walk-in path must not get longer: at most 6 clicks for a new patient and 3 for a returning one. Re-measure after every desk change.
4. No tick, Saved, or number the system did not earn.
5. Every control that changes state is comfortable, 44 px (48 px rows on a coarse pointer). Compact rows only on the audit log, formulary, order sets and staff tables.
6. No palette or typeface change.
7. No second component library, no new state library.

## Do not change without clinical validation

- Allergy rules, presentation and wording.
- The unknown / none / not recorded distinction.
- The Add medicine dialog and the safety-check wording ("not checked" is honest and stays). The dialog stays until the free-text rule is closed and a clinician has seen a replacement.
- Prescription line fields, units and safety-check text.
- The sign dialog; sign and authorise gating; the separation of sign, authorise and place orders.
- The duplicate block at registration and the identity confirmation at check-in.
- The walk-in path: it must not get longer.
- Consultation section order and required fields.
- Vitals ranges and flags.
- Anything that changes what a receptionist can see of clinical data.
- The palette and the typeface.

## Do not do

Change the palette or typeface; add a second component library; rebuild the shell; add a state library; start dark mode before the tone layer.

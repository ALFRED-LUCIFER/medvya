# Reviewer C — Design system and SaaS experience architecture

Independent review, 13 November 2026. Audit only: no application code, test, infra, brand asset or existing doc was changed. I did not read the Reviewer A or Reviewer B files.

Scope: `web/clinic`, `packages/ui`, `packages/tokens`, `packages/i18n`, `packages/ui-native`, `mobile/*` (code only), `web/platform-admin`, `docs/02-design`.

---

## 1. Execution note

### 1.1 Skill files read

Under `/Users/alfredpaul/.claude/plugins/cache/ui-ux-pro-max-skill/ui-ux-pro-max/2.15.0/.claude/skills/`:

| File | Read |
|---|---|
| `ui-ux-pro-max/SKILL.md` | Whole file (217 lines) |
| `design-system/SKILL.md` | Whole file (248 lines) |
| `ui-styling/SKILL.md` | Whole file (336 lines) |
| `brand/SKILL.md`, `design/SKILL.md`, `banner-design`, `slides` | Not read. Not relevant to an audit, and their scripts write files |
| `ui-ux-pro-max/references/*.md`, `design-system/references/*.md` | Not read. Only the SKILL.md summaries of them |

### 1.2 Commands run and what they returned

All read-only. No `--persist`, no brand or token sync script.

| # | Command (script: `ui-ux-pro-max/scripts/search.py`, run with `python3 -I`) | Result |
|---|---|---|
| 1 | `"healthcare clinic EHR dashboard" --design-system --density 8 --variance 2 --motion 2 -p "Clinic EHR" -f markdown` | Style: "Minimalism & Swiss Style". Palette: cyan `#0891B2` + green `#059669`. Font: Atkinson Hyperlegible. **Pattern returned was a marketing landing page ("Hero + Testimonials + CTA") and a GSAP scroll-reveal**, which is off-topic for an authenticated clinical app. Anti-patterns: neon colours, heavy motion, AI purple/pink gradients |
| 2 | `"healthcare medical clinic" --domain product -n 3` | "Medical Clinic": Accessible & Ethical + Minimalism & Swiss; `rtl_level: partial`. Result 1 recommended Neumorphism, which I reject for a clinical tool (low-contrast boundaries) |
| 3 | `"dense data table sorting" --domain ux -n 3` | Table handling on mobile (horizontal scroll or card layout), bulk actions. Third result (autoplay video) was noise |
| 4 | `"long form validation error" --domain ux -n 3` | Focusable error summary at the top of the form, linked to fields, inline errors kept; errors announced (`role=alert`/`aria-live`); submit feedback |
| 5 | `"enterprise sidebar navigation" --domain ux -n 3` | Sticky nav must not obscure content; keyboard order; breadcrumbs for 3+ levels |
| 6 | `"healthcare medical accessible" --domain color -n 3` | Three cyan/teal palettes |
| 7 | `"data dense dashboard" --domain style -n 3` | "Data-Dense Dashboard": `--table-row-height: 36px`, `--card-padding: 12px`, `--grid-gap: 8px`, `--sidebar-width: 240px`, `--header-height: 56px`, font 12–14px, sticky headers |
| 8 | `"medical clean accessible" --domain typography -n 3` | Figtree + Noto Sans; Lexend + Source Sans 3 |
| 9 | `"data table sorting pagination" --stack shadcn -n 3` | Use the shadcn DataTable pattern (Table + TanStack Table), not custom sort/filter/paging logic |
| 10 | `"form field validation" --stack shadcn -n 3` | React Hook Form `Controller` + shadcn `Field`/`FieldError`; the older `FormField` abstraction is marked retired |
| 11 | `"dashboard" --domain style --rtl=full -n 3` | **0 results.** No style row is rated fully RTL-safe for "dashboard" |
| 12 | `node design-system/scripts/validate-tokens.cjs --dir web/clinic/src` | 11 "hardcoded hex", 3 "hardcoded pixel". At least 4 of the hex hits are false positives (issue numbers such as `#912` in test names) |
| 13 | `node design-system/scripts/validate-tokens.cjs --dir packages/ui/src` | 97 hardcoded hex colours (almost all in `primitives.tsx`) |

How I used the skill output: I took the density variables (7), the error-summary rule (4), the DataTable and Field guidance (9, 10) and the mobile table rule (3). I **did not** adopt its palette or font (1, 6, 8). Medvya's teal/navy/Manrope tokens already exist, are contrast-tested, and a brand change buys a pilot clinic nothing.

### 1.3 Other work

- Own analysis scripts (scratchpad, not committed): Tailwind utility counter over `web/clinic/src` (371 non-test files) and `packages/ui/src`; component and raw-element counter over 168 `.tsx` files; metrics summary of `docs/product-ux-audit/evidence/local-metrics.jsonl` (289 rows at the time).
- Screenshots opened: 14 clinic screenshots (local stack and Azure dev) and 4 platform-console screenshots.
- Platform console capture: done, see section 2.7. Script at `docs/product-ux-audit/tools/platform-admin-capture.mjs`, 30 PNGs under `docs/product-ux-audit/screenshots/platform-admin-mocked/`. Dev server stopped afterwards.

### 1.4 References

| Source | Status |
|---|---|
| WCAG 2.2 (`w3.org/TR/WCAG22/`) | Opened through a fetch agent. Only the first part of the page was read verbatim; criteria from 1.3.1 onward came back as a model summary, so I cite criterion numbers, names, levels and the well-known thresholds, not quoted text |
| NHS design system: components index, error summary, error message, table, tag, warning callout | Opened. `/components/tables` is a 404; the real page is `/components/table` |
| Vercel Web Interface Guidelines | Opened |
| NISTIR 7804 | **Not examined beyond the NIST abstract page.** The PDF downloaded but could not be converted to text here (`pdftoppm` missing; my own extraction attempt hung and was stopped). I do not cite its use-error categories |

### 1.5 Limits

- The local capture was still running when I finished: `local-capture.log` had 513 lines and no `hospital-it|1920|more` line. Metrics below cover **doctor (230 rows) and receptionist (59 rows) only**. Nurse and practice-admin screenshots existed but were not analysed.
- Full-page screenshots draw `sticky` elements at their first-viewport position (the sidebar ends at 900px, the sign bar floats mid-page). I treat those as capture artefacts, not defects.
- I did not run the clinic app, Metro, or any test suite. Nothing here is a compliance claim.
- Row heights quoted from screenshots are measured by eye from pixel positions, good to a few pixels.

---

## 2. Code audit

### 2.1 Tokens

**What exists.** `packages/tokens/src/tokens.json` is the single source: 57 colours, 5 radii, 2 font stacks, 4 breakpoints. `generate.mjs` renders `tokens.css` (`@theme static { … }`, Tailwind 4) and `theme.ts` (plain values for React Native). `packages/ui/src/styles.css:7` imports the CSS; a test fails when outputs are stale. This pipeline is sound and worth keeping.

**What does not exist as a token:** type scale, font weights, spacing, elevation, motion, z-index, density, control heights. `docs/02-design/tokens.md` documents a type scale and an 8px spacing scale, but neither is in `tokens.json`, so nothing enforces them.

**Colour discipline in features is good.** In `web/clinic/src` there are 0 arbitrary colour utilities (`bg-[#…]`, `text-[#…]`) and 22 inline `style=` attributes, 15 of them in the schedule grid where computed positions justify them.

**Colour discipline in the shared library is the weak spot.** `packages/ui/src/primitives.tsx:19-73` holds 103 raw hex values in the status, provenance, alert, recording, schedule and front-desk look tables, applied through inline `style`. The comment at line 15 gives the reason (the contrast test checks exactly what renders), and the test is real (`primitives.test.tsx:40-52`). But the consequence is that the whole clinical status vocabulary bypasses tokens: `#7A4710` (warning text) appears 12 times and is not `warning-700` (`#975a16`); `#1D4ED8` is not `action-600`; `#8A1626` is not `danger-700`. Dark mode and high contrast cannot be delivered while this stands. `button.tsx` adds two more (`#e6ecf1`, `#8a1626`).

**De-facto type scale versus the documented one.**

| Documented (`tokens.md`) | In code (`web/clinic/src`, class uses) | Rendered (text nodes, 289 captured pages) |
|---|---|---|
| Body 15/24 · 500 | `text-[15px]` ×8 | 15px: 18 nodes (0.1%) |
| Dense table 14/20 · 500 | `text-sm` ×299 | 14px: 9,865 (65%) |
| Body large 16/26 · 500 | `text-base` ×24 | 16px: 2,499 (17%) |
| Caption 12/16 · 600 | `text-xs` ×75 | 12px: 1,846 (12%) |
| H2 18/26 · 700 | `text-lg` ×35 | 18px: 248 |
| H1 24/32 · 700 | `text-2xl` ×7 + `PageHeader` | 24px: 213 |
| Display 32/40 · 700 | none | none |
| not documented | `text-[13px]` ×13, `[11px]` ×3, `[17px]`, `[12.5px]`, `[13.5px]`, `[22px]`, `[26px]`, `[28px]` ×9, `[30px]`, `[36px]`, `[40px]`, `[48px]`, `[80px]`, `text-xl` ×8, `text-3xl` | 13px: 295 · 20px: 48 · 17px: 43 · 13.5px: 32 · 30px: 20 · 12.5px: 8 |

The product's real body size is 14px, not the documented 15px. There are 21 distinct font-size utilities where the doc lists 7. Large arbitrary sizes (28–80px) belong to the queue TV and print pages and are legitimate, but should be named.

**Weights.** Documented: 500 body, 600 caption, 700 headings. Actual: `font-bold` ×250, `font-extrabold` ×79, `font-semibold` ×78, `font-medium` ×1. Body renders at 400 and emphasis jumps straight to 700/800. With bold on labels, table headers, chips, buttons and row names, weight no longer ranks anything (see the Today and Staff screenshots).

**Spacing.** Documented 4/8/12/16/24/32/48/64. Actual gaps: `gap-2` (8) ×232, `gap-3` (12) ×178, `gap-1.5` (6) ×132, `gap-4` (16) ×101, `gap-1` (4) ×67, plus 18 other variants including `gap-2.5`, `gap-5`, `gap-[18px]`. Padding has 47 distinct utilities. 6px is the third most used gap and is not in the documented scale. This is tolerable; the fix is to document 6 and stop the long tail, not to reflow the app.

**Radius.** Documented: 6 · 10 · 12 (cards) · pill. Actual: `rounded-xl` (16px) ×106 is the card radius, `rounded-lg` (12) ×73, `rounded-md` (10) ×53. Cards are 16px, not the documented 12.

**Elevation.** Consistent with the doc: 9 shadow uses in the whole app. Good.

**Dark mode and high contrast.** Documented in `tokens.md` and `responsive-and-dark.md` ("Ships in P5-10"). Built: nothing. No dark values in `tokens.json`; the 10 `dark:` variants in `packages/ui` are shadcn leftovers (`tabs.tsx:68`, `switch.tsx`). No `contrast-more` or `forced-colors` use. Documented as future, so not a defect today, but section 2.1's hex tables are the blocker.

**Density.** No compact mode anywhere. Every control is forced to 44px (`min-h-11` ×158, `h-11` ×62). `TableHead` is `h-11` and `TableCell` is `p-3` (`table.tsx:92,113`); with a 44px button in the row, the staff table comes out near 69px per row (7 rows in a 900px viewport, `local-stack/doctor/settings-staff/1440-default.png`). The patient directory, without row buttons, is near 51px.

**Breakpoints.** Documented xs 360 · md 768 · lg 1280 · xl 1600, max content 1440. Used: `md:` ×56, `sm:` ×53, `lg:` ×26, `max-lg:` ×21, `xl:` ×1, plus arbitrary `min-[1024px]` and `min-[1200px]` in `OnboardingWizard.tsx:63,119`.
- `sm` is remapped to **360px** (`tokens.json` breakpoint). So every `sm:grid-cols-2` / `sm:grid-cols-3` (30 uses) is active on essentially every phone. It is not a "small tablet" tier, it is "always on". Visible result: the registration stepper is three columns at 375px with clipped labels (C-11).
- There is no tier between 768 and 1280. A 1024–1279px laptop or landscape tablet gets the icon-only rail and single-column consult layout. Two files already reach for `min-[1024px]`/`min-[1200px]`, which is the code asking for the missing tier.
- "Max content 1440" is not implemented: `pageWidths.fluid` is `max-w-none` (`page.tsx:7`). At 1920 the Today list stretches the full width.

### 2.2 Component inventory

**Shared library (`packages/ui`), uses in `web/clinic/src` (non-test `.tsx`).**

| Component | Uses / files | Note |
|---|---|---|
| Button | 311 / 91 | 188 call sites re-add `min-h-11`, which the default size already sets (`button.tsx:26`). `buttonVariants()` on links ×85 |
| Input | 123 / 47 | Default is `h-9` (36px) (`input.tsx:11`). **109 of 123 call sites override to 44px.** The default is wrong for this product |
| Label | 83 / 30 | |
| Select | 65 / 37 | 55 of 65 triggers add `min-h-11`/`h-11` |
| Textarea | 18 / 16 | |
| Table (+ parts) | 19 / 19 | 0 raw `<table>`/`<tr>`/`<td>`/`<th>` in features. The lint rule works |
| TableState | 11 / 11 | 8 tables do not use it |
| PageContainer / PageHeader | 20 / 17 and 16 / 15 | But 37 raw `<h1>` in 30 files |
| Tabs | 3 / 2 | |
| Dialog | 4 / 4 | |
| Switch | 9 / 7 | 26 native checkbox/radio inputs alongside |
| FrontDeskTag | 11 / 3 | |
| ProvenanceTag | 8 / 5 | |
| IdentityBanner | 7 / 5 | A second, different banner lives in `features/encounters` |
| SystemState | 5 / 4 | |
| ClinicalAlert | 4 / 4 | |
| StatusChip, RecordingPill, FocusScope | 1 each | |

**Patterns re-implemented inside features.**

| Pattern | Implementations | Evidence |
|---|---|---|
| Page-level message / error card | **10** local `Message` functions; 7 are near-identical | `ReconciliationPage.tsx:168`, `DoctorTodayPage.tsx:60`, `IntakePage.tsx:107`, `ProblemsPage.tsx:157`, `Patient360Page.tsx:299`, `ConsultPage.tsx:69`, `DocumentsPage.tsx:182`, `TimelinePage.tsx:154`, `BoardPage.tsx:109`, `ClinicOverviewPage.tsx:53`; plus `ShellMessage`, `PatientsMessage`, `SystemState` |
| Form field wrapper | **5** `Field` (one is a read-only `dt/dd`), 3 `TextField`, 2 `SelectField`, 2 `FieldError` | `settings/components/Field.tsx:6`, `registration/components/FormFields.tsx:25`, `onboarding/components/Fields.tsx:23`, `patient360/components/PregnancyCard.tsx:137`, `patients/components/PatientRecord.tsx:250`, `onboarding/components/SignUpPage.tsx:34` |
| Card surface | No `Card` component. The string `rounded-xl|lg border bg-card` appears **97 times in 57 files** (p-4 ×59, p-6 ×19, p-3 ×7); 2 local `Card` functions and 4 local `Section` functions | `Patient360Page.tsx:156`, `ChronicCareCards.tsx:46`, `settings/components/Section.tsx:4`, `PatientRecord.tsx:241`, `NurseQueuePage.tsx:67` |
| Chips / badges | `Pill` (private, `primitives.tsx:78`), `FrontDeskTag`, `StatusChip`, `ProvenanceTag`, plus local `Chips`, `SafetyChip`, `SourceChip`, `LicenceBadge`, `VerificationTag`, `StateLabel`, `AppointmentChip`, and 15 inline `rounded-full px-*` spans in 10 files | Three chip styles sit side by side on one Today row (grey filled pill, white outlined pill, amber square tag): `local-stack/doctor/today/1440-default.png` |
| Segmented choice / view switch | 20 `aria-pressed` button groups in 15 files; shadcn `Tabs` styled as a pill (registration Quick/Full), as underline (consult Assist/Chart/Checklist, settings groups), as boxed buttons (settings sub-tabs); a bordered container variant on the board (By status/By doctor) | 0 `role="tab"` written by hand, which is right; the visual and semantic model still varies per screen |
| Empty state | Plain `<p>` in many places, 2 local `Empty`, `TableState` | No icon or next action in most |
| Loading | Text only: 93 `t('…loading…')` sites, 0 skeletons | Acceptable; layout shift when data arrives is the cost |
| Patient identity banner | **3**: `packages/ui` `IdentityBanner` (`primitives.tsx:139`), `features/encounters/components/IdentityBanner.tsx:12`, and the `PatientRecord` header card (`PatientRecord.tsx:87-91`) | See C-02 |
| Toast | 0 | Inline `aria-live` regions instead (31). Fine, keep |
| Tooltip, Popover, DropdownMenu, Sheet/Drawer, Combobox, DatePicker, Skeleton, Breadcrumb, Pagination | 0 in the library | The schedule builds its own tablet drawer from `FocusScope` (`SchedulePage.tsx:122-124`); 21 native `type="date"` and 13 `type="time"` inputs |

**Three or more divergent implementations:** message card, form field, card/section surface, chip/badge, segmented switch, patient banner.

**Missing from the library and clearly needed:** `Card`/`Section`, `Field` (label, hint, error, required/optional), `Chip` with tone, `Banner`/`Notice`, `EmptyState`/`PageMessage`, `SegmentedControl`, `Toolbar`/`FilterBar`, `Sheet`, `DropdownMenu` (account menu), `Combobox` (patient and medicine pickers), `Pagination`, density variants on `Table`/`Button`/`Input`. Ironically `packages/ui-native` already has `Card`, `Section`, `Chip` with a `Tone` type, `Banner`, `StateMessage`, `Segmented` and `TextField` with label/hint/error. The native library has the vocabulary the web library lacks.

**House rules (AGENTS.md §4).** Tables: obeyed, lint-enforced. No hard-coded text: obeyed (`react/jsx-no-literals: error`). Logical RTL utilities: obeyed in features (my counter found no physical `ml-/mr-/pl-/pr-` class in feature code); 3 physical utilities remain in shadcn-generated `select.tsx` (`pr-8`, `pl-2`, `right-2`). "Sorting, filtering and paging state lives in a `useXxxTableController`": only 2 of 19 tables have one (staff, queue).

### 2.3 Layout system

**Shell** (`features/shell/components/AppShell.tsx`). Grid at line 41: bottom tabs below 768, 72px icon rail from 768, sidebar `clamp(196px,22vw,232px)` from 1280. Sticky navy sidebar (`:48`), sticky 60px header (`:86`), fixed 72px bottom tab bar (`:103`), `main` padded `p-4 md:p-6` with `pb-28` for the tab bar (`:99`). Skip link present (`:42`). This is a competent shell.

Weaknesses:
- The header holds up to six controls as separate outlined buttons: search, language label + select, Switch facility, My devices, Sign out, bell (`:88-97`). There is no account menu. The sidebar's user block (`:76-82`) is not interactive. Sign out sits one click-slip from Search.
- The icon rail (768–1279) shows icons with `sr-only` labels (`:67`) and no tooltip. Four similar line icons are the only cue (`local-stack/receptionist/front-desk/1024-default.png`).
- On phones the header drops language, facility switch, devices and sign-out into "More", which is reasonable.

**Page container** (`packages/ui/src/components/ui/page.tsx:5-12`): `fluid` (no max), `form` (`max-w-3xl`, 768px), `reading` (`max-w-6xl`, 1152px). Good idea, partly adopted: features still carry `max-w-[720px]` ×6, `max-w-[560px]` ×6, `[880px]`, `[900px]`, `[1280px]`, `[1320px]`, `[440px]`, `[420px]`.

**Grid.** No shared grid. Each two-pane page picks its own side width: consult `20rem` (`ConsultStudio.tsx:67`), patient 360 `18rem` (`Patient360Page.tsx:95`), registration `300px` (`Registration.tsx:123`), schedule `340px` (`SchedulePage.tsx:119`). Four values for the same idea.

**Consultation** (`ConsultStudio.tsx`). Banner, then an action row, then a sticky section stepper (`:130`), then note sections with a sticky right panel from 1280 (`:98`, `lg:sticky lg:top-16`), and a sticky bottom sign bar (`:148`). Prescription, orders and referrals follow below the note as full-width blocks. The one-page consult is 3,611px tall at 1440 and 4,941px at 375.
- The identity banner is **not sticky** in the consult, although `responsive-and-dark.md` specifies a sticky identity banner on mobile and `tokens.md` says "fixed on every clinical screen". After one scroll the patient's name and allergies are off screen while the doctor writes the plan and prescription.
- At 375px the banner, three action buttons, the view switch, the heading and two help lines take the whole first viewport; the first note field starts below 700px, and the sticky sign bar covers the bottom of the screen above the tab bar (`local-stack/doctor/consult/375-default.png`).
- The prescription table has `min-w-[1000px]` (`PrescriptionPanel.tsx`), so it scrolls sideways inside the page at every width below about 1300px.

**Patient 360.** Sticky section filter (`Patient360Page.tsx:78`), vitals tiles, two-column cards with an 18rem side column. Sound. The banner here is the library one, with the red allergy pill.

**Reception board.** Six status columns in a horizontal scroller; at 1024 only four and a half columns fit and "Done" is clipped (`receptionist/front-desk/1024-default.png`). The design doc accepts horizontal scroll on tablet. At 375 the board starts about 600px down, after title, view switch, live chip, privacy chip, three actions and the workload card (`receptionist/front-desk/375-default.png`).

**Settings.** The brief said "17 tabs". That is out of date: both current main and the Azure dev build already group the 17 panels into five groups with a second row of sub-tabs (`settings/domain/settings.ts:173-186`, `SettingsPage.tsx:18-33`; `azure-dev/owner-doctor/settings/1440-default.png`). The tab id stays in the URL (`?tab=hours`), which is good. Remaining problems:
- Two stacked tab rows in two different visual styles (underline, then boxed) above every panel.
- Grouping oddities: "Pilot metrics", "Queue displays" and "Terms" sit under Practice; "Doctors" and "Staff" are separate tabs with separate add flows; "Data" (export, retention) sits under Security; "Messages" holds only Notifications.
- `SettingsRoute.tsx` wires 17 panels and about 20 controllers in one file. Fine for now.
- On a phone the two rows wrap to several lines before any content.

Is a 17-panel settings area sound? As grouped, yes for a pilot. A left sub-navigation at desktop width would scale better than two tab rows (blueprint 3.3).

### 2.4 Navigation and information architecture

Model: `features/shell/domain/navigation.ts:20-37`. 14 items in 4 groups; 8 are behind tenant flags, so a pilot owner-doctor sees 6: Today, Patients, Front desk, Schedule, Register patient, Practice settings. Capability-filtered, with server enforcement noted (`:51`). Role landing: first visible item, nurses to the nurse queue (`:54-58`). Mobile tabs: up to four by priority, then More (`:66-70`).

Problems:
1. **"Patients" is three things.** The nav item `/patients` redirects to the directory `/patients/directory` (`App.tsx:161-162`), so on the directory page the sidebar item is not highlighted (NavLink `end`; `doctor/patients-directory/1440-default.png`). The header "Search patients" opens the same directory or a ⌘K search. A row then opens either the desk "patient record" (`/patients/directory/:id`, registration data only) or "Patient 360" (`/patients/:id`, the chart), depending on role. Two different patient pages with two different headers and no cross-link wording that explains the difference.
2. **"Today" is two screens.** For a doctor it is the doctor's day; for other roles the same path shows the reception board (`App.tsx:151-158`). An owner-doctor has both "Today" and "Front desk" in the sidebar. The page titles are "Today · date" and "Reception board · date" under an eyebrow "Front desk": three names for the reception surface (Front desk, Reception board, Clinic overview at `/front-desk/overview`).
3. **"Register patient" is an action in the navigation**, and takes one of four mobile tab slots for a receptionist. It is also a button on the board.
4. **URL-only routes.** `/admin/staff` (`App.tsx:169`, `InviteColleagueRoute`) has no link anywhere in `web/clinic/src`. `/front-desk/overview` is reachable only through a board action. `/organisation` is reachable only from a notice or onboarding. `/schedule/leave`, `/walk-in`, `/waitlist` are reached by buttons, which is fine.
5. **Group labels do not match what is inside.** "Practice" contains Front desk, Schedule, Register, Nurse intake; "Admin" contains one item called "Practice settings".
6. **No breadcrumb or consistent back.** Sub-pages carry their own "Back to the board", "Back to patients", "Back to chart" buttons in different positions and styles (button row in consult, text link with arrow on patient record).
7. **Rail colour by group** (`rail-clinical`, `rail-practice`, …) is a 3px tint on the active item only. It is harmless but carries no information a user will learn.

### 2.5 Forms and tables

**Forms.**
- Validation is consistent underneath (React Hook Form + Zod schemas in `domain/`), which is the hard part.
- Error presentation is not consistent. Registration and onboarding: icon + semibold red text, `aria-describedby` wired through a render-prop (`FormFields.tsx:13-31`). Settings: `role="alert"` paragraph via a shared `fieldError` class string, no icon (`settings/components/Field.tsx:12`). Pregnancy card: bold black text, generic "invalid" message, not red, not linked (`PregnancyCard.tsx:143`). Platform console: plain red text, no icon. 205 `role="alert"` and 111 `text-danger-700` sites show how much of this is hand-assembled.
- No error summary anywhere. On a six-step registration or a long settings panel, a failed submit relies on the user finding the red line. NHS guidance (opened) is to always show a summary at the top and link each error to its field; the Vercel guideline (opened) is to focus the first error on submit.
- Required/optional marking uses three conventions: red asterisk (registration, `Full name *`), the word "(required)" (consult note, `Chief complaint (required)`), and "(optional)" in the label text (ABHA fields, onboarding). 
- Units: vitals use a dedicated `VitalField`; prescription dose/unit are separate columns. Acceptable.
- Save model is mixed but each case is defensible: consult note autosaves and says so ("Changes save automatically"), settings panels use an explicit "Save changes", registration is a stepper with a final submit. What is missing is one consistent place and wording for save status, and an unsaved-changes guard on explicit-save panels (not verified in code; marked inferred).
- Controls: native date and time inputs (34 uses) render `dd/mm/yyyy` in the browser locale, not the app locale. No combobox, so patient and medicine pickers are custom lists.
- Mixed control heights inside one form where a call site forgot the override: platform console invite form has 36px inputs beside a 44px select (`platform-admin-mocked/team/1440-invite-error.png`).

**Tables** (19).
- Sorting: 2 (staff, queue). Paging or "load more": 4 (directory, audit log, staff, timeline). Sticky header: 1. Filtering lives in ad-hoc controls above each table.
- 10 tables set a `min-w-[…]` between 480 and 1000px and rely on horizontal scroll at phone width; none switches to a card/stacked layout. The NHS table page (opened) says not to use a basic table where it becomes squashed on small screens.
- Four tables have no `label`, so their scroll container is not a focusable region (`DaySheetView`, `MergeReview`, `GrowthCard`, `TimelinePage`). axe reported `scrollable-region-focusable` on the platform console audit table at 375px for the same reason.
- Row actions are full 44px outline buttons in the last column, which sets the row height (staff: about 69px).
- Audit log: filter form of five fields above the table takes the first 700px; the table starts below the fold at 1440×900 (`doctor/settings-audit/1440-default.png`). Action values are raw codes (`patient.document.list`).
- Staff table: "Offboard" is offered on every active row including the signed-in owner (`staff.ts:77` `canOffboard` checks status only). Whether the server refuses is not something I checked.

### 2.6 Cross-platform coherence

**Tokens:** shared. `packages/ui-native` reads `theme` from `@medvya/tokens` (35 references, 0 raw hex). `brand.tsx:7-9` sets clinic (teal, 44px targets, 15px body) and patient (indigo, 48px, 17px) brands. This is the documented 15px body, so **native body text is 15px while web body text is 14px**.

**Type:** native has named variants (`display`, `title`, `heading`, `body`, `label`, `caption`, `overline`, `text.tsx:5`). Web has none. Icons: native uses Material Icons, web uses Lucide; the design doc says Material Symbols. Three icon vocabularies for the same states.

**Components:** native has `Chip` with `Tone` (`danger|warning|success|info|ai|neutral|brand`), `Banner`, `StateMessage`, `Card`, `Section`, `Segmented`. Web expresses the same ideas with look tables and ad-hoc markup. The semantic tone model in native is the better design and should be the shared vocabulary.

**Expo apps:** code only. Verified developer-experience defect: `@babel/helper-define-polyfill-provider` is absent from both `node_modules/@babel/` and `mobile/clinician/node_modules/@babel/` on this machine, matching the Metro error in `docs/product-ux-audit/screenshots/mobile-expo/clinician/375-bundle-error.png`. I did not run Metro myself and did not try to fix it.

**Platform console (`web/platform-admin`):** shares `@medvya/ui`, `@medvya/i18n`, the same token CSS, the same shell shape (navy sidebar, white header), the same feature-folder layering. It looks like the same product. It also inherits every library gap: its own 36px inputs, its own filter tabs at 30px, its own red-text errors.

### 2.7 Platform console screenshots (mocked API, not a deployed environment)

Captured with `docs/product-ux-audit/tools/platform-admin-capture.mjs` against the Vite dev server on port 4175, with `page.route` mocks and synthetic fixtures copied from `tests/e2e/platform-console.spec.ts` and `tests/e2e/platform-approvals.spec.ts`. No WebAuthn step was needed because `/v1/me` was mocked with `keyVerified: true`; the key-registration screen was captured in its "before" state only.

Files: `docs/product-ux-audit/screenshots/platform-admin-mocked/<screen>/<width>-<state>.png` for `sign-in`, `key-step`, `overview` (default, reviewer), `approvals`, `approval-detail` (clinic, hospital-no-dpa, approved, viewer-open, approve-reason, arabic), `team` (default, invite-error), `audit` (default, arabic), at 1440×900 and 375×812. 30 files.

Measured by the script:

| Observation | Where |
|---|---|
| Horizontal overflow at 375px | `sign-in`, `key-step` (card is wider than the viewport, right edge clipped) |
| Filter tabs 30px tall (Waiting, Needs changes, Decided, All) | every `approvals*` screen, both widths |
| Text inputs 36px tall beside a 44px select | `team/…-invite-error` |
| axe `scrollable-region-focusable` | `audit` at 375 (en and ar), `approval-detail/375-approved` |
| axe clean | all other captures |
| Mobile navigation is a horizontally scrolling strip under the logo with the third item cut off ("Team acces") | `audit/375-default.png` |
| "The quarterly access review is due." is red text with no icon or container | `team/1440-*.png` |

---

## 3. Design-system blueprint (proposal)

Grounded in the existing tokens, fonts and shell. No new brand, no new dependency beyond shadcn components the house rule already allows.

### 3.1 Principles

1. **Identity before content.** On any screen that can change a patient's record, the patient's name, age/sex, MRN and allergy state are visible without scrolling, in one form everywhere.
2. **Words, icon, then colour.** Every status has a text label and an icon; colour only reinforces (WCAG 1.4.1; NHS tag guidance, opened, says the same).
3. **One meaning, one look.** A tone (danger, warning, success, info, AI, neutral) maps to one token pair everywhere, web and native.
4. **Unknown is not none.** "Not recorded" and "None known" never share a look. The code already holds this line (`primitives.tsx:131`); the system must make it impossible to break.
5. **AI is provisional until accepted.** Violet, dashed border, sparkle icon, and the word "draft". Never the primary colour.
6. **Density is chosen by task, not by taste.** Touch-first surfaces stay at 44px; reference tables used with a pointer may go compact.
7. **The library is the only place a pattern is styled.** A feature composes; it does not restyle.
8. **Smallest change that removes a class of defect.** Fix defaults in `packages/ui` before touching 100 call sites.

### 3.2 Layout grid and widths

| Token | Value | Rationale |
|---|---|---|
| `--shell-rail` | 72px | exists |
| `--shell-sidebar` | 232px fixed (drop the `clamp`) | the sidebar only shows from 1280px, where 22vw is already 281px, so the clamp always resolves to 232; say so directly |
| `--shell-header` | 56px (from 60) | 4px back for content; matches the dense-dashboard guidance |
| `--page-gutter` | 16 / 24 / 32px at xs / md / xl | exists as `p-4 md:p-6`; add the xl step |
| `--page-max-fluid` | 1600px | implements the documented "max content" idea so lists stop stretching at 1920 |
| `--page-max-reading` | 1152px (`max-w-6xl`) | exists |
| `--page-max-form` | 768px (`max-w-3xl`) | exists |
| `--pane-side` | 320px | one value replacing 288, 300, 320, 340 |
| `--pane-side-wide` | 400px | consult evidence/assist panel at xl |

Two-pane pages use one `SplitLayout` (`main` + `aside`, side pane sticky below the header, collapses to a `Sheet` below `lg`).

### 3.3 Navigation model

**Breakpoints.** Keep xs 360, md 768, lg 1280, xl 1600. Add **`wide` at 1024** as a named token and use it for two-pane layouts and for showing the labelled sidebar. Stop using `sm:` for column changes (it is 360px); reserve `sm:` for "not the very smallest phones" tweaks only.

| | Mobile < 768 | Tablet 768–1023 | Laptop/desktop ≥ 1024 |
|---|---|---|---|
| Primary nav | Bottom tabs, max 4 + More | Icon rail with tooltips and visible labels under icons (72px allows an 11px label) | Labelled sidebar 232px, collapsible to rail by the user |
| Account | In More | Avatar button in header opens menu | Sidebar user block becomes the account menu: language, switch facility, my devices, sign out |
| Header | Facility name, search icon, bell | Search field, bell, avatar | Search field, bell |
| Patient search | Full-screen sheet | ⌘K dialog | ⌘K dialog |

**Role-based items** (pilot scope; flagged items slot into the same groups later).

| Role | Sidebar (in order) | Mobile tabs |
|---|---|---|
| Doctor / owner-doctor | **My day** (today), **Patients**, **Schedule**, *(owner only)* **Front desk**, **Settings** | My day · Patients · Schedule · More |
| Receptionist | **Front desk**, **Schedule**, **Patients** | Front desk · Schedule · Patients · More |
| Nurse | **Nurse queue**, **Patients** | Nurse queue · Patients · More |
| Practice admin | **Front desk**, **Schedule**, **Patients**, **Settings** | Front desk · Schedule · Patients · More |

Decisions inside that table:
- Rename nav "Today" to **My day** for doctors, and never serve the board at `/today`. The reception surface is **Front desk** in nav, eyebrow and title ("Front desk · date"). "Reception board" and "Clinic overview" become view names inside it (By status, By doctor, Overview).
- **Patients** is a real page at `/patients` (the directory) for every role that may look patients up, so the nav item highlights. A row opens one patient page whose tabs depend on capability: **Summary** (chart, for clinicians), **Details** (registration, for desk), **Timeline**, **Documents**. Call it "Patient record" everywhere; retire "Patient 360" from user-facing copy.
- **Register patient** leaves the navigation. It is the primary button on Front desk and on Patients, and a ⌘K action.
- Flat list, no group headings while there are six or fewer items. Bring headings back when flags turn on more than about eight.
- One `PageHeader` with an optional `back` slot (label + target) replaces the per-page back buttons.
- `/admin/staff`: link it from Settings › People, or delete the route. Product decision.

**Settings.** Keep the URL contract (`?tab=`). At ≥ 1024 replace the two tab rows with a left sub-navigation (grouped list) inside the settings page; below 1024 a single select or an index page that drills in. Proposed groups:

| Group | Panels |
|---|---|
| Clinic | Profile, Locations, Hours, Holidays, Queue displays |
| People | Staff, Doctors, Roles (custom roles, today hidden below Staff) |
| Clinical | Nurse intake, Medicines and order sets |
| Messages | Notifications |
| Access and audit | Who opens records, Access requests, Access review, Audit log |
| Data and legal | Data (export, retention), Terms |
| Pilot | Pilot metrics (remove after pilot) |

### 3.4 Density strategy

Two densities, set by a `data-density` attribute on a container and read by `Table`, `Button`, `Input`, `Select`, `Chip`.

| | Comfortable (default) | Compact |
|---|---|---|
| Control height | 44px | 32px |
| Table row min height | 48px | 36px |
| Cell padding | 12px | 6px 12px |
| Body text | 14/20 | 14/20 (never smaller) |
| Row action | Button `sm` 36px visual, 44px hit area | Icon button 28px visual, 32px hit area, spaced ≥ 8px |

Where each applies:
- **Comfortable everywhere by default**, and always on: anything below 1024px, `pointer: coarse`, the front-desk board, nurse intake and vitals, the consult note, prescription entry, sign and authorise actions, registration.
- **Compact, opt-in, pointer-only (≥ 1024 and `pointer: fine`)**: audit log, staff list, patient directory, formulary, order sets, timeline, day sheet.

The trade-off, stated plainly. The house rule is 44px (WCAG 2.5.5 is AAA at 44px; the AA criterion 2.5.8 is 24px). Holding 44px for every control on every screen is why a staff list shows 7 rows and an audit log shows 2 before scrolling. For read-mostly reference tables on a desktop, more rows in view reduces scrolling and comparison errors. For anything that writes clinical data, or is used on a tablet at the bedside or a busy desk, a mis-tap is the bigger risk and 44px stays. Compact therefore never applies to a control that commits a clinical or irreversible action. Compact mode needs a short clinician/receptionist check before it ships on any clinical list.

### 3.5 Spacing and type scales

**Spacing** (px): 2, 4, 6, 8, 12, 16, 24, 32, 48, 64. This is the documented scale plus 2 and 6, which the code already uses heavily. Rules: inside a control 6–8; between related controls 8; between fields 16; card padding 16 (compact 12); between cards and sections 24; page gutter per 3.2. Lint out `gap-2.5`, `gap-5`, `gap-[18px]`, `p-2.5`, `p-5`, `p-10`.

**Type** (Manrope; Noto Sans Arabic; JetBrains Mono for IDs). Name the scale the app already renders rather than the documented one, so nothing reflows.

| Token | Size/line | Weight | Use |
|---|---|---|---|
| `text-display` | 30/36 | 800 | Queue TV, print headers only |
| `text-title` | 24/32 | 800 | Page h1 (as `PageHeader` today) |
| `text-heading` | 18/26 | 700 | Card and section h2 |
| `text-subheading` | 16/24 | 700 | h3, patient name in rows |
| `text-body` | 14/20 | 500 | Default body, tables, controls |
| `text-body-lg` | 16/24 | 500 | Long reading text, patient-facing pages |
| `text-label` | 14/20 | 600 | Field labels, table headers, buttons |
| `text-caption` | 12/16 | 600 | Hints, metadata, chip text |
| `text-mono` | 13/20 | 500 | MRN, tokens, codes, times in grids |

Rules: body weight moves from 400 to **500** (as documented; Manrope at 400 and 14px is thin on low-end clinic monitors). Bold (700) is for headings and the one thing per row that identifies it. Extrabold (800) only for page titles. Remove 11, 12.5, 13, 13.5, 15, 17, 20, 22px. `tabular-nums` on every numeric cell, vitals value, time and money (the Vercel guideline, opened, says the same). Arabic: line-height +2px on body and labels; verify with a native reader. Document that native body stays 15 and patient 17, or align web to 15; pick one (C-05).

**Radius:** 6 (chips, tags), 10 (controls), 12 (cards, panels, tables), full (pills, avatars). Move cards from 16 back to the documented 12: one find-and-replace through the new `Card`.

**Elevation:** e0 border, e1 `shadow-md` popovers/menus, e2 `shadow-lg` dialogs/sheets/sticky action bar. Nothing else.

**Z-index tokens:** 10 sticky, 20 floating action, 30 drawer/sheet, 40 dialog, 50 skip link/toast.

### 3.6 Colour and semantic rules

Add a **tone layer** to `tokens.json` and make every chip, banner, alert and state read from it. Each tone is four values: `fg`, `bg` (tint), `border`, `solid`. Values below are the ones already rendering today (from `primitives.tsx`), so the migration is visual no-op.

| Tone | fg | bg | border | Icon family | Use |
|---|---|---|---|---|---|
| `danger` | `#8A1626` | `#FDECEE` | `#F3B9C0` | circle-alert | Critical, allergy present, failed, destructive |
| `warning` | `#7A4710` | `#FDF5E7` | `#EFD5A6` | triangle-alert / help-circle | Attention, incomplete, **not recorded / unknown** |
| `success` | `#07774F` | `#E6F4EF` | — | circle-check / badge-check | Confirmed, verified, done. Never inferred |
| `info` | `#1D4ED8` | `#EFF4FF` | `#93B4F5` | info | In progress, booked, synced |
| `brand` | `#0F766E` | `#E7F6F4` | `#0E9384` | — | In room, reviewed, selected |
| `ai` | `#5B3FC4` | `#F2EFFC` | dashed `#A897E8` | sparkles | AI-provisional only |
| `neutral` | `#334E68` | `#F1F5F8` | `#DFE7ED` | — | Categories, counts, patient-reported, past |
| `signed` | `#FFFFFF` on `#102A43` | — | — | lock | Signed, read-only |

Rules:
- **Never colour alone.** `Chip` and `Banner` require a label; an icon is required for `danger`, `warning`, `ai`, `signed`. Type-level enforcement: no `tone` prop without `children`.
- **Status** (workflow position) uses `Chip` pill shape. **Provenance** (where a fact came from) uses square `Tag` shape. **Severity** (alerts) uses `Banner`. Three shapes, three meanings; never mix on one fact.
- **Allergy** states, fixed wording and look in one component: present → `danger` + pill icon + "Allergy: name · status"; none known → `neutral` + check + "No known allergies"; not recorded → `warning` + help icon + "Allergies not recorded". Present allergies never truncate or collapse.
- **AI-provisional**: `ai` tone, dashed border, "AI draft · not reviewed". An accepted field loses the AI look and gains a `success` "Clinician verified" tag. No AI violet on buttons that commit data.
- **Destructive** buttons use `danger.solid`; a page has at most one primary (teal) action per region.
- Keep the contrast test, pointed at the tone tokens instead of look tables: fg on bg ≥ 4.5:1, border on surface ≥ 3:1 where the border is the only boundary (WCAG 1.4.3, 1.4.11).
- Dark and high-contrast themes become a second and third value per tone token. Do not start them before the tone layer exists.

### 3.7 Component inventory and priority

Effort is relative: S (hours), M (a day or two), L (several days).

**Must-have for pilot**

| Component | Purpose, variants, states | Accessibility | Replaces | Effort |
|---|---|---|---|---|
| **Input / Select / Textarea defaults** | Default height 44px in the library; `size="compact"` 32px later | Global focus ring already present; `aria-invalid` border already present | 109 `min-h-11` overrides on Input, 55 on Select, 188 redundant on Button (codemod to delete) | S |
| **Field** | Label, required/optional marker, hint, error; wraps any control; `wide` | `htmlFor`, `aria-describedby` to hint and error, `aria-invalid`, `aria-required`; error has icon + text | 5 `Field`, 3 `TextField`, 2 `SelectField`, 2 `FieldError`, the `fieldError` class string (130 uses) | M |
| **ErrorSummary** | Box at the top of a form after failed submit: heading + list of links to fields | `role="alert"`, receives focus, each item focuses its field (WCAG 3.3.1, 3.3.3; NHS error summary) | nothing today | S |
| **PatientBanner** | One banner. Variants: `full` (chart, consult), `compact` (sticky strip: name, age/sex, MRN, allergy chips), `desk` (no clinical data). Slots: consent, encounter, actions | `section` with accessible name; allergy chips are text; sticky strip must not cover focused elements (WCAG 2.4.11) | `packages/ui` `IdentityBanner`, `encounters/IdentityBanner.tsx`, `PatientRecord` header | M. **Needs clinical validation** |
| **Chip / Tag** | `tone`, `shape` (pill/tag), `icon`, `size` | Text always; decorative icon `aria-hidden` | `Pill`, `FrontDeskTag`, local `Chips`, `SafetyChip`, `SourceChip`, `LicenceBadge`, `VerificationTag`, 15 inline spans | M |
| **PageMessage / EmptyState** | Title, body, one action, optional back link; `busy`, `error`, `empty`, `denied` | `role="alert"` for errors, `status` for busy/empty; heading level prop | 10 `Message`, `ShellMessage`, `PatientsMessage`, 2 `Empty`; `SystemState` becomes a preset | S |
| **Card / Section** | Title, intro, actions slot, padding by density | `section` + `aria-labelledby` | 97 inline card strings, 2 `Card`, 4 `Section` | M (mostly codemod) |
| **AccountMenu (DropdownMenu)** | Avatar trigger; language, switch facility, my devices, sign out | Radix menu keyboard model | 4 header buttons; sidebar user block | S (add shadcn `dropdown-menu`) |

**Next**

| Component | Purpose | Replaces | Effort |
|---|---|---|---|
| **SegmentedControl** | Single-choice view switch; `aria-pressed` group or radio group; one look | 20 `aria-pressed` groups, pill and boxed Tabs restyles | M |
| **PageHeader `back` + `meta` slots** | One back affordance, one place for live/status chips | per-page back buttons, 37 raw `h1` | S |
| **SplitLayout + Sheet** | Main + side pane; side becomes a sheet below 1024 | 4 hand-written two-pane grids, the schedule's custom drawer | M |
| **StickyActionBar** | Bottom bar for the primary commit action with status text; reserves space so it never covers content | consult sign bar, mobile "Book" floating button | S |
| **Table density + DataTable helpers** | `density` prop; shared `useTableController` (sort, filter, page) over the existing `tableSort` domain; `Pagination`; stacked-row variant below 768 | per-table ad-hoc logic; 10 `min-w` scrollers | L |
| **Banner** | Tone-based inline notice with optional action | `ClinicalAlert`, `OfflineBanner`, `OrganisationNotice`, `TermsNotice`, red-text notices | S |
| **Tooltip** | Rail icon labels, icon buttons | none today | S |
| **SaveStatus** | "Saved 10:42" / "Saving…" / "Not saved" in a fixed slot | scattered autosave text | S |

**Later**

Combobox (patient, medicine), DatePicker/TimePicker in app locale, Skeleton, Breadcrumb, Stepper (registration, onboarding, consult steps share one), dark and high-contrast themes, command palette actions beyond patient search.

**States every interactive component must define:** default, hover (pointer only), focus-visible (2px `ring` + 2px offset, exists), pressed/selected (thick border + tint, exists as `selected`), disabled (grey fill, not opacity, exists), loading (`aria-busy`, spinner, label kept), invalid (danger border + message), read-only (no border, same text colour).

### 3.8 Consultation workspace anatomy

```
┌ header 56 ───────────────────────────────────────────────────────────────┐
├ PatientBanner (compact, sticky under header): name · age/sex · MRN · allergy chips · consent │
├ consult toolbar: status ("Draft · saved 10:42") · view switch · back ───────────────────────┤
│ section rail (sticky)  │  note sections (one column, max 768)         │ side pane 320–400   │
│ 1 Vitals      ✓        │  1 Vitals                                    │ tabs: Assist · Chart│
│ 2 History     •        │  2 History                                   │ · Checklist         │
│ 3 Exam                 │  …                                           │ (sticky, own scroll)│
│ 4 Assessment           │  6 Plan → Medicines, Tests, Advice, Follow-up│                     │
│ 5 Plan                 │  Prescription (full main width)              │                     │
│ 6 Review & sign        │  Orders · Referrals                          │                     │
├ StickyActionBar: "3 items need review" · [Sign note] ──────────────────────────────────────┤
```

- ≥ 1600: three regions as drawn. 1024–1599: section rail becomes the existing horizontal chip stepper; side pane stays. 768–1023: side pane becomes a sheet opened from the toolbar. < 768: banner compact strip sticky; stepper is a horizontal scroller; side pane is a sheet; action bar sits above the tab bar and the page reserves its height.
- The banner strip is the only sticky element at the top besides the header. Section stepper sticks under it only at ≥ 1024.
- Prescription lines below 1024: stacked line cards (medicine, then dose/unit/route/frequency as a 2×2 grid), not a 1000px table.
- Keep: autosave with visible status, the review checklist count on the sign button, the separation of signing the note from authorising the prescription.
- Do not change without clinical validation: section order, required-field set, sign gating, safety-check wording and placement, allergy display.

### 3.9 Table and form patterns

**Tables.**
- Anatomy: `Toolbar` (search, filters as chips, result count, primary action) → `Table` → `Pagination` or "Load more". Filters and sort live in the URL (as settings tabs already do).
- First column identifies the row and is the row header; numeric columns right-aligned with `tabular-nums`; IDs in mono.
- Row actions: one visible action at most; others in a row menu. Destructive row actions never sit alone next to a non-destructive one, and are hidden for the signed-in user's own row.
- Header sticky when the table is taller than the viewport. Every scrollable table has a `label` (make it a required prop).
- Below 768: tables with more than four columns switch to stacked rows (label: value), keeping the row header as the card title.
- Empty, loading, error through `TableState` in all 19 tables.
- Human-readable values: audit "action" shows words, with the code in a tooltip or secondary line (the platform console already does this in Arabic and English).

**Forms.**
- One column by default; two columns only for short, related pairs (date + time, dose + unit) at ≥ 768.
- Label above control, hint below label, error below control with icon, replacing the hint position visually but keeping both in `aria-describedby`.
- Marking rule: mark the minority. In a form where most fields are required, mark optional ones "(optional)"; state "All fields are required unless marked optional" once. Drop the asterisk and "(required)".
- Validate on submit and on blur after first submit; never on each keystroke. On failed submit show `ErrorSummary`, focus it, keep values (NHS error message guidance: do not clear fields).
- Error text says how to fix ("Enter a 10-digit mobile number"), not what went wrong.
- Units inside the control's trailing slot (kg, mmHg, °C), read by assistive tech through `aria-describedby`.
- Long forms: stepper with a review step (registration) or sectioned page with sticky section nav (settings, consult). Steps show completion with icon + text.
- Save model: autosave only where a draft exists server-side (note, prescription draft) and always with `SaveStatus`. Everything else is explicit save with a dirty-state guard on navigation. Never mix both in one panel.
- Destructive or irreversible actions (offboard, cancel prescription, reject organisation) use a `Dialog` that names the object and requires the reason where the domain needs one.

### 3.10 Sticky controls

| Region | Sticky at | Notes |
|---|---|---|
| Header | all widths | 56px |
| Sidebar / rail | ≥ 768 | own scroll |
| Bottom tabs | < 768 | 64px + safe-area inset |
| Patient banner (compact) | clinical pages, all widths | under header |
| Section nav / filters | ≥ 1024 | under banner; one row only |
| Side pane | ≥ 1024 | own scroll, max height viewport minus header and banner |
| Action bar | pages with a commit action | above bottom tabs on mobile; content gets matching bottom padding |
| Table header | tables taller than viewport | |

Budget: sticky chrome may not exceed 30% of viewport height at 375×667. With header 56 + banner strip 48 + tabs 64 + action bar 64 = 232px of 667 (35%), so on phones the section nav does not stick and the action bar collapses to a single 56px row. Focused elements must not be hidden under any of these (WCAG 2.4.11): add `scroll-margin` equal to the top sticky stack and bottom padding equal to the bottom stack.

### 3.11 Responsive behaviour per breakpoint

| Width | Shell | Pages |
|---|---|---|
| 360–767 | Bottom tabs, header with search icon | One column. Tables stack. Side panes are sheets. Board: one status at a time with a status switcher, or the "By doctor" list. Page header actions collapse to one primary + overflow menu |
| 768–1023 | Labelled icon rail | Two-column cards. Board scrolls horizontally with snap. Side panes are sheets |
| 1024–1279 | Sidebar 232 (collapsible) | Two-pane layouts on. Compact density allowed |
| 1280–1599 | Sidebar | As above; consult shows side pane |
| ≥ 1600 | Sidebar | Fluid pages capped at 1600; consult adds the section rail; reading pages stay 1152 |

Reflow at 320 CSS px and 200% zoom must work without two-dimensional scrolling except for the schedule grid and data tables (WCAG 1.4.10, 1.4.4).

### 3.12 Accessibility requirements (WCAG 2.2 level A and AA targets, by criterion)

These are targets for the design system, not a statement that the product meets them.

| Criterion | Requirement in this system |
|---|---|
| 1.3.1 Info and Relationships | Tables use the `Table` parts with row and column headers; fields use `Field`; groups use `fieldset`/`legend` |
| 1.3.5 Identify Input Purpose | `autocomplete` tokens on name, phone, email, address in registration and sign-up |
| 1.4.1 Use of Color | Tone components require text; selection shown by border + tint; schedule states keep icon + label |
| 1.4.3 Contrast (Minimum) | Tone fg/bg ≥ 4.5:1, tested from tokens |
| 1.4.4 Resize Text, 1.4.10 Reflow | 200% zoom and 320px width checked per page template |
| 1.4.11 Non-text Contrast | Control borders (`--color-input`) ≥ 3:1, already tested; chip borders where they are the only boundary |
| 1.4.12 Text Spacing | No fixed heights on text containers; chips and buttons use min-height |
| 1.4.13 Content on Hover or Focus | Tooltip is dismissible, hoverable, persistent (Radix) |
| 2.1.1 Keyboard | Every scrollable region focusable (`label` required on `Table`); drag on the schedule has a keyboard path |
| 2.4.3 Focus Order, 2.4.7 Focus Visible | Global 2px ring kept; sheets and dialogs trap and restore focus |
| 2.4.11 Focus Not Obscured (Minimum) | Sticky budget and scroll margins in 3.10 |
| 2.5.7 Dragging Movements | Schedule reschedule has a non-drag alternative |
| 2.5.8 Target Size (Minimum) | 24×24 CSS px floor everywhere, including compact density and in-row links. House rule stays 44px (48px patient portal) for comfortable density. 2.5.5 (44px) is AAA |
| 3.2.6 Consistent Help | Help and language controls in the same place on every page (account menu) |
| 3.3.1, 3.3.3 Error Identification / Suggestion | `Field` error text + `ErrorSummary` |
| 3.3.2 Labels or Instructions | Visible labels; units and formats in hints; one required/optional convention |
| 3.3.7 Redundant Entry | Quick registration data pre-fills full registration; booking pre-fills from the chosen patient |
| 3.3.8 Accessible Authentication (Minimum) | Entra and passkey flows, no puzzles; paste never blocked |
| 4.1.3 Status Messages | `SaveStatus`, live board updates, table states use `role="status"`/`aria-live` without stealing focus |

Language and direction: `lang` and `dir` per locale (exists); IDs, MRNs, doses and dates isolated LTR inside Arabic (the `iso()`/`ltr()` helpers exist, keep them in the components so features cannot forget).

### 3.13 Migration path for one developer

Order matters: each step removes a class of defect and makes the next step smaller. None requires a redesign freeze.

| Step | What | How | Effort | Clinical validation? |
|---|---|---|---|---|
| 1 | Fix control defaults in `packages/ui`: Input/Select 44px, card radius token | Edit 3 files; **codemod**: delete `min-h-11`/`h-11` from `Input`, `SelectTrigger`, `Button` call sites (about 350) | S | No |
| 2 | `PageMessage`, `Card`/`Section` | Add to library; **codemod** the 7 identical `Message` functions and the `rounded-xl border bg-card p-*` strings | M | No |
| 3 | `Field` + `ErrorSummary` | Add to library from the registration implementation (the best of the five); migrate settings, onboarding, pregnancy, sign-up, platform console by hand, one feature per PR | M | No |
| 4 | Tone tokens + `Chip`/`Tag`/`Banner` | Add tones to `tokens.json`; repoint look tables to tokens (no visual change); repoint the contrast test; replace local chips | M | Review only (looks unchanged) |
| 5 | One `PatientBanner`, sticky compact strip in consult | Merge the three; sticky under header | M | **Yes**: allergy wording and look, what the strip shows |
| 6 | Account menu, nav renames, Patients as a real page, Register out of nav | `navigation.ts`, `AppShell`, i18n keys in three languages | M | Product decision on names; quick check with a receptionist and a doctor |
| 7 | Type and spacing tokens; body weight 500 | Add `@theme` text tokens; **codemod** arbitrary sizes to the nearest token; visual check in en and ar | M | No |
| 8 | `SegmentedControl`, `PageHeader` back/meta, `StickyActionBar`, `SaveStatus` | Library + hand migration | M | Sign bar behaviour: yes |
| 9 | `wide` breakpoint, `SplitLayout`, `Sheet`; stop `sm:grid-cols-*` | Hand migration of 4 two-pane pages; **codemod** `sm:grid-cols` → `md:` then review at 375 | M | No |
| 10 | Table controller, pagination, stacked mobile rows, compact density | One table at a time, audit log and staff first | L | Compact on clinical lists: yes |
| 11 | Settings left sub-nav and regrouping | `SettingsPage`, `settings.ts` | S | No |
| 12 | Dark and high-contrast themes | Only after step 4 | L | Yes |

**Lint rules to add** (oxlint, same style as the existing table rule):
- Forbid `min-h-11`/`h-11` on `Input`, `SelectTrigger`, `Button` in features (after step 1).
- Forbid arbitrary font sizes `text-[…]` in `src/features/**` except an allow-list (queue TV, print).
- Forbid `bg-[#…]`, `text-[#…]`, `border-[#…]` everywhere, including `packages/ui`.
- Forbid the string `border bg-card` in features (use `Card`).
- Forbid local component names `Message`, `Field`, `Card`, `Empty` in `features/*/components` via a small custom check, or simpler: forbid raw `<h1>` in features (use `PageHeader`).
- Forbid `sm:grid-cols-*`.
- Require `label` on `Table` (make the prop non-optional in TypeScript; no lint needed).

**Do not touch without clinical validation:** allergy presentation and wording; the unknown/none/not-recorded distinction; consult section order and required fields; sign and authorise gating and their separation; prescription line fields, units and safety-check text; AI-provisional marking; vitals ranges and flags; anything that changes what a receptionist can see of clinical data.

**Do not do:** swap the palette or font; adopt a second component library; rebuild the shell; introduce a state library; start dark mode before the tone layer.

---

## 4. Findings

Severity: P0 safety or blocker, P1 critical workflow, P2 major usability, P3 polish. "Observed" means seen in a screenshot, measured, or read in code. "Inferred" means a consequence I reason to but did not see happen.

### C-01 · Input and Select defaults are 36px; 164 call sites patch them to 44px
- **Where:** system-wide (clinic and platform console), all roles and viewports.
- **Severity:** P2. **Observed.**
- **Evidence:** `packages/ui/src/components/ui/input.tsx:11` (`h-9`); 109 of 123 `<Input>` and 55 of 65 `<SelectTrigger>` call sites add `min-h-11`/`h-11`; 188 `<Button>` call sites re-add `min-h-11` that `button.tsx:26` already sets. Missed override: `platform-admin-mocked/team/1440-invite-error.png` (36px inputs beside a 44px select; script measured `INPUT 557x36`).
- **Problem:** the library default contradicts the house rule, so every feature must remember to fix it.
- **Consequence:** inconsistent control heights wherever someone forgets; 350 lines of noise; the 44px rule is one omission from being broken on any new form.
- **Reproduction:** open the platform console team page, click Invite.
- **Remediation:** blueprint step 1.
- **Dependency:** frontend. **Confidence:** high. **Validation owner:** developer.

### C-02 · Three patient identity headers; allergy shown differently in consult and chart; banner not sticky in the consult
- **Where:** `/consult/:id`, `/patients/:id`, `/patients/directory/:id`; doctor; all widths.
- **Severity:** P1. **Observed** (the divergence and non-sticky banner); **inferred** (wrong-patient or missed-allergy risk).
- **Evidence:** `features/encounters/components/IdentityBanner.tsx:17-28` (allergy as bold text with a generic alert icon, 2px teal border, no avatar); `packages/ui/src/primitives.tsx:139-164` (red pill with pill icon, avatar, mono MRN); `features/patients/components/PatientRecord.tsx:87-91`. Screenshots `local-stack/doctor/consult/1440-default.png` vs `local-stack/doctor/patient-360/1440-default.png`. No `sticky` on the consult banner (`ConsultStudio.tsx`); `docs/02-design/responsive-and-dark.md` and `tokens.md` call for a fixed/sticky identity banner.
- **Problem:** the most safety-relevant element has no single form, and on the screen where prescribing happens it scrolls away. The consult is 3,611px tall at 1440 and 4,941px at 375.
- **Consequence:** while writing the plan or prescription, the patient's name and allergies are not on screen; the allergy cue the doctor learned on the chart (red pill) is not the cue in the consult (plain text).
- **Reproduction:** open a consult, scroll to Prescription.
- **Remediation:** one `PatientBanner` with a sticky compact strip (blueprint 3.7, 3.8, step 5).
- **Dependency:** frontend + product decision. **Confidence:** high on the observation, medium on severity. **Validation owner:** clinical lead (a practising doctor), then developer.

### C-03 · Clinical status colours bypass the token system (103 raw hex in the shared library)
- **Where:** system-wide.
- **Severity:** P2. **Observed.**
- **Evidence:** `packages/ui/src/primitives.tsx:19-73`, `:135-137`, `:176-180`; skill validator reported 97 hardcoded hex in `packages/ui/src`. `#7A4710` used 12 times is not `--color-warning-700`.
- **Problem:** two colour systems: tokens for chrome, literals for meaning. Native (`ui-native`) uses tokens and a `Tone` type, so web and mobile status colours can drift.
- **Consequence:** dark mode, high contrast and any palette fix require editing look tables by hand; cross-platform drift.
- **Reproduction:** read the file.
- **Remediation:** tone tokens (blueprint 3.6, step 4). No visual change intended.
- **Dependency:** frontend. **Confidence:** high. **Validation owner:** developer; clinical lead confirms looks unchanged.

### C-04 · Core patterns have 3 to 10 divergent implementations inside features
- **Where:** system-wide.
- **Severity:** P2. **Observed.**
- **Evidence:** section 2.2 table: 10 `Message`, 5 `Field`, 97 inline card strings, about 12 chip variants, 20 `aria-pressed` groups, 3 patient headers.
- **Problem:** the library stops at primitives; every feature rebuilds the layer above.
- **Consequence:** visible inconsistency (three chip styles on one Today row), slower changes, each a11y fix made N times.
- **Reproduction:** `grep -rn "function Message" web/clinic/src/features`.
- **Remediation:** blueprint 3.7 must-haves, steps 2–4.
- **Dependency:** frontend. **Confidence:** high. **Validation owner:** developer.

### C-05 · Documented type scale and the product disagree; weight carries no hierarchy
- **Where:** system-wide; native vs web.
- **Severity:** P2. **Observed.**
- **Evidence:** `docs/02-design/tokens.md` (Body 15/24 · 500) vs 65% of rendered text nodes at 14px and 0.1% at 15px (`local-metrics.jsonl`); 21 font-size utilities; `font-bold` ×250, `font-medium` ×1; `packages/ui-native/src/brand.tsx:7` sets body 15.
- **Problem:** no type tokens; documentation describes a different product; bold is everywhere.
- **Consequence:** flat hierarchy on dense screens (Today list, staff table); web and native differ by a pixel in body size for no stated reason.
- **Reproduction:** compare `today/1440-default.png` with the doc.
- **Remediation:** blueprint 3.5, step 7.
- **Dependency:** frontend + design-only decision (14 vs 15). **Confidence:** high. **Validation owner:** founder.

### C-06 · No density option: reference tables show few rows
- **Where:** `/settings?tab=staff`, `?tab=audit`, `/patients/directory`, formulary; desktop.
- **Severity:** P2. **Observed.**
- **Evidence:** `table.tsx:92,113`; `settings-staff/1440-default.png` (7 rows, about 69px each); `settings-audit/1440-default.png` (table header at about 800px of 900; page 4,192px tall).
- **Problem:** one touch-first density for every surface.
- **Consequence:** heavy scrolling on admin and audit tasks at a desk.
- **Reproduction:** open the audit log at 1440×900.
- **Remediation:** blueprint 3.4, step 10.
- **Dependency:** frontend + product decision. **Confidence:** high. **Validation owner:** practice admin user; clinical lead for clinical lists.

### C-07 · "Patients" and "Today" each name several different screens
- **Where:** navigation, all roles.
- **Severity:** P2. **Observed.**
- **Evidence:** `App.tsx:151-162`; `navigation.ts:22-23,60-64`; `patients-directory/1440-default.png` (no nav item highlighted); en.json `nav.frontDesk` "Front desk", `board.title` "Reception board · {day}", `overview.title` "Clinic overview", `patient360.title` "Patient 360".
- **Problem:** one label, several destinations; one surface, three names; two patient pages.
- **Consequence:** users cannot build a stable map; support and training cost; the nav loses its "you are here".
- **Reproduction:** as owner-doctor click Patients, then Today, then Front desk.
- **Remediation:** blueprint 3.3.
- **Dependency:** frontend + product decision + translations. **Confidence:** high. **Validation owner:** founder with one receptionist and one doctor.

### C-08 · Header is a row of buttons; no account menu; Sign out beside Search
- **Where:** shell, ≥ 768.
- **Severity:** P3. **Observed.**
- **Evidence:** `AppShell.tsx:86-98`; any 1440 screenshot.
- **Problem:** account and session controls occupy the primary toolbar permanently.
- **Consequence:** clutter; accidental sign-out mid-task is one mis-click away (inferred).
- **Reproduction:** look at the header.
- **Remediation:** `AccountMenu` (blueprint 3.3, 3.7).
- **Dependency:** frontend (adds shadcn `dropdown-menu`). **Confidence:** high. **Validation owner:** developer.

### C-09 · Icon rail between 768 and 1279 has no visible labels or tooltips, and that range includes common laptops and landscape tablets
- **Where:** shell, 768–1279.
- **Severity:** P2. **Observed.**
- **Evidence:** `AppShell.tsx:41,62-67` (`max-lg:sr-only`); `receptionist/front-desk/1024-default.png`; `OnboardingWizard.tsx:63,119` uses `min-[1024px]`/`min-[1200px]`.
- **Problem:** no layout tier at 1024; navigation is unlabeled icons for sighted users across 512px of width range.
- **Consequence:** recognition-by-icon only on reception tablets and small laptops.
- **Reproduction:** load any page at 1024 wide.
- **Remediation:** `wide` breakpoint at 1024, labelled rail (blueprint 3.3, 3.11).
- **Dependency:** frontend. **Confidence:** high. **Validation owner:** developer.

### C-10 · Consult on a phone: first viewport is chrome; note table for prescriptions needs 1000px
- **Where:** `/consult/:id`, doctor, 375 and 768.
- **Severity:** P2. **Observed.**
- **Evidence:** `local-stack/doctor/consult/375-default.png`; `ConsultStudio.tsx:130,148`; `PrescriptionPanel.tsx` (`min-w-[1000px]`); page height 4,941px at 375.
- **Problem:** banner + actions + switch + help text precede any field; sticky sign bar and tab bar take the bottom; prescription lines scroll sideways.
- **Consequence:** the mobile consult is technically usable and practically slow. If the pilot is desktop-first this is tolerable.
- **Reproduction:** open a consult at 375.
- **Remediation:** blueprint 3.8, 3.10.
- **Dependency:** frontend + product decision (is phone consult in pilot scope?). **Confidence:** medium (I saw the first viewport only, plus code). **Validation owner:** founder, doctor.

### C-11 · `sm:` is 360px, so "small-screen" column rules apply on phones
- **Where:** 30 `sm:grid-cols-*` uses; registration stepper at 375.
- **Severity:** P3 (P2 for the stepper). **Observed.**
- **Evidence:** `tokens.json` breakpoint `sm: 360px`; `Registration.tsx:126` (`grid-cols-2 sm:grid-cols-3`); `receptionist/register-patient/375-default.png` (three step columns, "Family & emergency" clipped at the right edge).
- **Problem:** the remapped `sm` does not mean what a Tailwind reader expects.
- **Consequence:** cramped multi-column layouts on phones; clipped text.
- **Reproduction:** open Register patient at 375.
- **Remediation:** step 9; rename the token to `xs` or forbid `sm:grid-cols-*`.
- **Dependency:** frontend. **Confidence:** high. **Validation owner:** developer.

### C-12 · No error summary; three error styles; three required/optional conventions
- **Where:** registration, settings, onboarding, pregnancy card, consult note, platform console.
- **Severity:** P2. **Observed.**
- **Evidence:** `FormFields.tsx:13-31`, `settings/components/Field.tsx:12`, `PregnancyCard.tsx:143`; screenshots `register-patient/1440-default.png` (asterisk; "(optional)"), `consult/1440-default.png` ("(required)"), `platform-admin-mocked/team/1440-invite-error.png`.
- **Problem:** inconsistent field feedback; nothing gathers errors on long or multi-step forms.
- **Consequence:** missed errors below the fold; screen-reader users get per-field alerts with no overview (WCAG 3.3.1 is met per field where `role="alert"` exists; the pregnancy card message is generic and not linked).
- **Reproduction:** submit registration step 1 empty; compare with settings profile.
- **Remediation:** `Field` + `ErrorSummary` (step 3).
- **Dependency:** frontend. **Confidence:** high. **Validation owner:** developer; accessibility tester.

### C-13 · Tables: 2 of 19 sortable, 10 rely on horizontal scroll on phones, 4 unlabeled scroll regions
- **Where:** system-wide.
- **Severity:** P2. **Observed.**
- **Evidence:** section 2.5 survey; `DaySheetView.tsx`, `MergeReview.tsx`, `GrowthCard.tsx`, `TimelinePage.tsx` (no `label`); axe `scrollable-region-focusable` on the console audit table at 375.
- **Problem:** the `Table` primitive is good; the data-table layer (controller, paging, mobile form) is per-feature.
- **Consequence:** inconsistent capability from list to list; keyboard users cannot scroll some tables (WCAG 2.1.1).
- **Reproduction:** tab through the timeline table at 375.
- **Remediation:** make `label` required now (S); rest in step 10.
- **Dependency:** frontend. **Confidence:** high. **Validation owner:** developer.

### C-14 · Staff table offers "Offboard" on the signed-in owner's own row
- **Where:** `/settings?tab=staff`, owner-doctor.
- **Severity:** P2. **Observed** (button present); **inferred** (outcome).
- **Evidence:** `staff/domain/staff.ts:77`; `settings-staff/1440-default.png` (last visible row "Dr. Arjun Rao (synthetic) · Owner doctor · Offboard").
- **Problem:** a self-lockout action is presented as a routine row button.
- **Consequence:** if the server allows it, the only owner can remove themselves; if it refuses, the user gets an error for an action the UI offered. I did not test either.
- **Reproduction:** open Staff as the owner.
- **Remediation:** hide or disable with an explanation for the current user and for the last owner; confirm the server rule.
- **Dependency:** frontend + backend check + product decision. **Confidence:** medium. **Validation owner:** developer.

### C-15 · `/admin/staff` is reachable only by typing the URL
- **Where:** routing.
- **Severity:** P3. **Observed.**
- **Evidence:** `App.tsx:169`; no other reference in `web/clinic/src`.
- **Problem:** orphan screen duplicating Settings › Staff.
- **Consequence:** dead weight; a second invite flow to maintain.
- **Reproduction:** grep.
- **Remediation:** link it or remove it.
- **Dependency:** product decision. **Confidence:** high (within `web/clinic/src`; emails or docs may link it). **Validation owner:** founder.

### C-16 · Fluid pages have no maximum width
- **Where:** Today, board, schedule, directory at ≥ 1600.
- **Severity:** P3. **Observed** in code; 1920 screenshots not opened.
- **Evidence:** `page.tsx:7` (`max-w-none`); `tokens.md` "Max content width 1440 at widescreen".
- **Problem:** documented cap not implemented.
- **Consequence:** very long rows and far-apart actions on wide monitors (the Today row puts the name at the left edge and "Start consultation" at the right edge).
- **Reproduction:** open Today at 1920.
- **Remediation:** blueprint 3.2.
- **Dependency:** frontend. **Confidence:** medium. **Validation owner:** developer.

### C-17 · Settings: two tab rows in two styles; some panels grouped oddly
- **Where:** `/settings`, admin roles.
- **Severity:** P3. **Observed.**
- **Evidence:** `SettingsPage.tsx:20-29`; `settings.ts:183-186`; `settings-staff/1440-default.png`, `settings-audit/1440-default.png`.
- **Problem:** already grouped (the "17 tabs" problem is solved), but the two-row pattern is heavy and Pilot metrics/Terms/Data sit in unexpected groups.
- **Consequence:** minor findability cost.
- **Reproduction:** look for "Data export".
- **Remediation:** blueprint 3.3 settings table, step 11.
- **Dependency:** frontend. **Confidence:** medium. **Validation owner:** founder.

### C-18 · Dark mode and high contrast are documented and not built
- **Where:** system-wide.
- **Severity:** P3 (documented as later). **Observed.**
- **Evidence:** `tokens.md`, `responsive-and-dark.md` ("Ships in P5-10"); no dark values in `tokens.json`; dead `dark:` variants in `tabs.tsx:68`, `switch.tsx`.
- **Problem:** not a pilot blocker; the risk is starting it before C-03 is fixed.
- **Consequence:** none today.
- **Remediation:** step 12 after step 4; delete dead `dark:` classes.
- **Dependency:** frontend. **Confidence:** high. **Validation owner:** developer.

### C-19 · Native date and time inputs ignore the app locale; no combobox
- **Where:** 34 date/time inputs; booking patient picker, add-medicine.
- **Severity:** P3. **Observed** (code and `dd/mm/yyyy` placeholders in screenshots); **inferred** (mismatch when browser locale differs from the chosen app language).
- **Evidence:** `register-patient/1440-default.png`, `settings-audit/1440-default.png`.
- **Problem:** browser-locale widgets inside an app with its own language switch.
- **Consequence:** an Arabic UI with an English-format date widget, or the reverse.
- **Remediation:** later-tier DatePicker and Combobox; not for pilot.
- **Dependency:** frontend, possible dependency change. **Confidence:** medium. **Validation owner:** developer.

### C-20 · Expo apps cannot be bundled on this machine
- **Where:** `mobile/clinician`, `mobile/patient`.
- **Severity:** P2 (developer experience; blocks any mobile UX review). **Observed** (module absent, pre-captured error screenshot); I did not run Metro.
- **Evidence:** `ls node_modules/@babel/helper-define-polyfill-provider` and `mobile/clinician/node_modules/@babel/…`: not found; `screenshots/mobile-expo/clinician/375-bundle-error.png`.
- **Problem:** a Babel helper is not resolvable in the pnpm workspace.
- **Consequence:** no one can run or review the mobile apps locally.
- **Reproduction:** start Metro for `mobile/clinician`.
- **Remediation:** not attempted, per instructions.
- **Dependency:** dependency change. **Confidence:** medium. **Validation owner:** developer.

### C-21 · Web and native use different component vocabularies and three icon sets
- **Where:** `packages/ui` vs `packages/ui-native`.
- **Severity:** P3. **Observed.**
- **Evidence:** `ui-native/src/display.tsx:8,24,63,89` (`Tone`, `Chip`, `Banner`, `StateMessage`), `controls.tsx:8` (Material Icons); web uses Lucide; `tokens.md` names Material Symbols.
- **Problem:** same states, different names and glyphs per platform.
- **Consequence:** a doctor moving between phone and desktop sees different icons for the same status.
- **Remediation:** adopt the native `Tone` vocabulary on web (step 4); keep one icon mapping table per status in `packages/tokens` or i18n-adjacent data.
- **Dependency:** frontend. **Confidence:** medium (native not seen running). **Validation owner:** developer.

### C-22 · Platform console: sign-in overflows at 375px; filter tabs 30px; mobile nav strip clips
- **Where:** `web/platform-admin`, mocked API, 375 and 1440.
- **Severity:** P3 (internal staff tool). **Observed.**
- **Evidence:** capture log in 2.7; `platform-admin-mocked/sign-in/375-signed-out.png`, `audit/375-default.png`, `approval-detail/1440-clinic.png`.
- **Problem:** small-screen and target-size slips in a tool that otherwise matches the product.
- **Consequence:** low; staff use it on desktops. The 30px tabs still pass WCAG 2.5.8 (24px) and miss the house 44px rule.
- **Remediation:** falls out of steps 1 and 8 (library defaults, SegmentedControl).
- **Dependency:** frontend. **Confidence:** high. **Validation owner:** developer.

### C-23 · Reception board on a phone starts about 600px down
- **Where:** `/front-desk`, receptionist, 375.
- **Severity:** P3. **Observed.**
- **Evidence:** `receptionist/front-desk/375-default.png`; `firstContentTop` average 379px at 375 across captured pages.
- **Problem:** title, two chips, three buttons and the workload card stack before the columns.
- **Consequence:** the working surface is below the fold; reception is unlikely to run on a phone, so impact is limited.
- **Remediation:** collapse header actions on mobile (blueprint 3.11).
- **Dependency:** frontend + product decision. **Confidence:** medium. **Validation owner:** receptionist user.

### C-24 · Audit log shows raw action codes
- **Where:** `/settings?tab=audit`, admin.
- **Severity:** P3. **Observed.**
- **Evidence:** `settings-audit/1440-default.png` ("patient.document.list"); the platform console audit shows words ("Team member invited").
- **Problem:** inconsistent between the two audit views; codes are developer language.
- **Remediation:** reuse the console's wording approach.
- **Dependency:** frontend + translations. **Confidence:** high. **Validation owner:** developer.

No P0 findings from this lens. C-02 is the one I would not ship a pilot without deciding.

---

## 5. What is good and should be kept (verified)

| Asset | Verification |
|---|---|
| Token pipeline: one JSON → web CSS and native theme, staleness test | `packages/tokens/generate.mjs`, `tokens.test.ts` |
| Layered front-end architecture enforced by lint | `web/clinic/.oxlintrc.json` (components cannot import services, hooks or TanStack; hooks cannot import UI); same layering in `web/platform-admin` |
| Table rule works | 0 raw table elements in features; `forbid-elements` rule |
| No hard-coded UI text; three languages near parity | `react/jsx-no-literals: error`; en 4,805 keys, ar 4,802 (4 missing), hi 4,779 (27 missing) |
| RTL groundwork | logical utilities throughout features; `rtl:` variants where shadows are directional; LTR isolation helper for IDs (`primitives.tsx:134`); console Arabic captures axe-clean at 1440 |
| Never-colour-alone is designed in | every look has icon + label (`primitives.tsx:17`); selected state uses border + tint (`button.tsx:19`); unknown vs none allergy kept distinct (`primitives.tsx:131`) |
| Contrast is tested, with documented deviations | `primitives.test.tsx:40-52`; `tokens.json` notes (`brand-700` and `input` lifted for AA) |
| Automated accessibility checks | 60 of 66 e2e specs reference axe; local capture found 1 axe issue in 289 page loads (`page-has-heading-one`) and 0 horizontal overflows |
| Shell fundamentals | skip link, sticky regions, role-filtered nav with server enforcement, URL-addressable settings tabs |
| Restraint | 9 shadows in the app, one primary colour, almost no motion, reduced-motion honoured globally (`styles.css:14`) |
| Target sizes | average 2.7–2.8 of about 25 targets per page under 44px (mostly in-row name links at 19px tall, which are inline text links) |
| A component gallery exists | `web/clinic/src/dev/UiGallery.tsx` at `/__ui` |
| Honest page messages and states | `TableState`, `SystemState`, offline handling present |

---

## 6. Direction vote

**B — cohesive modernisation, delivered as a sequence of surgical PRs.**

Why not C (reset): the foundations are better than most early products (tokens, layering, i18n, RTL, a11y tests, lint). A reset would discard tested behaviour in a regulated domain and stall the pilot for a visual gain users did not ask for. Risk high, benefit low.

Why not A alone (surgical fixes): the defects are systemic, not local. Fixing chips or errors screen by screen means doing each fix 5 to 10 times and leaving the cause (a library that ends at primitives) in place. One person cannot keep 10 copies of a pattern consistent.

Why B is affordable for one person: steps 1–4 are mostly library additions plus codemods, each a small PR with no behaviour change; they shrink the code. Steps 5–6 are the only ones that change what users see in a meaningful way, and they are the ones with the highest value (patient identity, navigation naming). Steps 7–12 can follow the pilot. Total relative effort: 2 S + 7 M before pilot (steps 1–6 and the `Table` label fix), 1 S + 3 M + 2 L after. Main risk: scope creep into restyling. Guard: no palette, font or shell change; every step must delete more feature code than it adds.

---

## 7. Subscores

| Area | Score | Basis |
|---|---|---|
| Visual and design-system quality | **8 / 15** | Strong: token pipeline, colour discipline in features, contrast tests, restraint, consistent shell. Weak: wrong control defaults patched 350 times, no type/spacing/density tokens, status colours outside tokens, 3–10 copies of core patterns, flat typographic hierarchy, docs and product disagree |
| Information architecture | **8 / 15, provisional** | Strong: capability-filtered nav, sensible role landing, grouped settings with URL state, global patient search. Weak: overloaded "Patients" and "Today", three names for the reception surface, two patient pages, action in nav, orphan route, no account menu, unlabeled rail. Provisional because nurse, practice-admin and hospital-IT captures were not analysed and no user was observed |

---

## 8. Could not do

- NISTIR 7804 body text: not readable here; not cited.
- WCAG 2.2: criteria cited by number and name from a partly summarised fetch; no verbatim quotes.
- Nurse, practice-admin and hospital-IT evidence: capture unfinished; not analysed.
- Mobile apps: code only; Metro not run.
- No clinic-app interaction states (errors, dialogs, drawers, Arabic) were seen for the clinic app; only default states were captured. Arabic was seen only on the platform console.
- 1920px clinic screenshots were not opened; C-16 rests on code.

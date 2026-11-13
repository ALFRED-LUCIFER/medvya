⚠️ DEGRADED: single-context (the brief allowed no reviewer sub-agents, so the Impeccable critique's two isolated assessments ran in one context; see the execution note)

# Reviewer B · Visual and interaction design review

Independent review, audit only. Nothing in the application, tests, infra, brand assets or existing docs was changed. Reviewer A and Reviewer C files were not read.

Date: 13 November 2026. Build reviewed: current `main` on the throwaway local stack (primary evidence) and the older Azure India dev deploy (screenshots only, never contacted by me).

## 1 · Execution note

### Skill files read

| File | Read |
|---|---|
| `~/.claude/plugins/cache/impeccable/impeccable/4.9.0/skills/impeccable/SKILL.md` | whole |
| `…/impeccable/reference/critique.md` | whole |
| `…/impeccable/reference/audit.md`, `operate.md`, `layout.md`, `typeset.md`, `animate.md` | whole |
| `find-animation-opportunities` skill | whole (loaded through the Skill tool; the path in the brief, `~/.claude/skills/…`, does not exist, the skill lives in the repo at `.claude/skills/` and `.agents/skills/`) |
| `/Volumes/Development/medvya/.agents/skills/review-animations/SKILL.md` | first 150 lines (the method, the ten standards, the output format). Its `STANDARDS.md` was not opened |
| `/Volumes/Development/medvya/.agents/skills/emil-design-eng/SKILL.md` | first ~120 lines (philosophy, the animation decision framework, the easing values). The rest was not read |

### Commands run

| Command | Result |
|---|---|
| `impeccable detect --json src ../../packages/ui/src` from `web/clinic` | Exit 2, **2 findings**, both the same rule: `side-tab` (thick one-side border) at `packages/ui/src/components/ui/table.tsx:62` and its test file. In context this is the selected-row marker, added so selection is not shown by background alone. I count it as a false positive |
| `node docs/product-ux-audit/tools/states-measure.mjs` (written for this review) | 48 page measurements, saved to `docs/product-ux-audit/evidence/reviewer-b-measure.jsonl` |
| `node docs/product-ux-audit/tools/states-capture.mjs` (written for this review; the first run crashed in its "loading" step on a Playwright route error, I fixed the script and re-ran the remaining sections) | 130 screenshots in `docs/product-ux-audit/screenshots/local-stack/states/`. Notes from the second run are in `evidence/reviewer-b-states-loading-small-locale-zoom-nurse.json`. The first run crashed before it wrote its notes file, so the desktop notes (tab order, hidden-focus counts, validation) exist only in my session log and are quoted in this report |
| `node docs/product-ux-audit/tools/states-focus.mjs` (written for this review) | Settled focus-ring colours for 24 tab stops, notification panel screenshot |
| Python over `evidence/local-metrics.jsonl` | All 615 captured pages analysed |
| `grep` over `web/clinic/src` and `packages/ui/src` for motion code | Inventory in section 5 |
| One web-fetch agent for the references | Results in section 9 |

### Substitutions and limits

- **No interactive user.** The skill's interview steps (target, priorities, tone, scope, constraints) were replaced by the brief. The closing questions are therefore skipped. `Questions skipped: no interactive user in this run; the brief stands in for the answers.`
- **`impeccable context` was not run.** I read the project's design docs directly instead. **The project has no `PRODUCT.md` and no `DESIGN.md`.** That is a gap: there is no single statement of who the screens are for, how dense they should be, or what the composition rules are. I did not create them.
- **Order of the two assessments.** The brief told me to run the detector first, so its output was in context before I looked at screens. It returned one rule, so the risk of it steering my judgement is small, but the skill's ordering was not followed.
- **No snapshot was saved** with `critique-storage` or `audit-storage`: those write outside the one folder I was allowed to write in.
- **The Ponytail skill was not invoked.** This was an audit with no development work.
- **Screens I looked at with my own eyes: about 55 PNGs** (26 from the local capture across 1920, 1440, 1024, 768 and 375; 3 from Azure; about 26 state screenshots). The other pages are covered by numbers only, through the metrics for all 615 captures. I did not open every screenshot.
- **The consultation I reviewed had already ended** (the only consult in the capture plan). Fields render, but a live consultation with recording and an active prescription may look different. I did not start one, because that would change clinic data.
- **Nurse intake could not be reviewed.** `/app/nurse` answers "Page not found" for both the doctor and the nurse on this stack (the intake switch is off).
- **Viewport emulation in desktop Chromium**, not physical tablets or phones. No screen reader was used. Touch gestures were not exercised. Dark mode does not ship, so it was not reviewed.
- **Zoom** was tested as an equivalent viewport (720x450 for 200 % of 1440x900, 320x256 for 400 % of 1280x1024), not with the browser's zoom control.
- **Mobile apps and the platform console are code-only reviews**, not rendered.
- `local-metrics.jsonl` holds no API errors (the capture tool appends them after writing the row), so I did not analyse API errors.

## 2 · Verdict in one page

**The foundation is good and should be kept. The page compositions are the problem.**

What the numbers say about the foundation, across 615 captured pages (7 roles, 5 widths):

- Horizontal page overflow: **0 pages**. The Azure header overflow at 768 and 375 (I confirmed it in `azure-dev/receptionist/front-desk/768-default.png` and `375-default.png`) is fixed on `main`.
- axe violations: **5**, all the same moderate rule (`page-has-heading-one`).
- Text contrast: every colour pair I measured on 15 key screens passes 4.5:1, except disabled button labels (2.56:1, exempt).
- Status is never colour alone: every chip, tag and schedule state I saw has an icon and a text label.
- Arabic right-to-left works on the 8 screens I checked at 3 widths, with no overflow and no untranslated interface text. Hindi had one untranslated word ("Assist").
- Focus ring: 2px solid `#2563EB` with a 2px offset on every one of 24 tab stops, white on the navy sidebar.

What the numbers say about the compositions:

| Screen | Share of the first viewport left for the actual work at 1024x768 |
|---|---|
| Consultation | **3 %** (first text box at 645px, the sticky sign bar starts at 666px) |
| Settings · medicines | 23 % |
| Patients directory (first row) | 28 % |
| Registration (first field) | 32 % |
| Schedule (first slot) | 38 % |
| Reception board (first patient card) | 41 % |

A doctor and a receptionist use this all day. The screens look calm, but they are not efficient: each page stacks a 60px top bar, a small label, a 24px title, a caption, a view switch, an action row and a helper card before the work starts.

**Design health (Nielsen, 0–4 each): 26 / 40, "Acceptable".**

| # | Heuristic | Score | Key issue |
|---|---|---|---|
| 1 | Visibility of system status | 3 | Live chip, offline state and busy buttons are good. Autosave is a static sentence, not a status |
| 2 | Match with the real world | 3 | Plain clinic language. Some system talk on screen ("Facility time · Asia/Kolkata") |
| 3 | User control and freedom | 3 | Back links and Escape work. Sign out sits one click from search |
| 4 | Consistency and standards | 2 | Five styles of view switch, three patient headers, seven title positions |
| 5 | Error prevention | 3 | Identity check before check-in, disabled-with-reason sign button |
| 6 | Recognition, not recall | 3 | Icon-only navigation between 768 and 1279px has no visible labels |
| 7 | Flexibility and efficiency | 2 | Ctrl+K search exists. Density is low and there are no other accelerators |
| 8 | Aesthetic and minimal design | 2 | Clean, but permanent helper text and stacked chrome crowd out the work |
| 9 | Error recovery | 3 | Messages are plain and say nothing was lost. Registration errors can be off screen |
| 10 | Help | 2 | Inline hints only |

**Technical audit (Impeccable audit, 0–4 each): 14 / 20, "Good".** Accessibility 3 · Performance 3 (provisional, not profiled) · Theming 3 (tokens used well, light theme only) · Responsive 2 · Implementation integrity 3.

**Design specificity.** This does not read as a generic template. The identity banner, the provenance tags, the "receptionist view hides clinical notes" chip, the masked MRN in search results and the amber "registration incomplete" flag are all specific to a clinic. What is generic is the layout skeleton: every page is "title, then cards in a column", whether the task is a live board, a long clinical form or a table.

## 3 · Measurements

Method: `states-measure.mjs`, doctor role, three viewports, read from the rendered DOM.

- **Px before first working content**: the top edge of the first thing the user came for (first patient card on the board, first slot on the schedule, first text box in the consultation, first table row in a list, first field in a form).
- **Useful share**: the part of the first viewport below that edge and above any fixed bottom bar. For the consultation I also removed the sticky sign bar.
- **Widest paragraph** is in approximate characters (width ÷ half the font size).
- The primary-button count is not given for the consultation and Settings · hours: my script also matched the seven switches on those pages, so its figure (9) was wrong. From the screenshots, each has one or two real primary buttons.
- Every key screen had **no horizontal page overflow**, uses **Manrope plus JetBrains Mono**, and has main padding of 24px (16px at 375).

| Screen | Viewport | Px before first working content | Useful share of first viewport | Font sizes / weights / size-weight pairs | Button heights (px: count) | Card paddings | Widest paragraph (ch) | Primary-styled buttons in view / on page | Page height (viewports) | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| today | 1440x900 | 174 | 81% | 6 / 4 / 9 | 44: 8, 48: 1 | 10px 12px ×9, 16px ×1 | 161 | 3 / 6 | 1.6 | – |
| front-desk | 1440x900 | 452 | 50% | 7 / 4 / 13 | 44: 17 | 10px 12px ×9, 16px ×1 | 77 | 3 / 7 | 2.3 | names and "Start consultation" wrap to 2 lines |
| front-desk-overview | 1440x900 | 259 | 71% | 6 / 4 / 10 | 44: 12 | 16px ×3, 0px ×1 | 161 | 0 / 0 | 1.6 | – |
| schedule | 1440x900 | 318 | 65% | 4 / 3 / 8 | 44: 9, 47: 4, 48: 3, 96: 6 | 0px ×1, 16px ×1 | 120 | 0 / 0 | 1.1 | – |
| walk-in | 1440x900 | 295 | 67% | 4 / 4 / 6 | 44: 8 | 16px ×1 | 122 | 1 / 1 | 1.0 | – |
| register-patient | 1440x900 | 403 | 55% | 4 / 4 / 9 | 44: 12 | 16px ×3 | 114 | 0 / 1 | 1.2 | – |
| register-quick | 1440x900 | 241 | 73% | 4 / 4 / 8 | 44: 3 | 16px ×3, 12px ×1 | 104 | 1 / 1 | 1.0 | – |
| patients-directory | 1440x900 | 533 | 41% | 4 / 4 / 7 | 44: 15 | 16px ×1, 0px ×1 | 160 | 1 / 1 | 1.2 | – |
| patient-360 | 1440x900 | 338 | 62% | 5 / 4 / 9 | 44: 34 | 12px ×13, 16px ×12 | 141 | 0 / 0 | 3.6 | – |
| patient-record | 1440x900 | 407 | 55% | 4 / 4 / 9 | 44: 15 | 16px ×8 | 186 | 1 / 1 | 2.5 | – |
| consult | 1440x900 | 589 | 23% | 6 / 4 / 11 | 18: 7, 44: 41 | 16px ×7, 12px ×3, 24px ×3, 0px ×2 | 166 | not counted (see note below the table) | 4.0 | sticky sign bar 102px at y=798; inner h-scroll: 1108/1386 px |
| settings-profile | 1440x900 | 385 | 57% | 4 / 4 / 7 | 44: 13 | 24px ×2 | 138 | 1 / 1 | 1.4 | – |
| settings-hours | 1440x900 | 539 | 40% | 4 / 4 / 7 | 18: 7, 44: 41 | 24px ×2, 12px ×1 | 154 | not counted (see note below the table) | 2.5 | – |
| settings-staff | 1440x900 | 425 | 53% | 4 / 3 / 6 | 44: 18 | 24px ×2, 0px ×1 | 138 | 0 / 1 | 1.4 | – |
| settings-medicines | 1440x900 | 544 | 40% | 4 / 5 / 8 | 44: 11 | 24px ×4, 0px ×1 | 157 | 0 / 2 | 3.6 | – |
| today | 1024x768 | 174 | 77% | 6 / 4 / 9 | 44: 8, 48: 1 | 10px 12px ×9, 16px ×1 | 146 | 2 / 6 | 1.9 | – |
| front-desk | 1024x768 | 452 | 41% | 7 / 4 / 13 | 44: 17 | 10px 12px ×9, 16px ×1 | 77 | 3 / 7 | 2.8 | text spills out of card: 17:00 +18px, 18:20 +9px, 19:00 +10px; inner h-scroll: 904/996 px; names and "Start consultation" wrap to 2 lines |
| front-desk-overview | 1024x768 | 259 | 66% | 6 / 4 / 10 | 44: 12 | 16px ×3, 0px ×1 | 124 | 0 / 0 | 2.0 | – |
| schedule | 1024x768 | 480 | 38% | 4 / 3 / 8 | 44: 9, 47: 4, 48: 3, 96: 6 | 0px ×1, 16px ×1 | 137 | 0 / 0 | 1.3 | – |
| walk-in | 1024x768 | 295 | 62% | 4 / 4 / 6 | 44: 8 | 16px ×1 | 122 | 1 / 1 | 1.0 | – |
| register-patient | 1024x768 | 519 | 32% | 4 / 4 / 9 | 44: 12 | 16px ×3 | 145 | 0 / 1 | 2.0 | – |
| register-quick | 1024x768 | 305 | 60% | 4 / 4 / 8 | 44: 3 | 16px ×3, 12px ×1 | 145 | 0 / 1 | 1.6 | – |
| patients-directory | 1024x768 | 553 | 28% | 4 / 4 / 7 | 44: 15 | 16px ×1, 0px ×1 | 124 | 1 / 1 | 1.4 | – |
| patient-360 | 1024x768 | 338 | 56% | 5 / 4 / 9 | 44: 34 | 12px ×13, 16px ×12 | 124 | 0 / 0 | 5.0 | – |
| patient-record | 1024x768 | 407 | 47% | 4 / 4 / 9 | 44: 15 | 16px ×8 | 145 | 1 / 1 | 3.0 | – |
| consult | 1024x768 | 645 | 3% | 6 / 4 / 11 | 18: 7, 44: 41 | 16px ×7, 12px ×3, 24px ×3, 0px ×2 | 129 | not counted (see note below the table) | 5.3 | sticky sign bar 102px at y=666; inner h-scroll: 904/971 px; 852/1386 px |
| settings-profile | 1024x768 | 385 | 50% | 4 / 4 / 7 | 44: 13 | 24px ×2 | 107 | 0 / 1 | 1.6 | – |
| settings-hours | 1024x768 | 559 | 27% | 4 / 4 / 7 | 18: 7, 44: 41 | 24px ×2, 12px ×1 | 118 | not counted (see note below the table) | 2.9 | – |
| settings-staff | 1024x768 | 449 | 42% | 4 / 3 / 6 | 44: 18 | 24px ×2, 0px ×1 | 107 | 0 / 1 | 1.7 | – |
| settings-medicines | 1024x768 | 588 | 23% | 4 / 5 / 8 | 44: 11 | 24px ×4, 0px ×1 | 122 | 0 / 2 | 4.2 | – |
| today | 375x812 | 298 | 54% | 6 / 4 / 9 | 44: 8, 48: 1 | 10px 12px ×9, 16px ×1 | 53 | 1 / 6 | 2.9 | – |
| front-desk | 375x812 | 664 | 9% | 7 / 4 / 13 | 44: 17 | 10px 12px ×9, 16px ×1 | 53 | 1 / 7 | 3.0 | text spills out of card: 17:00 +18px, 18:20 +9px, 19:00 +10px; inner h-scroll: 343/996 px; names and "Start consultation" wrap to 2 lines |
| front-desk-overview | 375x812 | 299 | 54% | 6 / 4 / 10 | 44: 12 | 16px ×3, 0px ×1 | 44 | 0 / 0 | 2.6 | inner h-scroll: 341/892 px |
| schedule | 375x812 | 692 | 6% | 4 / 3 / 7 | 44: 14, 48: 5, 64: 4 | 0px ×1, 16px ×1 | 44 | 1 / 1 | 2.3 | – |
| walk-in | 375x812 | 335 | 50% | 4 / 4 / 6 | 44: 8 | 16px ×1 | 52 | 0 / 1 | 1.2 | – |
| register-patient | 375x812 | 637 | 13% | 4 / 4 / 9 | 44: 11, 62: 1 | 16px ×3 | 52 | 0 / 1 | 2.6 | – |
| register-quick | 375x812 | 345 | 49% | 4 / 4 / 8 | 44: 3 | 16px ×3, 12px ×1 | 52 | 0 / 1 | 2.1 | – |
| patients-directory | 375x812 | 737 | 0% | 4 / 4 / 7 | 44: 15 | 16px ×1, 0px ×1 | 44 | 1 / 1 | 1.9 | inner h-scroll: 341/637 px |
| patient-360 | 375x812 | 554 | 23% | 5 / 4 / 9 | 44: 34 | 12px ×13, 16px ×12 | 44 | 0 / 0 | 6.3 | – |
| patient-record | 375x812 | 415 | 40% | 4 / 4 / 9 | 44: 15 | 16px ×8 | 57 | 1 / 1 | 3.2 | – |
| consult | 375x812 | 905 | 0% | 6 / 4 / 11 | 18: 7, 44: 41 | 16px ×7, 12px ×3, 24px ×3, 0px ×2 | 49 | not counted (see note below the table) | 6.1 | sticky sign bar 162px at y=700; inner h-scroll: 343/971 px; 291/1386 px; 291/640 px |
| settings-profile | 375x812 | 497 | 30% | 4 / 4 / 7 | 44: 13 | 24px ×2, 4px ×1 | 37 | 0 / 1 | 1.9 | – |
| settings-hours | 375x812 | 919 | 0% | 4 / 4 / 7 | 18: 7, 44: 41 | 24px ×2, 4px ×1, 12px ×1 | 38 | not counted (see note below the table) | 5.1 | – |
| settings-staff | 375x812 | 537 | 25% | 4 / 3 / 6 | 44: 18 | 24px ×2, 4px ×1, 0px ×1 | 37 | 0 / 1 | 1.9 | inner h-scroll: 291/720 px |
| settings-medicines | 375x812 | 740 | 0% | 4 / 5 / 8 | 44: 11 | 24px ×4, 4px ×1, 0px ×1 | 42 | 0 / 2 | 5.2 | inner h-scroll: 291/760 px |

Readings that matter:

1. **Button height is consistent**: 44px almost everywhere (48px on one Today button, 18px switches). This is a real strength.
2. **Title position is not consistent**: the `h1` top edge sits at 83, 84, 102, 104, 108, 148 or 161px depending on the screen (with or without the small label above, with or without a back link).
3. **Card padding has four values** (10x12, 12, 16, 24px) and two radii (12, 16px), mixed on the same screen.
4. **Paragraphs run to 120–186 characters** on wide screens. The project's own "reading" width (`max-w-6xl`) does not limit paragraphs.
5. **Primary buttons compete** on Today and on the doctor's board: 3 in the first viewport, 6 and 7 on the page.

### Drift from the project's own spec

| Spec (`docs/02-design/tokens.md`, `responsive-and-dark.md`) | Build | Evidence |
|---|---|---|
| Type scale of 7 sizes: 12, 14, 15, 16, 18, 24, 32 | **12 sizes in use**: 12, 12.5, 13, 13.5, 14, 15, 16, 17, 18, 20, 24, 30 | Font histogram over 615 pages |
| Body 15/24 at weight 500 | Body is **14px at weight 400** (21,593 of about 35,000 text elements are 14px; 15px appears 55 times) | Same |
| Headings weight 700 | `h1` is weight 800 (`font-extrabold`) | `packages/ui/src/components/ui/page.tsx` |
| 8px spacing base (4, 8, 12, 16, 24, 32) | Gaps of 6px and 10px are common (6px is the most used gap on Today) | `gaps` in `reviewer-b-measure.jsonl` |
| Radius 6 · 10 · 12 · pill | 16px (`rounded-xl`) is the most used radius in the clinic app (113 uses), plus one-offs of 9px and 10px | `grep rounded-` |
| Touch targets ≥ 44px | Switch is 32x18. Select options are 32px high. Patient-name links on Today are 19px high | `local-metrics.jsonl`, my capture log |
| "Patient identity banner, fixed on every clinical screen" | The consultation banner scrolls away. Three different patient headers exist | Finding B-03, B-08 |
| Desktop sidebar from 1280px | Followed. The side effect is that every 1024–1279px laptop gets the icon-only tablet rail | `AppShell.tsx` |
| Motion 160ms ease-out | No motion token exists. Durations are Tailwind defaults (150ms) | Section 5 |
| Mobile: primary action pinned above the tab bar | Only the schedule does this (the "Book appointment" button) | `375` screenshots |
| Primary colour `#0E9384`, input border `#CBD6E0` | Changed to `#0F766E` and `#76899C` for contrast. **Recorded as deliberate deviations** in `docs/02-design/README.md`. This is good practice | – |

### Contrast, computed from the rendered colours

| Pair | Ratio | Where | Verdict |
|---|---|---|---|
| `#142536` on `#FFFFFF` | 15.58 | Body text | Pass |
| `#506579` on `#FFFFFF` / `#F7FAFC` / `#F1F5F8` | 6.04 / 5.76 / 5.51 | Secondary text, placeholders, text on board columns | Pass |
| `#FFFFFF` on `#0F766E` | 5.47 | Primary button | Pass |
| `#2563EB` on `#F7FAFC` | 4.93 | Links, small page label | Pass, with little margin |
| `#07774F` on `#E6F4EF` | 4.93 | "Live updates", "Clinician verified" | Pass, with little margin |
| `#7A4710` on `#FDF5E7` | 7.09 | "Registration incomplete" | Pass |
| `#1D4ED8` on `#EFF4FF` | 6.08 | Token and walk-in tags | Pass |
| `#8A1626` on `#FDECEE` | 8.28 | Allergy pill | Pass |
| `#8FA6BD` on `#102A43` | 5.83 | Sidebar group labels | Pass |
| `#8296A8` on `#E6ECF1` | **2.56** | Disabled button label | Exempt (disabled), but hard to read |
| `#76899C` on `#FFFFFF` (non-text) | 3.60 | Input and outline-button border | Pass (3:1) |
| `#2563EB` on `#F7FAFC` (non-text) | 4.93 | Focus ring | Pass |
| `#DFE7ED` on `#FFFFFF` (non-text) | 1.25 | Card border | Decorative, so not required. Cards are separated by a 1.05:1 fill difference, so on a poor clinic monitor the card edges disappear |

## 4 · Screen-by-screen critique

### App shell

Evidence: any `local-stack/doctor/*/1440-default.png`, `1024-default.png`, `375-default.png`.

- The top bar is 60px and holds **Search, Language (label and select), My devices, Sign out and the bell**. Three of the five are used about once a day or less. Sign out is a plain outline button 12px from the bell and near search, on what will often be a shared reception PC.
- At 1440px the top bar's left 400px is empty, because the facility name is only shown when the sidebar is collapsed.
- Between 768 and 1279px the navigation is a 72px rail of **icons with no visible labels and no tooltips** (labels are screen-reader only). That range includes every 1024px tablet in landscape and many small laptops.
- At 375px the header drops Language and Sign out into "More". This is the right idea, and it should be the rule at every width.
- The bottom tab bar is 72px and the header 60px. At 375x812 that is 16 % of the screen before any page content. At 400 % zoom (320x256) the two bars take 132 of 256px (52 %).

### Today (doctor)

Evidence: `doctor/today/1440-default.png`, `1024-default.png`, `375-default-full.png`.

The best composed screen in the product. The next patient is first, the action is obvious, and the list below is scannable. Problems:

- The "Next patient" card and the first row of "Today's visits" are the **same patient with the same button**. That is three "Start consultation" buttons in one viewport.
- Each visit row is about 100px tall for one line of information and two chips. Seven patients need 1,410px at 1440. A clinic day of 30 patients would be about 3,000px.
- "Allergies not recorded" (a question-mark chip) and "Allergies listed" (an exclamation chip) look almost the same. "Allergies listed" does not say which allergy.
- The nurse escalation chip ("Synthetic chest pain on arrival", red outline, with an icon) is the right level of emphasis. Keep it.

### Reception board

Evidence: `doctor/front-desk/1440-default.png`, `1024-default.png`, `receptionist/front-desk/768-default.png`, `375-default.png`, `1920-default.png`, `states/error/1440-front-desk-500.png`.

- **The first patient card starts at 452px** at every width from 768 to 1440. The stack above it: top bar 60, padding 24, small label 20, title 32, "Facility time · Asia/Kolkata" 18, view switch 52, action row 44, workload card 96, column header 36, plus gaps.
- The title holds the date ("Reception board · Tuesday, 3 November 2026"). That makes the title long enough to push the action buttons onto their own row. In the loading and error states the title is short and the same buttons sit beside it, 84px higher. So **the header changes shape when the data arrives**.
- The view switch ("By status / By doctor") sits inside a 500px-wide box that is two-thirds empty.
- "Receptionist view hides clinical notes" is permanent, 280px wide, and shown to the doctor too.
- At 1440px each of the six columns is 183px wide. **Patient names wrap to two lines and so does "Start consultation"** (15 wrapped labels measured). At 1920px nothing wraps. So the board is tuned for a monitor most clinics will not have.
- At 1024px the columns fall to the 156px minimum, the board scrolls sideways (904 of 996px visible), and the **appointment time is pushed 9 to 18px outside the card** (`17:00 +18px`, `18:20 +9px`, `19:00 +10px`).
- At 375px the board is still six 156px columns in a sideways scroller (343 of 996px visible), and the first card is at 664px. As a phone screen it does not work.
- Two columns out of six were empty and still took a third of the width. Empty columns are common (billing is switched off, "In consult" is usually zero or one).
- The doctor sees a primary "Start consultation" on every waiting card, so the board has up to seven primary buttons. The receptionist's version (no start button) is calmer and better.
- Good: the per-card flags (walk-in, token, waiting time, registration incomplete) all have an icon and words, the offline state disables actions and says why, and the error state says nothing was changed.

### Reception board · by doctor

Evidence: `doctor/front-desk-overview/1440-default.png`, `receptionist/front-desk-overview/768-default.png`.

- For a doctor, "Your day" repeats the first "Doctors today" card word for word, directly below it.
- The seven per-doctor counts are a run of grey text ("Booked 0 Checked in 0 With nurse 1 …") with no alignment. Numbers that are compared should be in a small table or fixed slots.
- At 768px the waiting-room table wraps "Dr. Arjun Rao (synthetic)" over three lines and "Ready for doctor" over three lines, with an empty 24px band under each row.

### Schedule

Evidence: `doctor/schedule/1440-default.png`, `1024-default.png`, `receptionist/schedule/375-default.png`, `states/schedule-panel/*.png`.

- The 1440px layout is good: grid on the left, a panel on the right, sticky column header.
- Below 1280px the "Select an empty slot" helper card moves **above** the grid and stays there (110px). At 1024x768 the first slot is at 480px. At 375x812 it is at 692px, and the toolbar has wrapped to five rows.
- The day opens at 09:00 with three "Passed" slots at 10:30. I saw no jump to the current time (inferred from the screenshot, not tested by scrolling).
- The legend is eight items with 1px coloured rings. The rings are too faint to match to the blocks. The icons and labels do the real work, which is correct for accessibility but makes the rings decoration.
- The appointment panel for a completed visit shows **four disabled 44px buttons, each followed by the same sentence** "The visit is completed."
- On desktop there is no button to book. Booking starts by clicking an empty slot, which the helper card explains. At 375px there is a floating "Book appointment" button. Two different models.

### Walk-in

Evidence: `doctor/walk-in/1440-default.png`, `receptionist/walk-in/375-default-full.png`.

A clear single-purpose form with the right width (768px). Problems: a two-line description plus a two-line hint under the field say nearly the same thing, the "Search" button is unnecessary when results appear as you type, and "Check in now" is disabled from the start with no visible reason.

### Register patient

Evidence: `doctor/register-patient/1440-default-full.png`, `receptionist/register-patient/768-default.png`, `states/validation/*.png`.

- Good structure: six named steps, a preview, required marks, hints under fields, and errors with an icon, red text and a red border.
- At 1440px the six step chips are squeezed so "Identity & IDs" and "Contact & address" wrap inside their chips, while a 300px side column shows a "Rules" card that staff will read once.
- The first field is at 403px at 1440 and 637px at 375, because of the title, description, mode switch, stepper, section title, section description and the "ABHA coming later" note.
- **After pressing Continue on an empty step, focus stays on the button and there is no summary.** At 375px all three errors are above the visible area, so the screen looks as if nothing happened (`states/validation/375-register-empty-submit.png`).
- The messages all say only "Required."

### Patients directory

Evidence: `doctor/patients-directory/1440-default.png`.

- First table row at 533px (1440) and 737px (375, so no rows in the first viewport).
- Every row says "MRN ··0008" and "Phone ··9087" under columns already headed MRN and Phone.
- All ten rows carry the amber "Incomplete registration" chip. When every row has a warning, it stops being a warning.
- The description paragraph is about 160 characters wide.

### Patient 360

Evidence: `doctor/patient-360/1440-default-full.png`, `1440-default.png`, `375-default.png`.

- Page height: 3,241px at 1440, 3,805px at 1024, 5,129px at 375.
- Six "Not recorded" vitals tiles take a full row before anything that is recorded.
- The empty "Problems" card is stretched to 240px to match the Medicines card.
- Link-style actions ("Prescribed at this visit", "Open visit") are indented about 10px from the text above them, because they are padded buttons. The left edge wobbles down the whole page.
- At 375px the six filter chips wrap to three rows and are sticky: 156px, plus the header and tab bar, is 288 of 812px (35 %) permanently covered.
- The patient header here is a different component from the one in the consultation (see B-08).

### Consultation

Evidence: `doctor/consult/1440-default-full.png`, `1440-default.png`, `1024-default.png`, `375-default.png`, `1920-default.png`, `states/focus/1440-consult-obscured-*.png`.

This is the screen that most needs work.

- Height: **3,611px at 1440, 4,093px at 1024, 4,941px at 375.** The note, templates, prescription, orders and referrals are one column.
- **The patient banner is not sticky.** After 176px of scrolling the name and the allergy are gone. The prescription section is about 2,400px down. The project's own spec says the banner is fixed on every clinical screen.
- Before the first text box: banner 92, an action row 44, a mode switch (on its own row at 1024), "Consultation note" 28, a recording notice 20, seven section chips 56, an empty Vitals card 90, then the History heading. First box at 589px (1440), 645px (1024), 905px (375).
- **The "7 · Review and sign" bar is sticky at the bottom and lies over the form**: 102px tall on desktop, 162px at 375. At 1024x768 the first text box starts at 645 and the bar starts at 666. At 375x812 the visible form area is the 72px between the sticky chips and the bar.
- Keyboard: of 45 tab stops, 2 were completely hidden behind the bar when focused (the template select and the "Referral" link) and 2 were partly hidden.
- The right-hand panel (Assist, Chart, Checklist) is three nested bordered boxes. It ends at about 940px, and the 30 % column beside the note is then empty for the next 1,200px.
- At 1920px the text boxes are 1,270px wide, about 180 characters per line.
- The prescription table is wider than its card at 1440 and below (1,108 of 1,386px visible), so "Quantity" and whatever follows it are hidden behind a sideways scroll inside the page.
- There is an unexplained 140px gap between the Plan card and "My templates".
- "Changes save automatically." is a fixed sentence. It never says "saving" or "saved at 10:42".
- Good: sections are numbered, the checklist counts 4/7, the sign button says how many items remain, required fields are marked in words, and nothing here depends on colour.

### Settings

Evidence: `doctor/settings-profile/1440-default.png`, `settings-hours/1024-default.png`, `settings-staff/375-default.png`, `settings-medicines/1440-default.png`.

- Two levels of tabs in two different styles (underlined tabs inside a card, then outlined buttons), then a card, then often a bordered table inside the card.
- Form fields are 1,100px wide for a clinic name or an address.
- The "closed every day" warning on Hours lists 18 doctor-and-day pairs as one sentence.
- Tables at 375px scroll sideways inside the card (291 of 720px visible on Staff).

### States

| State | What I saw | Evidence |
|---|---|---|
| Focus | 2px blue ring with offset on all 24 stops, white on navy. For the first 150ms the ring fades in from the text colour, so on a primary button it starts white | `states/focus/1440-settled-tab*.png` |
| Hover | Colour change only. No movement. Correct for this product | `states/hover/*.png` |
| Pressed | No pressed state on buttons | `button.tsx` |
| Disabled | Grey fill `#E6ECF1` with `#8296A8` text (2.56:1) | `walk-in`, `schedule-panel` |
| Loading | A 560px card with "Loading the board…" in bold. No skeleton. The page height jumps from 900 to 2,074px on the board when data arrives | `states/loading/1440-today.png` |
| Empty | Plain sentences ("No visits", "Nothing new."). Clear, but they do not point to the next step | board, notifications |
| Error | "The board could not be loaded. Nothing was changed." with "Try again". Good | `states/error/1440-front-desk-500.png` |
| Offline | Live chip turns amber with words, actions disable, an offline card appears bottom right. The card covers part of the "Done" column and talks about recording on the reception board | `states/error/1440-front-desk-offline.png` |
| Search dialog | 576px wide, appears instantly, focus in the field. Results show initials, age and masked MRN only | `states/global-search/1440-results.png` |
| Arabic | Mirrors correctly. The keyboard hint reads "K / Ctrl K⌘" | `states/rtl-arabic/1440-front-desk.png` |
| Hindi | Fits, with more wrapping (board buttons take two lines at 1024) | `states/hindi/1024-front-desk.png` |
| 200 % zoom of 1440x900 | Reflows to the phone layout with bottom tabs. No sideways scroll | `states/zoom/zoom200-of-1440x900-consult.png` |

## 5 · Motion

### The direct answer

**No, you do not need animation now.** Nothing a pilot clinic will struggle with is caused by missing motion. The problems are space, density and consistency. Motion is a **later** item: about six small, functional uses, after the layout work. Decorative motion is a **never** item for this product.

### What exists today

| Where | What | Assessment |
|---|---|---|
| `packages/ui/src/styles.css:6` | `tw-animate-css` imported | Used only by the select |
| `packages/ui/src/styles.css:14` | Under reduced motion: `animation: none; transition: none` on everything | Safe, but it also stops the button spinner and the "live" pulse. See F below |
| `components/ui/select.tsx:64` | Menu opens with fade and zoom from 95 %, 150ms, `ease`, from the trigger's origin. Slides 8px by side | Fine. Easing could be stronger |
| `components/ui/button.tsx:10` | `transition-colors` (150ms). `animate-spin` on the loading icon | Fine. The colour transition also covers the focus outline colour |
| `components/ui/switch.tsx:17,25` | `transition-all` on the track, `transition-transform` on the thumb | `transition: all` should name its properties |
| `components/ui/tabs.tsx:66,69` | `transition-all`, plus an opacity transition on the underline | Same |
| `components/ui/input.tsx`, `textarea.tsx`, `select.tsx` | `transition-[color,box-shadow]` | Fine |
| `components/ui/table.tsx:62` | `transition-colors` on row hover | Fine |
| `components/ui/dialog.tsx` | No animation | Fine for the keyboard-opened search. Abrupt for the tablet drawer |
| `primitives.tsx:129` | `animate-pulse` dot in the recording pill | Purposeful |
| `schedule/components/DayGrid.tsx:34`, `WeekView.tsx:19`, `AgendaView.tsx:18` | Fade to 60 % opacity while the schedule refreshes | Good: keeps the old content visible |
| `encounters/components/Disclosure.tsx:15` | Chevron rotates on open | Fine |
| `onboarding/components/OnboardingWizard.tsx:61` | Progress bar animates `width` | Layout property. Rare screen, low priority |
| `registration/components/Registration.tsx:163` | Spinner while checking duplicates | Fine |

There is no motion library, no skeleton component, no toast and no motion tokens. There is nothing gratuitous to remove.

### Review of the existing motion (review-animations format)

| Before | After | Why |
|---|---|---|
| `transition-all` on `switch.tsx:17` and `tabs.tsx:66` | `transition-[background-color,border-color,box-shadow]` | Name the properties. `all` animates things you did not intend |
| `transition-colors` on `button.tsx:10` (includes `outline-color`) | `transition-[background-color,border-color,color]` | The focus ring should appear at full colour at once. Today it fades in from the text colour for 150ms |
| Select menu: `animate-in … zoom-in-95`, 150ms, `ease` | Keep 150ms. Use `cubic-bezier(0.23, 1, 0.32, 1)` | A stronger ease-out feels more immediate at the same duration |
| `transition-[width]` on the onboarding bar | `transform: scaleX()` with `transform-origin: left` (right in RTL), 200ms ease-out | Compositor-only property |
| Global `animation: none; transition: none` under reduced motion | Stop movement, keep short colour and opacity changes (see F) | Reduced motion means less movement, not no feedback |

Verdict: **approve.** There are no feel-breaking problems, nothing animates on a keyboard shortcut, and all of it is under 300ms.

### Where motion would help, ranked

Add two tokens first so nothing invents its own numbers: `--ease-out: cubic-bezier(0.23, 1, 0.32, 1)` and `--duration-fast: 150ms` (the spec says 160ms; pick one and write it down).

| # | Where | Today | Purpose | How often | Proposed motion | Reduced-motion fallback |
|---|---|---|---|---|---|---|
| A | Board card that arrives or changes column **because of a live update from someone else** (`front-desk/components/BoardPage.tsx:187`) | The card appears in its new column with no cue. A receptionist looking at another column will miss it | State indication | Tens a day, but started by the system, not the user | No movement. Background `#E7F6F4` fading to `#FFFFFF` and a 2px `#0F766E` outline fading to transparent: `transition: background-color 1200ms ease-out, outline-color 1200ms ease-out`. Also announce it in the existing live region | Show a text tag "Updated" on the card for 5 seconds, no fade |
| B | Autosave in the consultation (`ConsultStudio.tsx`, "Changes save automatically.") | A fixed sentence | Feedback | Constant | Mostly a wording change: "Saving…" then "Saved 10:42". Swap with `opacity` 150ms `var(--ease-out)`. No tick animation, no toast | Instant text swap |
| C | Loading (`BoardPage`, `TodayRoute`, schedule, 360, settings) | A text card, then the whole layout appears and the height jumps | Preventing a jarring change | Every page open | Skeleton blocks in the final layout (column headers and three card outlines for the board). Show only after 200ms, keep for at least 400ms. Pulse `opacity` 1 → 0.6 → 1 over 1200ms ease-in-out | Static grey blocks, no pulse |
| D | Tablet schedule drawer and confirm dialogs (`SchedulePage.tsx:124`, `dialog.tsx`) | Appear instantly | Spatial consistency | Occasional | Drawer: `transform: translateX(100%)` → `0` (mirrored in RTL), 200ms `cubic-bezier(0.32, 0.72, 0, 1)`, exit 150ms. Backdrop `opacity` 150ms. Confirm dialog: `opacity: 0; transform: scale(0.97)` → settled, 150ms `var(--ease-out)`, exit 100ms. **Keep the Ctrl+K search dialog instant** | Opacity only, 100ms |
| E | Press feedback on buttons (`button.tsx`) | Colour change on hover only, nothing on press. On a touch tablet there is no hover, so a tap gives no response until the server answers | Feedback | Tens to hundreds a day | Keep it almost invisible: `:active { transform: scale(0.98) }`, `transition: transform 100ms ease-out`, plus the darker fill (`#0B5C55`) on `:active` | Darker fill only |
| F | The global reduced-motion rule (`styles.css:14`) | Removes every transition and animation | Accessibility | – | Replace with: no transforms and no keyframe movement, but allow `opacity` and colour transitions up to 150ms. Keep the spinner as a static icon with the "busy" label | – |

### Where motion must not be added

- `GlobalSearch` (Ctrl+K): keyboard-opened and used all day. **Rejected: frequency. Never animate.**
- Typing, focus moves and section jumps in the consultation note. **Rejected: clinical data entry. Anything that makes text appear to move while a doctor types is a hazard.**
- Cards sliding between board columns (a "FLIP" move). **Rejected for now: with several people changing the board, cards flying sideways draw the eye away from the patient in front of the desk. The highlight in A carries the same information.**
- Staggered entrance of Today's visit rows or board cards on page load. **Rejected: decoration on a screen opened dozens of times a day.**
- The waiting-time counters ("Waiting 19 min"). **Rejected: numbers the user is reading should not animate.**
- Hover lift or shadow on cards and table rows. **Rejected: tablets have no hover, and it adds nothing.**
- Page-to-page transitions. **Rejected: they delay the most frequent action in the product.**
- Height animation on the consultation's collapsible sections. **Rejected: opened many times per patient, and animating height is a layout animation.**
- Toasts that slide over the page. **Rejected: there is no toast today and inline "Saved." next to the control is clearer and cannot cover the patient banner.**

### Priority relative to everything else

| Item | When |
|---|---|
| Fix `transition-all` and the focus-ring fade (two one-line changes) | With the next UI change |
| B (autosave wording), C (skeletons) | With the consultation and board layout work, because they fix real status and layout-shift problems |
| A (live-update highlight) | Before the pilot if two or more people will share one board, otherwise just after |
| D, E, F | Later |
| Everything in the "must not" list | Never |

## 6 · Mobile apps (code-only, not rendered)

Metro bundling fails on this machine (`Cannot find module '@babel/helper-define-polyfill-provider'`), so there are no device screenshots. This is from reading `packages/ui-native/src`, `mobile/clinician/src` and `mobile/patient/src`.

Good:

- One small kit (`Button`, `TextField`, `Chip`, `ListRow`, `Card`, `Screen`, `StateMessage`) shared by both apps, using the same colour tokens as the web.
- The two apps differ in exactly four values (`brand.tsx`): accent colour, tint, target size (44 or 48) and body size (15 or 17). That is a clean way to do it.
- Buttons are at least 52px tall. Rows are at least 56px.
- Chips always carry text. Field errors carry an icon and text. The chevron flips in RTL.
- `Screen` has a pinned footer slot for the primary action, which the web app lacks.
- Text allows system scaling up to 200 %.
- No animation library is used. Pressed state is an opacity change. That is the right level for now.

Concerns:

| # | Concern | Where | Why it matters |
|---|---|---|---|
| 1 | The secondary button and the icon button use `theme.color.line` (`#DFE7ED`) as their border: 1.25:1 on white. The web app fixed the same problem by adding `--color-input` (3.6:1) | `controls.tsx` (`secondary`, `iconButton`) | A white button on a white card with an invisible edge. WCAG 1.4.11 applies to the boundary of a control |
| 2 | Placeholder text is `ink400` (`#8296A8`): 3.05:1 on white | `controls.tsx` `TextField` | Below 4.5:1 if the placeholder carries a format hint |
| 3 | Disabled is `opacity: 0.45` on the whole button | `controls.tsx` `off` | The web uses a grey fill instead. Two different "disabled" looks in one product |
| 4 | No font is loaded. The apps render in the system font, the web in Manrope | no `expo-font` or `useFonts` in `mobile/*` | The brand looks different on the phone. Decide whether that is intended |
| 5 | Radii are 8, 10, 12, 14, 16 and 22. The web scale is 6, 10, 12, 16 | `controls.tsx`, `display.tsx`, `layout.tsx` | Two radius systems |
| 6 | `Loading` is a centred spinner with no visible words | `display.tsx` | On a slow clinic connection the user sees only a spinner |
| 7 | Line height is always 1.35 × size | `text.tsx` | Tight for 15px body text in Hindi and Arabic, which have taller glyphs |
| 8 | The clinician Today card shows the allergy as one chip among result and brief chips | `TodayScreen.tsx` `flagLook` | Same weight as an AI brief. The allergy should come first and not wrap away |
| 9 | Queue status tone map uses green for "next" and amber for "waiting" | `TodayScreen.tsx` `statusTone` | Amber is the web's colour for "attention, uncertain". Waiting is normal, not a warning |

All of these are inferred from code. None is confirmed on a device.

## 7 · Platform admin console (code-only, not rendered)

From `web/platform-admin/src`.

- It imports the same `@medvya/ui` styles and tokens, the same navy sidebar classes, the same button and table components. It will look like the same product. Good.
- It does not use `PageContainer` or `PageHeader`. Each page writes its own `<h1 className="text-2xl font-extrabold">`. The clinic app's page-header rules (and any fix to them) will not reach the console.
- The active navigation item has no coloured rail marker, only a darker fill. The clinic app has both.
- Below 768px the sidebar becomes a block on top of the page with a sideways-scrolling list, not a tab bar. The top bar is not sticky. For an internal tool used on a laptop this is acceptable.
- The top bar again holds the language switch and Sign out permanently, the same pattern I criticise in the clinic app.
- Padding values `p-1`, `p-2`, `p-3`, `p-4`, `p-5`, `p-6` are all in use across five small pages.

## 8 · Findings

Severity: P0 safety or blocker · P1 critical workflow · P2 major usability · P3 polish. No finding is P0.

Each finding lists: where · severity · observed or inferred · evidence · problem · consequence · how to reproduce · fix · depends on · confidence · who should validate.

Paths are relative to `docs/product-ux-audit/screenshots/`.

---

**B-01 · The sticky sign bar covers the consultation form**

- Where: `/consult/:id`, doctor, all widths, worst at 1024x768 and 375x812.
- Severity: **P1**. Observed.
- Evidence: `local-stack/doctor/consult/1024-default.png`, `375-default.png`, `states/focus/1440-consult-obscured-15.png`; `reviewer-b-measure.jsonl` (bar 102px at y=666 on 1024x768; 162px at y=700 on 375x812); my capture log (2 of 45 tab stops fully hidden; re-run `states-capture.mjs desktop` to reproduce).
- Problem: "7 · Review and sign" is `position: sticky; bottom: 0` over the note. At 1024x768 no text box is visible on arrival. At 375 the form is visible through a 72px slot. Two focusable controls are entirely hidden behind it when focused.
- Consequence: the doctor scrolls after every field, and a keyboard user can type into a control they cannot see. WCAG 2.4.11 Focus Not Obscured (Minimum, AA) is at risk: the criterion fails when the focused control is entirely hidden by author content.
- Reproduce: open the consultation at 1024x768; press Tab until the template select ("General OPD") has focus.
- Fix: at 1280px and wider, move "Sign note" and the remaining-items count into the foot of the right-hand panel and remove the bottom bar. Below 1280px, keep a bar but make it one 56px row, add `padding-bottom: 72px` to the form and `scroll-padding-bottom: 72px` to the scroll container.
- Depends on: frontend.
- Confidence: high.
- Validate: frontend developer with a keyboard, then a doctor on a tablet.

**B-02 · Half the reception board's first screen is chrome**

- Where: `/front-desk`, receptionist and doctor, 768 to 1440.
- Severity: **P1**. Observed.
- Evidence: `local-stack/doctor/front-desk/1440-default.png`, `1024-default.png`, `receptionist/front-desk/768-default.png`; first card at 452px (50 % useful at 1440x900, 41 % at 1024x768, 9 % at 375x812).
- Problem: top bar, label, dated title, time-zone caption, view switch, action row and a workload card all sit above the columns.
- Consequence: on a 768px-high screen the receptionist sees about one and a half patients per column and scrolls a board that exists to be read at a glance.
- Reproduce: open the board at 1024x768.
- Fix: the layout in section 11.2. Target: first card at or above 190px.
- Depends on: frontend, and a product decision on where "Doctor workload" lives.
- Confidence: high.
- Validate: a receptionist at the pilot clinic's real screen size.

**B-03 · The patient banner scrolls away on a 3,600px clinical page**

- Where: `/consult/:id`, doctor, all widths.
- Severity: **P1**. Observed for the layout, inferred for the risk.
- Evidence: `local-stack/doctor/consult/1440-default-full.png`; sticky elements measured on the page are the section chips, the sign bar and the tools panel, not the banner.
- Problem: the name and the allergy line leave the screen after 176px of scrolling. The prescription is about 2,400px down.
- Consequence: the doctor prescribes with the patient's identity and allergy out of sight. This also breaks the project's own rule ("patient identity banner, fixed on every clinical screen", `docs/02-design/tokens.md`).
- Reproduce: open the consultation and scroll to "Prescription".
- Fix: make the banner sticky under the top bar. When stuck, compact it to one 56px row: name, age and sex, MRN, and the allergy pill (icon and words, never shortened).
- Depends on: frontend.
- Confidence: high for the observation, medium for how the pilot doctors will rate the risk.
- Validate: clinical safety lead.

**B-04 · The consultation is one 3,600 to 4,900px column**

- Where: `/consult/:id`, doctor.
- Severity: **P2**. Observed.
- Evidence: `local-stack/doctor/consult/1440-default-full.png`, `1920-default.png`; heights 3,611 / 4,093 / 4,941px; prescription table 1,108 of 1,386px visible; text boxes 1,270px wide at 1920.
- Problem: note, templates, prescription, orders and referrals are stacked. The right panel ends early and leaves 30 % of the width empty. The prescription table hides its last columns. An empty Vitals card and a recording notice come before the first field.
- Consequence: a doctor seeing 30 patients a day scrolls about four screens per patient.
- Reproduce: open the consultation at 1440x900 and take a full-page screenshot.
- Fix: the workspace in section 11.3.
- Depends on: frontend, with a product decision on tabs against one page.
- Confidence: high on the measurements. Medium on the final structure, because I saw only an ended consultation.
- Validate: two doctors doing a timed consultation.

**B-05 · Board cards wrap and overflow at normal laptop widths**

- Where: `/front-desk`, 768 to 1440.
- Severity: **P2**. Observed.
- Evidence: `local-stack/doctor/front-desk/1440-default.png` (names and "Start consultation" on two lines), `1024-default.png` and `receptionist/front-desk/768-default.png` (time outside the card); measured spill `17:00 +18px`, `18:20 +9px`, `19:00 +10px`.
- Problem: columns are `minmax(156px, 1fr)`. At 1440 they are 183px, which leaves a 137px button. A long name pushes the time out of the card because the name does not shrink.
- Consequence: uneven card heights, slower scanning, and a time that looks like it belongs to the next column.
- Reproduce: open the board at 1024x768 and look at "Meenakshi Sundarameejh".
- Fix: minimum column width 208px. Collapse empty columns to a 48px stub with the count. Add `min-w-0` and `break-words` to the name and `shrink-0` to the time. Shorten the card buttons to "Start" and "Call in" with the full sentence as the accessible name. Let the board scroll sideways when the columns do not fit.
- Depends on: frontend.
- Confidence: high.
- Validate: frontend developer with the long-name fixtures.

**B-06 · On a phone the board is six 156px columns in a sideways scroller**

- Where: `/front-desk`, 375.
- Severity: **P2**. Observed.
- Evidence: `local-stack/receptionist/front-desk/375-default.png`; 343 of 996px visible; first card at 664px.
- Problem: two columns fit on screen. The patient cards start below the fold.
- Consequence: a doctor or receptionist checking the queue on a phone cannot see it.
- Reproduce: open the board at 375x812.
- Fix: below 768px, replace the columns with one list and a sticky row of status tabs with counts ("Ready 5 · With nurse 1 · Done 3"). Empty statuses are greyed, not removed.
- Depends on: frontend, and a product decision on whether phone reception is in the pilot.
- Confidence: high.
- Validate: product owner.

**B-07 · Language and Sign out are permanent in the top bar**

- Where: app shell, 768 and wider.
- Severity: **P2**. Observed.
- Evidence: every `1440-default.png`; `AppShell.tsx` header.
- Problem: Language (label and 160px select), My devices and Sign out take about 400px of a 60px bar. Sign out is an ordinary outline button beside the bell.
- Consequence: wasted height on every screen, and a one-click sign-out on a shared PC.
- Reproduce: open any page at 1024px or wider.
- Fix: an account menu on the avatar holding Language, My devices, Change facility and Sign out. The top bar keeps facility name, search and the bell, at 48px.
- Depends on: frontend.
- Confidence: high.
- Validate: product owner.

**B-08 · Three different patient headers, and the allergy looks different in each**

- Where: `/consult/:id`, `/patients/:id`, `/patients/directory/:id`, `/today`.
- Severity: **P2**. Observed.
- Evidence: `local-stack/doctor/consult/1440-default.png` (teal outline box, allergy as bold text with a grey icon), `patient-360/1440-default.png` (teal side rail, avatar, red allergy pill), `receptionist/patient-record/768-default-full.png` (plain card), `states/ui-gallery/1440-default.png` (the designed banner).
- Problem: the gallery banner in `packages/ui` and the banner in `features/encounters/components/IdentityBanner.tsx` are separate components. On the consultation, the most safety-relevant screen, the allergy is the least prominent of the three.
- Consequence: the doctor has to learn where to look on each screen.
- Reproduce: open the same patient in the consultation and in Patient 360.
- Fix: one banner component with two sizes (full and compact). The allergy is always the red pill with an icon and words. The registration page keeps a plain header, because it must not show clinical facts.
- Depends on: frontend.
- Confidence: high.
- Validate: clinical safety lead.

**B-09 · Five different looks for "switch the view"**

- Where: board, Today, schedule, directory, Patient 360, registration, settings, consultation.
- Severity: **P2**. Observed.
- Evidence: `front-desk/1440-default.png` (two buttons in a wide box), `today/1440-default.png` and `schedule/1440-default.png` (separate outline buttons, the selected one teal), `register-patient/1440-default-full.png` (grey track with a white pill), `settings-profile/1440-default.png` (underlined tabs in a card, then outline buttons), `consult/1440-default.png`.
- Problem: the same interaction has five appearances.
- Consequence: the product feels assembled, not designed. Selected state relies on a thicker teal border, which is easy to miss.
- Reproduce: visit the screens listed.
- Fix: one segmented control in `packages/ui` (grey track, white selected segment, 44px tall, icon optional) for switching a view. One tab style for settings sections. Filters (directory, Patient 360) become chips with a tick when on.
- Depends on: frontend.
- Confidence: high.
- Validate: design owner.

**B-10 · Page headers differ in position and change shape when data loads**

- Where: all pages.
- Severity: **P2**. Observed.
- Evidence: `h1` top at 83, 84, 102, 104, 108, 148 and 161px (`reviewer-b-measure.jsonl`); `states/error/1440-front-desk-500.png` against `doctor/front-desk/1440-default.png` (action row at y=154 when loading or failed, y=238 when loaded).
- Problem: some pages have a small label above the title, some do not, some have a back link. The board and Today put the date in the title, so the title grows when data arrives and pushes the actions to a new row.
- Consequence: the eye re-finds the title on every page, and the board header jumps on load.
- Reproduce: reload the board with a slow connection.
- Fix: one header row of fixed height (44px): title at 20px, the date as plain secondary text after it, actions on the same row. Drop the small label where it repeats the navigation item. Reserve the date's space while loading.
- Depends on: frontend.
- Confidence: high.
- Validate: design owner.

**B-11 · The type scale has drifted from the spec**

- Where: whole clinic app.
- Severity: **P2**. Observed.
- Evidence: 12 sizes over 615 pages (12, 12.5, 13, 13.5, 14, 15, 16, 17, 18, 20, 24, 30); body is 14px weight 400; spec says 7 sizes with body 15/24 weight 500.
- Problem: sizes such as 12.5 and 13.5px exist only to squeeze text into narrow board columns. 59 text elements on the board are 12px.
- Consequence: small, light text on a tablet at arm's length, and no dependable hierarchy.
- Reproduce: run `states-measure.mjs`.
- Fix: decide the scale and write it into the tokens: 12 (caption), 14 (dense and tables), 15 (body), 18 (section), 20 (page title), 24 (patient name). Remove 12.5, 13, 13.5 and 17. Use 15px for anything a person reads as a sentence.
- Depends on: design decision, then frontend.
- Confidence: high.
- Validate: design owner. Optional design advice, not a WCAG requirement.

**B-12 · Tablet and small-laptop navigation is icons only**

- Where: app shell, 768 to 1279.
- Severity: **P2**. Observed.
- Evidence: `local-stack/doctor/today/1024-default.png`; `AppShell.tsx` (`max-lg:sr-only` on labels, no `title`).
- Problem: six unlabeled icons. Two of them are calendars (Today and Schedule).
- Consequence: new staff guess. A 1024 to 1279px laptop is treated as a tablet.
- Reproduce: open any page at 1024px.
- Fix: keep the 72px rail but show an 11px label under each icon (56px-tall items), as the bottom tab bar already does. Or move the full-sidebar breakpoint down to 1024px with a 200px sidebar.
- Depends on: frontend.
- Confidence: high.
- Validate: a new receptionist in a hallway test.

**B-13 · Registration errors can be off screen, and focus does not move**

- Where: `/patients/new`, all roles, worst at 375.
- Severity: **P2**. Observed.
- Evidence: `states/validation/375-register-empty-submit.png` (no error visible after submit), `1440-register-empty-submit.png`; my capture log (focus stays on the button, no summary).
- Problem: on Continue with missing fields, the errors appear beside the fields but nothing moves to them.
- Consequence: on a phone or a short tablet the receptionist presses Continue and sees no change. The criterion 3.3.1 Error Identification (A) is met in text, so this is a usability failure, not a WCAG failure.
- Reproduce: at 375x812 open full registration, scroll to Continue, press it.
- Fix: on a failed submit, move focus to the first field in error (which also scrolls it into view). Add a short summary above the step ("3 things to fix") that links to each field. Replace "Required." with the field's name ("Enter the full name").
- Depends on: frontend.
- Confidence: high.
- Validate: frontend developer and a screen-reader check.

**B-14 · The schedule grid starts low on anything smaller than a desktop**

- Where: `/schedule`, 375 to 1279.
- Severity: **P2**. Observed.
- Evidence: `local-stack/doctor/schedule/1024-default.png` (first slot at 480px), `receptionist/schedule/375-default.png` (692px).
- Problem: the helper card "Select an empty slot" sits above the grid permanently. The toolbar wraps to two rows at 1024 and five at 375. The legend has eight items.
- Consequence: 38 % of a tablet screen and 6 % of a phone screen shows appointments.
- Reproduce: open the schedule at 1024x768.
- Fix: remove the helper card below 1280px (the empty slots can say "Tap to book" themselves). Put Print and Leave in an overflow menu. Put the legend behind a "Key" button. Scroll to the current time on open.
- Depends on: frontend.
- Confidence: high for the layout. The scroll-to-now gap is inferred.
- Validate: receptionist on a tablet.

**B-15 · Loading is a sentence in a card, and the page jumps when data arrives**

- Where: board, Today, schedule, Patient 360, consultation, settings.
- Severity: **P2**. Observed.
- Evidence: `states/loading/1440-today.png`; page height 900px while loading, 2,074px after, on the board.
- Problem: no skeletons anywhere (`grep Skeleton` finds none). About 40 places render a loading sentence.
- Consequence: every navigation shows a flash of a different layout.
- Reproduce: throttle the network and open the board.
- Fix: a `Skeleton` in `packages/ui`, with one layout skeleton each for the board, Today, the schedule and a record page. Show after 200ms, hold for at least 400ms.
- Depends on: frontend.
- Confidence: high.
- Validate: frontend developer.

**B-16 · Patients directory: the list starts at 533px and repeats itself**

- Where: `/patients/directory`.
- Severity: **P2**. Observed.
- Evidence: `local-stack/doctor/patients-directory/1440-default.png`; first row at 533 / 553 / 737px.
- Problem: a description, a link, a search card with a hint, three filter buttons and a count sit above the table. Cells repeat "MRN" and "Phone". Every row has the same amber chip.
- Consequence: three to seven patients visible on a laptop.
- Reproduce: open the directory at 1024x768.
- Fix: put search and filters on one 44px row. Move the hint into the placeholder or an info button. Drop the repeated cell labels. Show the registration column as text ("Incomplete") and keep the amber chip for a filtered view.
- Depends on: frontend.
- Confidence: high.
- Validate: receptionist.

**B-17 · Patient 360 is long, and its sticky filter takes a third of a phone**

- Where: `/patients/:id`.
- Severity: **P2**. Observed.
- Evidence: `local-stack/doctor/patient-360/1440-default-full.png`, `375-default.png`; 3,241 to 5,129px; sticky chip block 156px at 375.
- Problem: empty sections take as much room as full ones. Link buttons are indented from their text.
- Consequence: the doctor scrolls past six "Not recorded" tiles to reach the medicines.
- Reproduce: open the chart at 375x812.
- Fix: collapse empty sections to one line ("Vitals: none recorded · Add"). Make the filter chips one sideways-scrolling row (52px). Align link buttons to the text edge with a negative margin.
- Depends on: frontend.
- Confidence: high.
- Validate: doctor.

**B-18 · Text lines and form fields are far too wide on large screens**

- Where: directory, Today, patient record, settings, consultation at 1440 and wider.
- Severity: **P3**. Observed.
- Evidence: widest paragraph 161 to 186 characters; settings inputs 1,100px; consultation text boxes 1,270px at 1920.
- Problem: prose is not width-limited.
- Consequence: harder reading, and forms that look unfinished.
- Reproduce: open Settings · profile at 1440px.
- Fix: `max-width: 72ch` on descriptions and hints. Settings forms in the 768px "form" width. Note column 760px.
- Depends on: frontend.
- Confidence: high.
- Validate: design owner. Optional design advice.

**B-19 · Too many primary buttons per view**

- Where: Today and the board (doctor).
- Severity: **P3**. Observed.
- Evidence: 3 primary buttons in the first viewport on Today and on the board; 6 and 7 on the page (`reviewer-b-measure.jsonl`).
- Problem: on Today the next-patient card and the first row repeat the same button.
- Consequence: the teal fill stops meaning "the next thing to do".
- Reproduce: open Today.
- Fix: one primary per view. On Today, remove the next patient from the list below, or make the row buttons outline. On the board, the primary style only for the first "Ready for doctor" card of the signed-in doctor.
- Depends on: frontend.
- Confidence: high.
- Validate: design owner.

**B-20 · Small targets against the project's own 44px rule**

- Where: switches (settings, notifications), select options, patient-name links on Today.
- Severity: **P3**. Observed.
- Evidence: `local-metrics.jsonl`: switch 32x18, select option 32px, name link 109x19.
- Problem: the switch is under 24x24.
- Consequence: mis-taps on a tablet. Under WCAG 2.5.8 Target Size (Minimum, AA) the switch passes only if the spacing exception holds. The rows are 69px apart, so it probably does, but I did not verify each case.
- Reproduce: open Settings · hours at 375px.
- Fix: wrap the switch in a 44x44 hit area (padding, not a larger track). Select options 44px on touch. Make the whole visit row the link.
- Depends on: frontend.
- Confidence: medium (the exception was not checked per instance).
- Validate: accessibility reviewer.

**B-21 · Buttons disabled from the start, some with no reason shown**

- Where: walk-in "Check in now", "Request merge review", the appointment panel.
- Severity: **P3**. Observed.
- Evidence: `local-stack/doctor/walk-in/1440-default.png`, `states/schedule-panel/1440-appointment-open.png`.
- Problem: 2.56:1 grey labels. Walk-in gives no reason. The appointment panel gives the same reason four times.
- Consequence: a button that looks broken.
- Reproduce: open walk-in.
- Fix: keep "Check in now" enabled and validate on press, or show "Choose a patient first" beside it. For a completed visit, hide the four actions and show one line: "This visit is completed."
- Depends on: frontend.
- Confidence: high.
- Validate: product owner. Optional design advice (disabled controls are exempt from contrast rules).

**B-22 · Card padding, radius and nesting are inconsistent**

- Where: whole app.
- Severity: **P3**. Observed.
- Evidence: paddings of 10x12, 12, 16 and 24px, radii of 12 and 16px; the consultation's right panel and the settings pages nest three bordered boxes.
- Problem: there is no rule for which card gets which padding.
- Consequence: uneven rhythm, and borders used where spacing would do.
- Reproduce: compare Settings · staff with Patient 360.
- Fix: two card sizes only: page section (16px padding, 12px radius) and list item (12px padding, 10px radius). No bordered card directly inside a bordered card. Use a divider or a heading.
- Depends on: frontend.
- Confidence: high.
- Validate: design owner.

**B-23 · The offline card covers board content and talks about recording**

- Where: `/front-desk`, offline.
- Severity: **P3**. Observed.
- Evidence: `states/error/1440-front-desk-offline.png`.
- Problem: a 420x224px card sits over the "Done" column. Its text is about recordings and notes.
- Consequence: confusing on the reception screen.
- Reproduce: open the board and go offline.
- Fix: a 40px strip under the top bar with wording for the screen ("Offline. The board shows the last update from 10:31. Actions are paused.").
- Depends on: frontend.
- Confidence: medium (one screen checked).
- Validate: product owner.

**B-24 · Small motion hygiene items**

- Where: `packages/ui`.
- Severity: **P3**. Observed in code and by measurement.
- Evidence: `switch.tsx:17`, `tabs.tsx:66` (`transition-all`); the focus outline colour measured mid-fade as white on a primary button, settling to `#2563EB`; `styles.css:14`.
- Problem and fix: see the table in section 5.
- Consequence: none a user will notice today.
- Depends on: frontend.
- Confidence: high.
- Validate: frontend developer.

**B-25 · Native kit: invisible button borders and low-contrast placeholders (code-only)**

- Where: `packages/ui-native/src/controls.tsx`.
- Severity: **P2**. Inferred from code.
- Evidence: `borderColor: theme.color.line` (`#DFE7ED`, 1.25:1); `placeholderTextColor={theme.color.ink400}` (3.05:1).
- Problem: the web's fix for control borders did not reach the native kit.
- Consequence: secondary and icon buttons with no visible edge. WCAG 1.4.11 Non-text Contrast applies to a control's boundary.
- Reproduce: not possible until Metro bundling is fixed.
- Fix: use `theme.color.input` for control borders and `ink600` for placeholders.
- Depends on: mobile frontend.
- Confidence: medium (not rendered).
- Validate: mobile developer on a device.

**B-26 · The platform console does not use the shared page header (code-only)**

- Where: `web/platform-admin/src/features/*/components/*Page.tsx`.
- Severity: **P3**. Inferred from code.
- Evidence: five hand-written `h1` elements; no `PageHeader` or `PageContainer` import.
- Problem: header fixes in `packages/ui` will not apply to the console.
- Consequence: slow drift between the two apps.
- Fix: adopt `PageContainer` and `PageHeader`.
- Depends on: frontend.
- Confidence: medium.
- Validate: frontend developer.

**B-27 · No PRODUCT.md or DESIGN.md, and the token doc describes the design file, not the build**

- Where: repository.
- Severity: **P3**. Observed.
- Evidence: neither file exists; `docs/02-design/tokens.md` is generated from the design files and still lists the old primary colour and a Material radius.
- Problem: there is no written rule for density, page headers, card sizes or how many primary buttons a view may have. That is why B-09, B-10, B-11 and B-22 happened.
- Consequence: each new screen invents its own composition.
- Fix: a short `DESIGN.md` written from the shipped build after the layout pass (the Impeccable `document` command does this).
- Depends on: design decision.
- Confidence: high.
- Validate: founder.

**B-28 · Right-to-left detail: the keyboard hint is scrambled**

- Where: top bar search, Arabic.
- Severity: **P3**. Observed.
- Evidence: `states/rtl-arabic/1440-front-desk.png` ("K / Ctrl K⌘").
- Problem: the Latin key hint is reordered by the bidirectional text algorithm.
- Fix: wrap the hint in an element with `dir="ltr"`.
- Depends on: frontend.
- Confidence: high.
- Validate: an Arabic reader. My Arabic review is provisional: I checked layout and mirroring, not the wording.

### Normative and advisory, kept apart

| Finding | WCAG 2.2 AA requirement? |
|---|---|
| B-01 | **Yes, at risk**: 2.4.11 Focus Not Obscured (Minimum). Two controls were entirely hidden when focused |
| B-20 | **Possibly**: 2.5.8 Target Size (Minimum). Depends on the spacing exception, not verified per control |
| B-25 | **Yes if confirmed on a device**: 1.4.11 Non-text Contrast, 1.4.3 Contrast (Minimum) |
| axe `page-has-heading-one` on 5 pages | Best practice in axe, not a WCAG failure by itself |
| B-13 | No. 3.3.1 is met. Moving focus to the first error is advice (Vercel guidelines, NHS error summary) |
| B-03 | No WCAG criterion. It is the project's own clinical-safety rule |
| All others | Design advice |

I make no claim of WCAG conformance. This was not a conformance audit.

## 9 · References

| Source | Opened? | Used for |
|---|---|---|
| Vercel Web Interface Guidelines, `https://vercel.com/design/guidelines` | Yes, full page | "Never `transition: all`"; honour reduced motion; animate `transform` and `opacity`; show-delay of about 150–300ms and minimum visible time of about 300–500ms for loading states; "Don't pre-disable submit"; on submit, focus the first error; redundant status cues; 44px minimum hit target on mobile; input font size at least 16px on mobile |
| WCAG 2.2, `https://www.w3.org/TR/WCAG22/` | Yes, but only partly read by the fetch agent. The criterion names and levels are from the page. **The wording I give for each criterion is a paraphrase and should be checked against the page before being quoted** | 1.4.1, 1.4.3, 1.4.4, 1.4.10, 1.4.11, 2.4.7, 2.4.11, 2.5.8, 3.3.1, 4.1.3 |
| NHS design system · typography | Yes | Default paragraph 19px on large screens, 16px on small |
| NHS design system · buttons | Yes | "Use only 1 primary button on a page." |
| NHS design system · error summary | Yes | Always show a summary on a validation error and move focus to it |
| NHS design system · layout | Yes | Lines of text no longer than 70 to 80 characters; default page width 960px |

The NHS system is written for public-facing services. I use its numbers as a reference point, not a rule for a staff tool.

## 10 · What is good and should be kept

1. **The token set and its tests.** Text and control-boundary contrast are tested, and the deviations from the design file are written down with reasons.
2. **Status is always icon plus words.** Chips, provenance tags, schedule states, alerts.
3. **The 44px control height**, almost everywhere.
4. **The focus ring**, including the white variant on navy.
5. **The clinic-specific components**: provenance tags, the dashed AI-draft tag, the recording pill, the nurse escalation chip, "Clinician verified".
6. **Error and offline copy** that says what happened and that nothing was lost.
7. **Privacy by design in the interface**: masked MRN and initials in search, a registration page that never shows clinical notes.
8. **Arabic and Hindi**: mirrored layout, logical properties, no overflow.
9. **The Today screen's structure**: next patient first.
10. **The `/app/__ui` gallery.** It is the nearest thing to a design system page, and it shows the intended banner that the consultation should use.
11. **Restraint with motion and decoration.** No gradients, no glass, no gratuitous animation. Do not let anyone "modernise" that away.
12. **The phone header**, which already moves Language and Sign out into "More".

## 11 · Direction and layout proposals

### My vote: B, cohesive modernisation, limited to layout, density and component consolidation

- **Not C (reset).** 615 pages with no overflow, 5 moderate axe hits, passing contrast, working right-to-left and a coherent token set are assets a reset would throw away. The visual identity (navy, teal, Manrope, calm white cards) is credible for a clinic.
- **Not A alone (surgical fixes).** The main problems are systemic: the same header stack on every page, five view switches, three patient headers, four card paddings. Fixing them screen by screen will let them drift apart again.
- **B, with a hard boundary.** Keep the colours, type family, components and copy. Change four things, in this order:
  1. the app shell,
  2. one page header and one segmented control in `packages/ui`,
  3. the reception board,
  4. the consultation workspace.
  Then write the rules down (`DESIGN.md`). Colour, branding and animation stay as they are.
- B-01, B-02 and B-03 are worth doing as surgical fixes before the pilot even if the rest waits.

### 11.1 · App shell

| Element | Today | Proposed |
|---|---|---|
| Top bar height | 60px | 48px from 768px up, 56px on phones |
| Top bar content | Facility (only on the rail), search, Language, My devices, Sign out, bell | Facility name, search (320 to 480px, centred or start-aligned), bell, avatar button (36px) that opens the account menu |
| Account menu | none | Name and role, Language, My devices, Change facility, Sign out (separated, last) |
| Sidebar, 1280px and wider | 196 to 232px | 208px fixed. Add a collapse control to the 72px rail, remembered per device, so the board can use the width |
| Rail, 768 to 1279px | 72px, icons only | 72px, icon (20px) with an 11px label under it, items 56px tall |
| Main padding | 24px | 20px top, 24px sides (16px on phones) |
| Page header | Label, 24px title, caption, then an action row: 100 to 180px | One row, 44px: title 20px/28px weight 700, secondary text after it (date), actions at the end. 12px gap below |
| Bottom tabs | 72px | 64px plus the safe-area inset |

Result: page content starts at 48 + 20 + 44 + 12 = **124px** on every page, instead of 164 to 240px.

### 11.2 · Reception board

At 1024px and wider:

```
y=0    top bar 48
y=68   header row 44:  Reception board · Tue 3 Nov   [By status|By doctor]   ● Live   [Waitlist] [Walk-in] [Register]
y=124  workload strip 32, one line: Dr Rao 5 waiting · on time   |   Dr Menon 1 waiting · on time   (tap for "By doctor")
y=164  column headers 36, sticky
y=200  first patient card
```

- First card at about **200px** instead of 452px. At 1024x768 that is 74 % of the screen for patients, against 41 % today.
- Columns: `grid-auto-columns: minmax(208px, 1fr)`. A column with no visits collapses to a 48px stub showing its icon and "0". The board region has a fixed height (`calc(100dvh - 164px)`), and each column scrolls on its own, so column headers stay visible.
- Card: 12px padding, 10px radius. Row 1: name at 14px weight 700 (may wrap to two lines, `min-width: 0`) and the time at 12px mono, `flex-shrink: 0`. Row 2: age and sex at 12px. Row 3: flags on one line, wrapping only if needed. Row 4: one action, full width, 44px, short label ("Call in", "Start", "Intake done").
- Primary fill only on the single next action for the signed-in user. Everything else is outline.
- "Receptionist view hides clinical notes" becomes an info button beside "Live", shown only to roles it applies to. "Facility time" moves into the date's tooltip, and is shown in the header only when the device's time zone differs from the facility's.
- Below 768px: one list, with a sticky row of status tabs and counts (44px), and Walk-in as a fixed 48px button above the tab bar.

### 11.3 · Consultation workspace

At 1280px and wider:

```
top bar 48
sticky patient banner 56 (one row): Meera Iyer · 54 F · MRN DQ-10-4482 · [Allergy: Penicillin] · Consent not recorded · Room 3
┌ section list 200 ─┬ note column, max 760 ───────────────┬ side panel 360, sticky ─────┐
│ 1 Vitals        ✓ │ 2 · History                         │ [Assist] [Chart] [Checklist]│
│ 2 History       ● │ Chief complaint (required)          │ …                           │
│ 3 Examination     │ [ text box, min-height 64 ]         │                             │
│ 4 Assessment      │ Subjective                          │                             │
│ 5 Plan            │ [ text box ]                        │ ─────────────────────────── │
│ ───────────────   │ …                                   │ 3 items to review           │
│ Prescription      │                                     │ [ Sign note ]               │
│ Orders            │                                     │ Saved 10:42                 │
│ Referrals         │                                     │                             │
└───────────────────┴─────────────────────────────────────┴─────────────────────────────┘
```

- Three columns: 200px section list (sticky, shows done and required marks), a note column capped at 760px, and a 360px side panel (sticky, holds the sign button and the save status at its foot). Gaps of 24px. At 1440px that is 200 + 24 + 760 + 24 + 360 = 1,368px inside the 1,208px content area if the sidebar is 208px, so at 1280 to 1535px the section list collapses into the sticky chip row and the layout is two columns (note, side panel). From 1536px all three fit.
- Prescription, orders and referrals become entries in the same section list. Selecting one shows it in the centre column in place of the note. Nothing is stacked below.
- "One page / Guided steps", "Problems and diagnoses", "Reconcile medicines" and "Back to the board" move into the banner's end as one overflow menu plus a back arrow.
- Empty sections (Vitals with nothing recorded) are one 44px line, not a card.
- First text box at about 48 + 56 + 16 + 40 = **160px** instead of 589px.

At 768 to 1279px: banner in two rows (72px), section chips sticky under it (44px), side panel as a drawer opened from a button, and a 56px sign row at the bottom with 72px of padding under the form so nothing is hidden (B-01).

Below 768px: banner compacts to 44px when stuck (name and the allergy pill), section chips 44px, sign row 56px above the 64px tab bar. Fixed chrome is then 56 + 44 + 44 + 56 + 64 = 264 of 812px (33 %), against about 350px (43 %) today, and the form is visible on arrival.

## 12 · Subscores

| Area | Score | Basis |
|---|---|---|
| Visual and design-system quality | **9 / 15** | Strong tokens, components, colour semantics and restraint (worth about 12). Loses points for 12 type sizes against a spec of 7, five view switches, three patient headers, four card paddings, seven title positions, and no written composition rules |
| Responsiveness and tablet usability | **5 / 10** | No page overflow anywhere and a sensible breakpoint model. But at 1024x768, the pilot's most likely tablet size, the working share of the first screen is 3 % on the consultation, 38 % on the schedule and 41 % on the board; board text spills out of cards; the phone board does not work; the rail has no labels. Emulated viewports only, so provisional |
| Accessibility | **7 / 10, provisional** | axe nearly clean on 615 pages, contrast passes, focus ring is correct, status never by colour alone, right-to-left works, reduced motion honoured, 200 % and 400 % reflow without sideways scrolling. Loses points for focus hidden behind the sticky sign bar, errors that do not take focus, an 18px-high switch, and sticky bars that take half the screen at 400 % zoom. No screen reader, no real devices and no conformance audit were done, so this is a floor estimate, not a compliance statement |

# Design system and screens

Visual direction (Foundations §01): **quiet luxury, clinically reassuring**. Deep midnight navy, Mediterranean teal, disciplined blue actions and restrained violet for AI. Surfaces are white and cool off-white. Red and amber are reserved for real clinical risk.

Four rules carry through every screen:

1. **Verified first.** Clinician-verified data is in solid ink. AI output is always violet and dashed, labelled "AI draft · not reviewed", and never shares the verified style.
2. **One patient, one banner.** Every clinical screen has a fixed identity banner: name, age, sex, MRN, DOB, allergies, consent and encounter date.
3. **Status is text + shape + colour.** Colour is never the only signal.
4. **Quiet surfaces.** White cards, 1px borders, almost no shadow.

| File | Content |
|---|---|
| [tokens.md](tokens.md) | Colour tokens (light/dark + contrast), module rails, type scale, spacing, radius/elevation/breakpoints, closed-loop states, status primitives |
| [components-theme-copy.md](components-theme-copy.md) | Component state tables (button, status chip, provenance tag, alert, text field, recording pill), Handoff MUI theme excerpt (reference only, see ADR-010), testable microcopy, WCAG 2.2 AA checklist, ERD additions |
| [sitemap-and-permissions.md](sitemap-and-permissions.md) | Role sitemap with routes, **role-permission matrix**, routes ↔ guards ↔ Figma frames |
| [screen-inventory-and-erd-coverage.md](screen-inventory-and-erd-coverage.md) | Every screen with route and status, ERD entity → screen coverage, capture / care-task / failure state machines |
| [screen-specs.md](screen-specs.md) | Behaviour rules per screen collected from the hi-fi designs (validation, states, guards, copy). Used as the acceptance-criteria source for tickets |
| [journeys.md](journeys.md) | J1–J6 end-to-end journeys (X-03) with approval gates and recovery branches |
| [responsive-and-dark.md](responsive-and-dark.md) | Mobile/tablet/desktop rules, dark mode, RTL |
| [data/](data) | Machine-readable Foundations and Handoff data extracted from the design files |

## Deviations from the design files (fix these in the design project)

| Where | Design value | Implemented | Why |
|---|---|---|---|
| Success text on the `#E6F4EF` tint ("Clinician verified" tag, "Recording consent · given", "Paid", "Eligible" chips) | `#087F5B` | `#07774F` | `#087F5B` on `#E6F4EF` is 4.42:1, below WCAG AA 4.5:1. Found by the contrast test in `packages/ui` (MDV-P0-020). `#087F5B` on white (5.2:1) is unchanged |
| Primary colour (`--color-primary`) | `#0E9384`, hover `#0F766E` | `#0F766E`, hover `#0B5C55` | White 14px bold text on `#0E9384` is 3.8:1, and `#0E9384` text on the canvas is 3.62:1. Both fail WCAG AA (the large-text exception needs ≥ 18.66px bold). Found by page-level axe on the UI gallery (MDV-P0-020). `#0F766E` is the design's own brand.700. `#0E9384` stays available for non-text accents |
| AI button | `#7155D8` fill | hover `#5B3FC4` (`ai-700` token) | Existing AI design colour; needed for a hover state |
| Focus ring | listed in the a11y checklist, not in the theme | global `:focus-visible` rule in `packages/ui/src/styles.css`: 2px `#2563EB`, offset 2 | Applies the Handoff rule to every interactive element |

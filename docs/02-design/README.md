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
| [components-theme-copy.md](components-theme-copy.md) | Component state tables (button, status chip, provenance tag, alert, text field, recording pill), **MUI theme (verbatim)**, testable microcopy, WCAG 2.2 AA checklist, ERD additions |
| [sitemap-and-permissions.md](sitemap-and-permissions.md) | Role sitemap with routes, **role-permission matrix**, routes ↔ guards ↔ Figma frames |
| [screen-inventory-and-erd-coverage.md](screen-inventory-and-erd-coverage.md) | Every screen with route and status, ERD entity → screen coverage, capture / care-task / failure state machines |
| [screen-specs.md](screen-specs.md) | Behaviour rules per screen collected from the hi-fi designs (validation, states, guards, copy). Used as the acceptance-criteria source for tickets |
| [journeys.md](journeys.md) | J1–J6 end-to-end journeys (X-03) with approval gates and recovery branches |
| [responsive-and-dark.md](responsive-and-dark.md) | Mobile/tablet/desktop rules, dark mode, RTL |
| [data/](data) | Machine-readable Foundations and Handoff data extracted from the design files |

# Rules for every AI assistant and human working on Medvya

**This file is the single source of truth for how code is written here.** `CLAUDE.md` imports it. If a rule here conflicts with a habit, the rule wins. If a rule looks wrong for a real case, say so in the PR and change this file in the same PR; never ignore it silently.

Medvya handles patient health data in India and the UAE. **Production quality is the only quality.** No prototypes, demo shortcuts or "fix later" code without a recorded `shortcut:` (see 2.6).

## 1 · Non-negotiable safety rules

1. **Synthetic data only.** Never put real patient data in code, tests, fixtures, logs, screenshots, CI output, issues or AI evaluation sets. Use the synthetic cast (`docs/00-overview/personas.md`). National IDs in fixtures must fail their checksum (`pnpm check:synthetic` enforces this).
2. **No PHI anywhere but the database and Blob.** Never in logs, telemetry, URLs, query strings, browser storage, push or SMS/WhatsApp/email previews, error messages or exception text. Telemetry is allowlist-scrubbed (`src/Host.Api/Observability`). Do not weaken it. The only exception is the mobile apps' encrypted on-device store, exactly as ADR-011 §3 limits it (what, where, how long). Nothing wider without a new ADR.
3. **Authorisation is server-side, on every endpoint:** tenant, facility, role/capability, care relationship, purpose. Hiding something in the UI is never the control. Every endpoint declares a policy or is explicitly public (test-enforced).
4. **Tenant isolation is two layers:** EF query filters **and** database row-level security on the tenant setting (PostgreSQL, ADR-012). Never bypass, disable or "temporarily" switch off either.
5. **Clinical safety:** AI output is a draft until a licensed clinician accepts each field and signs. Unknowns stay blank, never guessed. Signed records are never edited, only amended by addendum. Allergy and status are never conveyed by colour alone.
6. **Country data stays in country.** No cross-region resources, replication, AI inference or support access.
7. **Secrets** live only in Key Vault (cloud) or local user secrets. `.env.example` holds names, never values. Never commit a secret, even a test one that looks real.

## 2 · How to work

1. **Work from a ticket.** Every change references its key (`MDV-P1-013`) in the branch name, commit and PR. Read the ticket's acceptance criteria and the linked docs before coding. If the ticket is wrong or incomplete, update the ticket source (`docs/05-delivery/tickets/_source/*.tickets`, then `python3 -I tools/tickets/build.py`).
2. **Smallest complete change** (Ponytail discipline): solve the whole ticket with the least new code. Reuse what exists (house component, existing service, standard library, installed package) before adding anything. No speculative abstractions, options or "for later" code. Finish every part the change touches: callers, tests, docs, translations.
3. **Read before you write.** Find every caller, test and config your change reaches. Fix root causes in shared code, not symptoms at call sites.
4. **Tests prove behaviour.** New logic (branch, parser, money, security, consent, clinical rule) ships with a test. A safety-critical test must be shown to **fail when the protection is removed** (mutation check) before the PR. A test that cannot fail proves nothing.
5. **Verify for real.** UI changes: run the app and check in a browser (layout, keyboard, axe, Arabic RTL). Database changes: run against real PostgreSQL (Testcontainers). Never claim "done" from a passing build alone.
6. **Shortcuts are written down.** A known limit gets a code comment `shortcut: <the limit>, <when to upgrade>`. Untracked shortcuts are bugs.
7. **Never weaken a guard to get green.** Do not disable lint rules, analyzers, tests, audit or CI checks, or lower thresholds, to make a change pass. Fix the code. A justified, narrow suppression needs a comment with the reason (see `TelemetryRedactionTests` for the pattern).
8. **Comments explain why**, in one line, only where the code cannot.

## 3 · Back end (.NET 10, `src/`)

1. **Modular monolith.** A module (`src/Modules/<M>/{Domain,Application,Infrastructure,Api}`) never touches another module's tables or DbContext. Talk through application services, contracts or domain events.
2. **Every domain property carries `[Classification(DataClass.X)]`** (architecture test). National/insurance IDs are `NationalId` → application-level envelope encryption with a blind index (ADR-012).
3. Country rules live in country rule classes, never in scattered `if (country == …)`.
4. Money is integer minor units (`long`) plus ISO currency. Never `decimal`/`double` for stored money.
5. Time is `DateTimeOffset` in UTC. Display in the facility time zone.
6. EF Core migrations are forward-only and reviewed. Raw SQL is parameterised (`ExecuteSqlAsync($"…{value}")`), never string-built. **Any change to a table, column, key or foreign key regenerates the ERD in the same PR:** `python3 -I tools/erd/generate.py` rewrites `docs/03-architecture/erd.md`; CI fails when it is stale. A new cross-module link by id is added to `SOFT_REFS` in that script, and a newly built planned entity to `PLANNED_TO_BUILT`.
7. Logging uses message templates (`LoggerMessage` source generator in production code). Never log patient values; the scrubber is a safety net, not a licence.
8. Warnings are errors. NuGet versions live only in `Directory.Packages.props` (central management with transitive pinning).

## 4 · Front end (React 19 + TypeScript, `web/`): hard rule, ADR-009

```
src/app/                         composition only: providers, routing, controller → component wiring
src/features/<feature>/domain/   pure types and rules (no React, no I/O)
src/features/<feature>/services/ I/O only: API client, storage, browser APIs (no React)
src/features/<feature>/hooks/    controller hooks useXxxController(): TanStack Query, React Hook Form + Zod, state; return view model + handlers
src/features/<feature>/components/ presentational: props in, events out (no services, hooks, fetch, storage)
```

- Lint enforces the layers (`web/clinic/.oxlintrc.json`). Full guide: `docs/03-architecture/frontend-architecture.md`.
- Server state → TanStack Query. Forms → React Hook Form + Zod (schemas in `domain/`). Routing → React Router. No other state library without an ADR.
- UI is **shadcn/ui + Radix + Tailwind 4** (ADR-010) from `@medvya/ui`. Never add MUI or another component library. New shadcn components go into `packages/ui` via the shadcn CLI. Colours and spacing come from the tokens in `packages/ui/src/styles.css`. No ad-hoc colours: they must meet WCAG 2.2 AA (contrast is tested). Use Tailwind logical utilities (`ms-*`, `ps-*`, `start-*`) so right-to-left works.
- **Tables use the `Table` components from `@medvya/ui`** (shadcn). Never write raw `<table>`, `<tr>`, `<td>` or `<th>` in a feature. Sorting, filtering and paging state lives in a `useXxxTableController` hook (sort and paging: the shared `useTableController` in `web/clinic/src/features/table`, paged over 50 rows with `TablePagination`); column definitions are pure data in `domain/`; the component only renders rows it is given. A table that can be read on a phone is `stacked` (labelled blocks under 768 px, never a sideways scroll that hides a dose or identity column), and each `TableCell` gets its column `label`.
- **No hard-coded user-facing text** (`react/jsx-no-literals`). Every string is a key in `packages/i18n/messages/{en,hi,ar}.json`. Arabic must work right-to-left.
- Accessibility: keyboard path, visible focus ring, screen-reader labels, 44px targets (48px in the patient portal), 200% zoom, reduced motion.

**Design rules** ([DESIGN.md](DESIGN.md), [PRODUCT.md](PRODUCT.md)). New code follows them now. Where the existing code does not, the ticket in brackets makes it true; do not copy the old pattern.

- **44 px is the library default** for Input, Select, Textarea and buttons. A feature never patches a control height (`h-11`, `min-h-11`) (MDV-P1-077, MDV-P1-078).
- **Two densities.** Comfortable (44 px controls, 48 px rows) is the default and is mandatory for every control that changes state, every row that picks a patient, medicine lines, and any device with a coarse pointer (`any-pointer: coarse`). Compact 36 px rows (`<Table density="compact">`) are allowed only on four tables: the audit log, the formulary, order sets and the staff list, as an explicit per-table choice (MDV-P2-054). Lint enforces it (`medvya-ui/compact-table`; the allow-list is in `web/clinic/.oxlintrc.json`). A compact table ignores its density on any device that reports a coarse pointer (`any-pointer: coarse`): its rows are 48 px there. A destructive action inside a compact table (Offboard, Remove) stays 44 px or asks to confirm. Any other row under 44 px needs this section amended in the same change.
- **Typed clinical text is 16 px.** Body text is 14/20; no arbitrary font sizes (MDV-P1-079, MDV-P2-057).
- **1024 px is a named layout tier** for two-pane layouts. `sm:grid-cols-*` is not used for column changes (MDV-P1-079, MDV-P2-057).
- **Use the shared component once it exists; never build a local copy:** PatientBanner (MDV-P1-081); StickyActionBar and SaveStatus (MDV-P1-082); Chip, Tag and Banner (MDV-P1-083); AccountMenu, PageHeader and Tooltip (MDV-P1-084); Field and ErrorSummary (MDV-P1-085); Card, Section, PageMessage, EmptyState, SegmentedControl, SplitLayout and Sheet (MDV-P1-086). No raw `h1` in a feature once PageHeader exists. Every `Table` has a `label` (MDV-P1-077).
- **One primary (filled) button per view.**
- **Pinned chrome** (top bar, patient strip, section anchors, action footer) takes at most 30 % of the viewport height, and nothing focusable sits under it. The action footer is part of the layout, never an overlay (MDV-P1-082, MDV-P1-092).
- **Motion** uses the two tokens (ease-out `cubic-bezier(0.23, 1, 0.32, 1)`, 150 ms; MDV-P1-079) and named transition properties, never `transition-all` (MDV-P2-056). Never: page transitions, sliding cards between columns, staggered lists, animating the Ctrl+K search, anything that moves while a doctor types, animated counters, toasts over the patient strip.
- **A new shared component is not done until it is in the `/__ui` gallery in English and Arabic.**
- **Drift counts may only fall.** `pnpm check:ui-drift` (CI, web job) counts height patches, arbitrary font sizes, raw hex colours, `sm:grid-cols-*`, raw `h1`, `border bg-card` and local `Message`, `Field`, `Card` and `Empty` components against `tools/check-ui-drift/baseline.json`. A rise fails. When your change lowers a count, run `pnpm check:ui-drift --update` and commit the baseline in the same PR. Never raise a number by hand.

## 5 · Definition of Done (every PR)

- [ ] Ticket acceptance criteria met and demonstrated
- [ ] `dotnet build` (0 warnings) · `dotnet test` · `pnpm -r build` · `pnpm -r lint` · `pnpm -r test` · `pnpm e2e` · `pnpm check:synthetic` · `pnpm check:ui-drift` all pass locally
- [ ] New/changed behaviour tested; safety tests mutation-checked
- [ ] UI verified in a browser (English and Arabic), axe clean
- [ ] Cross-cutting X1–X8 (`docs/05-delivery/README.md`) considered: security, privacy, clinical safety, states (empty/loading/error/403/offline), a11y and language, performance, reliability, observability
- [ ] Docs, tickets and ADRs updated in the same PR when behaviour or decisions change
- [ ] Database change: `python3 -I tools/erd/generate.py` run and `docs/03-architecture/erd.md` committed
- [ ] CI green. **Never merge red CI.** Never bypass hooks (`--no-verify`) or force-push `main`

## 6 · Dependencies

1. Prefer what is already installed. A new dependency needs a reason in the PR (what it replaces, why not the standard library).
2. Pin through the lockfile / `Directory.Packages.props`. Keep majors current: Dependabot PRs are reviewed weekly. Major upgrades are done deliberately on one branch with a browser check.
3. `pnpm audit` (high/critical) and the NuGet vulnerability check must stay clean. Fix by upgrading or pinning the patched version, never by ignoring.
4. pnpm build scripts are allow-listed (`pnpm-workspace.yaml` `allowBuilds`); approve only what needs it.

## 7 · Git and PRs

- Branch: `feat/MDV-<phase>-<nnn>-<slug>` (or `chore/…`, `fix/…`). One ticket per PR where possible.
- Commit message: what and why. Include the ticket key.
- PR body: what changed, how it was verified (commands, tests, browser checks), what is deliberately not done and which ticket covers it.
- Merge only with green CI. `main` is protected.

## 8 · Where things are

| Need | Look in |
|---|---|
| What to build, acceptance criteria | `tickets/` (source: `docs/05-delivery/tickets/_source`) |
| Requirements, phases, exit gates | `docs/01-prd/` |
| Who the product is for, principles, device strategy | [`PRODUCT.md`](PRODUCT.md) |
| Design rules: layout, density, type, tone, components, motion | [`DESIGN.md`](DESIGN.md) |
| Screens, tokens, copy, permissions | `docs/02-design/`, `design/` (read-only imports) |
| Architecture, data model, classification | `docs/03-architecture/` |
| Security, compliance, threat model | `docs/04-security-compliance/`, `docs/compliance/` |
| Decisions | `docs/adr/` |
| End-to-end journeys J1–J6 (Playwright, steps read from `docs/02-design/journeys.md`) | `tests/e2e/`: turn a step green by passing its implementation to `journey('Jn', { step: … })` |

## 9 · Repository layout (ADR-013)

1. **Keep the folders:** `src/` (.NET), `web/`, `mobile/`, `packages/`, `tests/`, `infra/`, `tools/`, `docs/`. The tree and what is built versus planned are in the README ("Repository structure"); update it in the same PR when you add a folder.
2. **No empty scaffolding.** A planned module, app or package is created by the ticket that builds it.
3. **`Host.Api` is composition only** (`Program.cs`, auth/BFF, context, middleware, observability, public site, module registration). A feature endpoint lives in its module's Api project (`HostCompositionTests` enforces it).
4. **No package imports an app** (`web/*`, `mobile/*`); native code never imports `@medvya/ui` (lint enforces both). Tokens, i18n and the API client are shared; screens are not.
5. **The API client is generated** from Host.Api's OpenAPI document (`packages/api-client`); `bash tools/check-contract.sh` regenerates it and CI fails on drift.
6. **Tickets and design sources live under `docs/`** (`docs/05-delivery/tickets/`, `docs/02-design/source/`). Relative Markdown links must resolve (`python3 -I tools/check-links/check.py`).


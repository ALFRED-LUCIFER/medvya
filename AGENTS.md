# Rules for every AI assistant and human working on Medvya

**This file is the single source of truth for how code is written here.** `CLAUDE.md` imports it. If a rule looks wrong for a real case, change this file in the same PR; never ignore it silently.

Medvya handles patient health data in India and the UAE. **Production quality is the only quality.** No prototypes, demo shortcuts or "fix later" code without a recorded `shortcut:` (see 2.6).

## 1 · Non-negotiable safety rules

1. **Synthetic data only.** Never put real patient data in code, tests, fixtures, logs, screenshots, CI output, issues or AI evaluation sets. Use the synthetic cast (`docs/00-overview/personas.md`). National IDs in fixtures must fail their checksum (`pnpm check:synthetic` enforces this).
2. **No PHI anywhere but the database and Blob.** Never in logs, telemetry, URLs, query strings, browser storage, push or SMS/WhatsApp/email previews, error messages or exception text. Telemetry is allowlist-scrubbed (`src/Host.Api/Observability`). Do not weaken it. The AI service still scrubs by a key-name and pattern denylist until ADR-021's pilot condition is met; do not copy that pattern. The only exception is the mobile apps' encrypted on-device store, exactly as ADR-011 §3 limits it (what, where, how long). Nothing wider without a new ADR.
3. **Authorisation is server-side, on every endpoint:** tenant, facility, role/capability, care relationship, purpose. Hiding something in the UI is never the control. Every endpoint declares a policy or is explicitly public (test-enforced).
4. **Tenant isolation is two layers:** EF query filters **and** database row-level security on the tenant setting (PostgreSQL, ADR-012). Never bypass, disable or "temporarily" switch off either.
5. **Clinical safety:** AI output is a draft until a licensed clinician accepts each field and signs. Unknowns stay blank, never guessed. Signed records are never edited, only amended by addendum. Allergy and status are never conveyed by colour alone.
6. **Country data stays in country.** No cross-region resources, replication, AI inference or support access.
7. **Secrets** live only in Key Vault (cloud) or local user secrets. `.env.example` holds names, never values. Never commit a secret, even a test one that looks real.

## 2 · How to work

1. **Work from a ticket.** Every change references its key (`MDV-P1-013`) in the branch name, commit and PR. Read its acceptance criteria and linked docs first. To change what a ticket asks for, edit its source (`docs/05-delivery/tickets/_source/*.tickets`, then `python3 -I tools/tickets/build.py`). **Ticket status lives on GitHub:** the issue's `status:` label plus a comment (what started, what was delivered, what is pending), **and** its Project 1 board Status. Close an issue only when its acceptance criteria are met; tick chunk sub-issues the same way. Never hand-edit `.tickets` progress lines, Gherkin tags or the generated pages to record status, and never run `tools/tickets/gh_issues.py --refresh` (it overwrites issues from the source).
2. **Smallest complete change** (Ponytail discipline): solve the whole ticket with the least new code. Reuse before adding; no speculative abstractions or "for later" code. Finish every part the change touches: callers, tests, docs, translations.
3. **Read before you write.** Find every caller, test and config your change reaches. Fix root causes in shared code, not symptoms at call sites.
4. **Tests prove behaviour.** New logic (branch, parser, money, security, consent, clinical rule) ships with a test. A safety-critical test must be shown to **fail when the protection is removed** (mutation check) before the PR, recorded next to the test as `// SAFETY: mutation check: <what was removed> -> <test> failed`. A test that cannot fail proves nothing.
5. **Verify for real.** UI changes: run the app and check in a browser (layout, keyboard, axe, Arabic RTL). Database changes: run against real PostgreSQL (Testcontainers). Never claim "done" from a passing build alone.
6. **Shortcuts are written down.** A known limit gets a code comment `shortcut: <the limit>, <when to upgrade>`. Untracked shortcuts are bugs.
7. **Never weaken a guard to get green.** Do not disable lint rules, analyzers, tests, audit or CI checks, or lower thresholds, to make a change pass. Fix the code. A justified, narrow suppression needs a comment with the reason (see `TelemetryRedactionTests` for the pattern).
8. **Comments explain why**, in one line, only where the code cannot.
9. **Working practice.** Parallel worktrees get non-overlapping files; check `git branch --show-current` before committing. One agent at a time drives the Playwright browser. Never restart Colima or prune Docker on a shared machine. Run targeted tests locally: `dotnet test tests/Medvya.IntegrationTests --filter FullyQualifiedName~X -- xUnit.ParallelizeTestCollections=false`, `pnpm --filter e2e e2e <spec>`, `pnpm e2e:fullstack`, `cd src/AiService && uv run pytest`.

## 3 · Back end (.NET 10, `src/`)

1. **Modular monolith.** A module (`src/Modules/<M>/{Domain,Application,Infrastructure,Api}`) references another module only through its Application or Contracts project, never its Infrastructure, Api, tables or DbContext (architecture-test-enforced). Talk through application services, contracts or domain events. One exception: a one-time data copy inside a forward-only migration may read another module's tables, read-only, tenant by tenant under row-level security; the migration comment names it (first: Billing's `ServicesCatalogue`, MDV-P4-002). **The audit write is the last statement before commit**, never before I/O or before taking another lock, and no network I/O happens inside a transaction that holds the audit lock.
2. **Every domain property carries `[Classification(DataClass.X)]`** (architecture test). National/insurance IDs are `NationalId` → application-level envelope encryption with a blind index (ADR-012).
3. Country rules live in country rule classes, never in scattered `if (country == …)`.
4. Money is integer minor units (`long`) plus ISO currency. Never `decimal`/`double` for stored money.
5. Time is `DateTimeOffset` in UTC, read from `TimeProvider`. Display in the facility time zone.
6. EF Core migrations are forward-only and reviewed. Raw SQL is parameterised (`ExecuteSqlAsync($"…{value}")`), never string-built. **Any schema change regenerates the ERD in the same PR** (`python3 -I tools/erd/generate.py`; CI fails when stale); a new cross-module id link goes in its `SOFT_REFS`, a newly built planned entity in `PLANNED_TO_BUILT`.
7. Logging uses message templates (`LoggerMessage` source generator in production code). Never log patient values; the scrubber is a safety net, not a licence.
8. Warnings are errors. NuGet versions live only in `Directory.Packages.props` (central management with transitive pinning).
9. **Payment and other provider webhooks** are explicitly public endpoints that verify an HMAC over the raw body with a constant-time compare before parsing anything; are idempotent on the provider's event id; never trust an amount or tenant from the payload (look up the stored link); use per-clinic keys from Key Vault; never log the payload; and audit every payment state change.

## 3b · AI service (Python, `src/AiService`, ADR-021)

- Same checks as CI: `uv sync --frozen`, `uv run ruff check .`, `uv run ruff format --check .`, `uv run mypy` (strict), `uv run pytest` on real PostgreSQL 16; `pip-audit` clean. Run Python with `-I`.
- Called only by Host.Api (`AUTH_MODE=service`). Every route requires the auth dependency (test-enforced). Alembic migrations are forward-only. Datetimes are timezone-aware. No PHI or request bodies in logs. Model and speech endpoints are in-country only (allow-list).

## 4 · Front end (React 19 + TypeScript, `web/`): hard rule, ADR-009

```
src/app/                         composition only: providers, routing, controller → component wiring
src/features/<feature>/domain/   pure types and rules (no React, no I/O)
src/features/<feature>/services/ I/O only: API client, storage, browser APIs (no React)
src/features/<feature>/hooks/    controller hooks useXxxController(): TanStack Query, React Hook Form + Zod, state; return view model + handlers
src/features/<feature>/components/ presentational: props in, events out (no services, hooks, fetch, storage)
```

- Lint enforces the layers (`web/clinic/.oxlintrc.json`). Full guide: `docs/03-architecture/frontend-architecture.md`.
- Server state → TanStack Query. Forms → React Hook Form + Zod (schemas in `domain/`). Routing → React Router. No other state library without an ADR. Services call the shared HTTP client (`features/api/services/apiClient.ts`) and type responses from `@medvya/api-client`. The query cache is cleared on clinic switch and sign-out.
- An instant is formatted with an explicit facility time zone (`timeZone` is required), never the browser's.
- UI is **shadcn/ui + Radix + Tailwind 4** (ADR-010) from `@medvya/ui`. Never add MUI or another component library. New shadcn components go into `packages/ui` via the shadcn CLI. Colours and spacing come from the tokens in `packages/ui/src/styles.css`. No ad-hoc colours: they must meet WCAG 2.2 AA (contrast is tested). Use Tailwind logical utilities (`ms-*`, `ps-*`, `start-*`) so right-to-left works.
- **Tables use the `Table` components from `@medvya/ui`.** Never write raw `<table>`, `<tr>`, `<td>` or `<th>` in a feature. Sort, filter and paging state lives in a controller hook (the shared `useTableController`, `TablePagination` over 50 rows); columns are pure data in `domain/`. Every `Table` has a `label`. A table read on a phone is `stacked` (never a sideways scroll that hides a dose or identity column), each `TableCell` with its column `label`.
- **No hard-coded user-facing text** (`react/jsx-no-literals`). Every string is a key in `packages/i18n/messages/{en,hi,ar}.json`. Arabic must work right-to-left.
- **Screen layout standard, every app and every new screen** (DESIGN.md "Screen layout standard"): small labelled sidebar (Settings at its foot, no fold toggle) · top bar · content at the **full width** · **bottom bar**. Every button that acts on the screen (Save, Add, Continue, Back, Confirm, Cancel…) goes in `StickyActionBar`, never at the end of a long form; dialogs and drawers keep their own pinned footer.
- Accessibility: keyboard path, visible focus ring, screen-reader labels, targets per DESIGN.md Density (never under 24 px), 200% zoom, reduced motion.

**Design rules** ([DESIGN.md](DESIGN.md), [PRODUCT.md](PRODUCT.md)). New code follows them; where old code does not, do not copy it.

- **Use the shared component; never build a local copy:** PatientBanner, StickyActionBar, SaveStatus, Chip, Tag, Banner, AccountMenu, PageHeader, Tooltip, Field, ErrorSummary, Card, Section, PageMessage, EmptyState, SegmentedControl, SplitLayout, Sheet, DetailList, LanguageSwitcher (all `@medvya/ui`); form fields `TextField`, `SelectField`, `StepErrorSummary` (`web/clinic/src/features/forms`); dates `todayIn`, `dateIn`, `isDeviceTimeZone` (`@medvya/i18n`). No raw `h1` in a feature. **A new shared component is not done until it is in the `/__ui` gallery in English and Arabic.**
- **44 px is the library default** for Input, Select, Textarea and buttons. A feature never patches a control height (`h-11`, `min-h-11`).
- **Two densities.** Comfortable (44 px controls, 48 px rows) is the default and is mandatory for every control that changes state, every row that picks a patient, medicine lines, and any device with a coarse pointer (`any-pointer: coarse`). Compact 36 px rows (`<Table density="compact">`) are allowed only on four tables: the audit log, the formulary, order sets and the staff list (allow-list in `web/clinic/.oxlintrc.json`). Compact rows are 48 px on a coarse pointer; a destructive action in one stays 44 px or confirms. Any other row under 44 px amends this section.
- **Typed clinical text is 16 px.** Body text is 14/20; no arbitrary font sizes. **1024 px is a named layout tier** for two-pane layouts; `sm:grid-cols-*` is not used for column changes.
- **One primary (filled) button per view.**
- **Pinned chrome** (top bar, patient strip, section anchors, action footer) takes at most 30 % of the viewport height, and nothing focusable sits under it. The action footer is part of the layout, never an overlay.
- **Motion** uses the two tokens (ease-out `cubic-bezier(0.23, 1, 0.32, 1)`, 150 ms) and named transition properties, never `transition-all`. Never: page transitions, sliding cards between columns, staggered lists, animating the Ctrl+K search, anything that moves while a doctor types, animated counters, toasts over the patient strip.
- **Lint enforces these** (`medvya-ui/*` in `tools/oxlint-plugin`): `compact-table`, `no-height-patch`, `no-local-field`, `no-local-surface`, `no-inline-card`, `no-arbitrary-text-size`, `no-sm-columns`. **Drift counts may only fall:** `pnpm check:ui-drift` counts raw hex colours against `tools/check-ui-drift/baseline.json`; when your change lowers a count, run `pnpm check:ui-drift --update` and commit the baseline. Never raise a number by hand.

**Mobile apps (`mobile/`, `packages/ui-native`)**, ADR-011. Detail: [DESIGN.md "Native apps"](DESIGN.md#native-apps).

- UI comes only from `@medvya/ui-native`; every colour, radius, space and type size from `theme` in `@medvya/tokens` (one `tokens.json` feeds web and native). Zero raw hex: `pnpm check:ui-drift` fails one.
- The ADR-009 layers and `react/jsx-no-literals` hold, lint-enforced in `mobile/.oxlintrc.json`. Never import web UI or another app.
- Targets: 44 px in the clinic app, 48 px in the patient app (17 px minimum text); primary actions and inputs 52 px. Arabic right to left (`supportsRTL`).
- Screen readers get words, never glyphs; a disabled action shows its reason as text and uses token colours, not opacity; the primary action sits in `Screen`'s footer, above the keyboard.

## 5 · Definition of Done (every PR)

The only Definition of Done: the PR template and `docs/05-delivery/README.md` link here.


- [ ] Ticket acceptance criteria met and demonstrated
- [ ] Locally: build with 0 warnings and the tests your change affects (`dotnet build`, targeted `dotnet test`, `pnpm --filter <pkg> lint|test|build`, the touched e2e specs, `pnpm check:synthetic`, `pnpm check:ui-drift`); CI runs the full suite (`dotnet test`, `pnpm -r build|lint|test`, `pnpm e2e`)
- [ ] New/changed behaviour tested; safety tests mutation-checked (2.4)
- [ ] UI verified in a browser (English and Arabic), axe clean
- [ ] Cross-cutting X1–X8 (`docs/05-delivery/README.md`) considered: security, privacy, clinical safety, states (empty/loading/error/403/offline), a11y and language, performance, reliability, quality
- [ ] Docs, tickets, ADRs and translations updated in the same PR when behaviour or decisions change
- [ ] Database change: `python3 -I tools/erd/generate.py` run and `docs/03-architecture/erd.md` committed
- [ ] CI green. **Never merge red CI.** Never bypass hooks (`--no-verify`) or force-push `main`

## 6 · Dependencies

1. Prefer what is already installed. A new dependency needs a reason in the PR (what it replaces, why not the standard library).
2. Pin through the lockfile / `Directory.Packages.props`. Keep majors current: Dependabot PRs are reviewed weekly. Major upgrades are done deliberately on one branch with a browser check.
3. `pnpm audit` (high/critical), the NuGet vulnerability check and `pip-audit` must stay clean. Fix by upgrading or pinning the patched version, never by ignoring. One exception: when **no patched version exists**, the owner may approve ignoring that one advisory id, with the reason, proof it is not in a shipped bundle or reachable from user input, an expiry date, and a removal ticket, all next to the entry (first: picomatch, #1433).
4. pnpm build scripts are allow-listed (`pnpm-workspace.yaml` `allowBuilds`); approve only what needs it.

## 7 · Git and PRs

- Branch: `feat/MDV-<phase>-<nnn>-<slug>` (or `chore/…`, `fix/…`). One ticket per PR where possible. Commit message: what and why, with the ticket key.
- PR body follows `.github/pull_request_template.md`: what changed, how it was verified, what is not done and which ticket covers it.
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
| Environments, deploys, restore | `docs/runbooks/` |
| End-to-end journeys J1–J6 (Playwright, steps read from `docs/02-design/journeys.md`) | `tests/e2e/`: turn a step green by passing its implementation to `journey('Jn', { step: … })` |

## 9 · Repository layout (ADR-013)

1. **Keep the folders:** `src/` (.NET and `src/AiService`), `web/`, `mobile/`, `packages/`, `tests/`, `infra/`, `tools/`, `docs/`. The tree and what is built versus planned are in the README ("Repository structure"); update it in the same PR when you add a folder.
2. **No empty scaffolding.** A planned module, app or package is created by the ticket that builds it.
3. **`Host.Api` is composition only** (`Program.cs`, auth/BFF, context, middleware, observability, public site, module registration). A feature endpoint lives in its module's Api project (`HostCompositionTests` enforces it).
4. **No package imports an app** (`web/*`, `mobile/*`); native code never imports `@medvya/ui` (lint enforces both). Tokens, i18n and the API client are shared; screens are not.
5. **The API client is generated** from Host.Api's OpenAPI document (`packages/api-client`); `bash tools/check-contract.sh` regenerates it and CI fails on drift.
6. **Tickets and design sources live under `docs/`** (`docs/05-delivery/tickets/`, `docs/02-design/source/`). Relative Markdown links must resolve (`python3 -I tools/check-links/check.py`).

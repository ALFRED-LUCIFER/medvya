# Architecture decision records

Format: Context · Decision · Consequences · Status. One decision per file. Never edit an accepted ADR; supersede it with a new one.

| ADR | Title | Status | Due |
|---|---|---|---|
| [ADR-001](ADR-001-stack.md) | Stack: .NET 10 modular monolith + React + Azure SQL (UI per ADR-010) | **Proposed** (PRD v2 decided; record and accept in P0 wk 1) | P0 wk 1 |
| [ADR-002](ADR-002-launch-market.md) | Launch market | **Proposed**, decision needed | P0 wk 1 |
| [ADR-003](ADR-003-country-isolation.md) | Country isolation: separate data planes | Proposed | P0 |
| [ADR-004](ADR-004-bff-session.md) | BFF with HTTP-only cookies, no tokens in the browser | Proposed | P0 |
| [ADR-005](ADR-005-tenant-isolation-rls.md) | Tenant isolation: EF query filters + Azure SQL RLS | Proposed | P0 |
| [ADR-006](ADR-006-immutable-signed-records.md) | Immutable signed records, ledger tables, addenda | Proposed | P0/P2 |
| [ADR-011](ADR-011-mobile-apps.md) | Mobile: two Expo store apps, native token sign-in, what a phone may keep | **Accepted** (deferred: store accounts, https links) | P5 |
| [ADR-010](ADR-010-ui-shadcn-tailwind.md) | UI: shadcn/ui + Radix + Tailwind 4, replaces MUI | **Accepted** | Now |
| [ADR-014](ADR-014-platform-console.md) | Platform console: separate host and DB role, company sign-on plus our own WebAuthn key, reason on every action | Proposed (key model chosen by the founder) | P5 |
| [ADR-015](ADR-015-formulary-data-source.md) | Formulary data source: a small starter list with no licence risk, clinic additions, licensed registers later | Accepted for the pilot starter (registers pending a person) | P2 |
| [ADR-009](ADR-009-frontend-architecture.md) | Front-end: domain-driven features, controller hooks, presentational components (lint-enforced) | **Accepted** | Now |

The Software Architecture blueprint also lists ADR topics for speech providers, consent ledger and audio retention. They are ticketed in P3: speech and LLM providers as ADR-007 (MDV-P3-001), consent templates and receipts (MDV-P3-002), audio retention (MDV-P3-019).

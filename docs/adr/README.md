# Architecture decision records

Format: Context · Decision · Consequences · Status. One decision per file. Never edit an accepted ADR; supersede it with a new one.

| ADR | Title | Status | Due |
|---|---|---|---|
| [ADR-001](ADR-001-stack.md) | Stack: .NET 10 modular monolith + React/MUI + Azure SQL | **Proposed** (PRD v2 decided; record and accept in P0 wk 1) | P0 wk 1 |
| [ADR-002](ADR-002-launch-market.md) | Launch market | **Proposed**, decision needed | P0 wk 1 |
| [ADR-003](ADR-003-country-isolation.md) | Country isolation: separate data planes | Proposed | P0 |
| [ADR-004](ADR-004-bff-session.md) | BFF with HTTP-only cookies, no tokens in the browser | Proposed | P0 |
| [ADR-005](ADR-005-tenant-isolation-rls.md) | Tenant isolation: EF query filters + Azure SQL RLS | Proposed | P0 |
| [ADR-006](ADR-006-immutable-signed-records.md) | Immutable signed records, ledger tables, addenda | Proposed | P0/P2 |

The Software Architecture blueprint also lists ADR topics for speech providers, consent ledger and audio retention. They are ticketed in P3 (MDV-P3-0xx).

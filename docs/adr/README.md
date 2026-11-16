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
| [ADR-016](ADR-016-organisation-onboarding-and-approval.md) | Organisation onboarding and approval: Medvya approves Clinic and Hospital organisations, never individual doctors; owners attest their doctors | Decided 10 Nov 2026 (document lists and pre-approval mode proposed, legal review) | P1 |
| [ADR-017](ADR-017-diagnosis-code-set.md) | Diagnosis code set per country: a curated WHO ICD-10 starter list with the system and version stored on every coded problem; licensed sets (UAE ICD-10-CM) before claims | Accepted for the pilot starter (licensed sets pending a person) | P2 |
| [ADR-018](ADR-018-server-pdf-questpdf.md) | Server PDFs with QuestPDF (Community licence) and embedded Noto fonts, so Hindi, Tamil and Arabic print shaped; a missing glyph is refused | Accepted | P2 |
| [ADR-019](ADR-019-subscription-pricing.md) | Subscription pricing: Solo, Clinic per doctor and Hospital custom in INR and AED, 14-day trial, patients not counted, plans as versioned data in the console | Decided 6 Nov 2026 (prices) and 15 Nov 2026 (limits and defaults) | P5 |
| [ADR-020](ADR-020-patient-app-session.md) | Patient app session: a verified mobile number, the staff app's token rules on its own scheme, no clinic data until records are linked | **Accepted** 15 Nov 2026 | P5 |
| [ADR-021](ADR-021-ai-service.md) | AI runs in a separate Python service (FastAPI, `src/AiService`), called only by Medvya's API; Medvya stays the system of record | **Accepted** 16 Nov 2026 for development (pilot conditions listed) | P3 |
| [ADR-022](ADR-022-ai-communication-middleware.md) | AI communication middleware: one .NET conversation engine with channel adapters for WhatsApp, clinic chat and the support desk; AI turns in the AI service, tools run by Medvya | **Proposed** 16 Nov 2026, needs the owner's agreement | P4 |
| [ADR-023](ADR-023-india-payments-razorpay.md) | India payments with Razorpay: each clinic's own account (no aggregator), payment links, paid only after a verified webhook or fetch, clinic keys in a payments Key Vault; partner OAuth is the target | **Proposed** 16 Nov 2026 (account model decided by the owner) | P4 |
| [ADR-009](ADR-009-frontend-architecture.md) | Front-end: domain-driven features, controller hooks, presentational components (lint-enforced) | **Accepted** | Now |

The Software Architecture blueprint also lists ADR topics for speech providers, consent ledger and audio retention. They are ticketed in P3: speech and LLM providers as ADR-007 (MDV-P3-001), consent templates and receipts (MDV-P3-002), audio retention (MDV-P3-019).

# Architecture decision records

Format: Context · Decision · Consequences · Status. One decision per file. Never edit an accepted ADR; supersede it with a new one.

Status and title come from each file's own heading and **Status** line; regenerate this table when one changes.

| ADR | Title | Status |
|---|---|---|
| [ADR-001](ADR-001-stack.md) | Stack: .NET 10 modular monolith, React/MUI, Azure SQL | Accepted 12 Nov 2026 (decider: founder), as amended by ADR-012 |
| [ADR-002](ADR-002-launch-market.md) | Launch market | Accepted for India (founder, 2026-11-04): India first, Central India |
| [ADR-003](ADR-003-country-isolation.md) | Country isolation: separate data planes | Accepted 6 Nov 2026 (founder) |
| [ADR-004](ADR-004-bff-session.md) | BFF session: no tokens in the browser | Accepted 6 Nov 2026 (founder) |
| [ADR-005](ADR-005-tenant-isolation-rls.md) | Tenant isolation: EF Core filters + PostgreSQL row-level security | Accepted 6 Nov 2026 (founder) |
| [ADR-006](ADR-006-immutable-signed-records.md) | Immutable signed records and tamper evidence | Accepted 6 Nov 2026 (founder) |
| [ADR-009](ADR-009-frontend-architecture.md) | Front-end architecture: domain-driven features and controller hooks | Accepted (founder requirement, 21 Oct 2026) |
| [ADR-010](ADR-010-ui-shadcn-tailwind.md) | UI layer: shadcn/ui + Radix + Tailwind 4 (replaces MUI) | Accepted (founder decision, 21 Oct 2026) |
| [ADR-011](ADR-011-mobile-apps.md) | Mobile: two Expo store apps, native token sign-in, what a phone may keep | Accepted 7 Nov 2026 (founder) |
| [ADR-012](ADR-012-postgres.md) | Database: PostgreSQL with EF Core, application-level encryption of national IDs | Accepted, 6 Nov 2026 (decider: founder) |
| [ADR-013](ADR-013-repository-layout.md) | Repository layout: one monorepo, existing folder names kept | Accepted, 6 Nov 2026 (founder) |
| [ADR-014](ADR-014-platform-console.md) | Platform console: separate host, company sign-on plus our own WebAuthn key | Proposed 7 Nov 2026 (MDV-P5-040) |
| [ADR-015](ADR-015-formulary-data-source.md) | Formulary data source and licence per country | Accepted for the pilot starter list, 9 Nov 2026 |
| [ADR-016](ADR-016-organisation-onboarding-and-approval.md) | Organisation onboarding and approval: Medvya approves organisations, never individual doctors | Decided by the product owner on 10 Nov 2026 |
| [ADR-017](ADR-017-diagnosis-code-set.md) | Diagnosis code set and version per country | Accepted for the pilot starter list, 10 Nov 2026 |
| [ADR-018](ADR-018-server-pdf-questpdf.md) | Server PDFs with QuestPDF (Community licence) and embedded Noto fonts | Accepted, 11 Nov 2026 (owner decision) |
| [ADR-019](ADR-019-subscription-pricing.md) | Medvya subscription pricing: three plans, per-doctor Clinic price, 14-day trial, plans as versioned data | Decided |
| [ADR-020](ADR-020-patient-app-session.md) | Patient app session: a verified mobile number, no clinic data until records are linked | Accepted 15 Nov 2026 (owner-approved default) |
| [ADR-021](ADR-021-ai-service.md) | AI runs in a separate Python service (FastAPI), called only by Medvya's API | Accepted 16 Nov 2026 (owner decision) for development with synthetic data |
| [ADR-022](ADR-022-ai-communication-middleware.md) | AI communication middleware: one conversation engine for WhatsApp, clinic chat and the support desk | Proposed 16 Nov 2026 |
| [ADR-023](ADR-023-india-payments-razorpay.md) | India payments with Razorpay: each clinic's own account, payment links, paid only after Razorpay confirms | Proposed 16 Nov 2026 |
| [ADR-024](ADR-024-patient-data-requests-and-erasure.md) | Patient data requests and erasure: crypto-shredding, an ids-only ledger, retention jobs | Proposed 16 Nov 2026 |

ADR-007 and ADR-008 are reserved for MDV-P3-001 (speech and LLM providers) and MDV-P4-029 (reporting store). Consent templates and receipts are MDV-P3-002; audio retention is MDV-P3-019.

# Source conflicts and how they are resolved

The design project contains documents written at different times. **PRD v2 (20 Oct 2026) is authoritative.** It explicitly reconciles the others. This page lists every conflict found while building these docs, so nobody implements a superseded decision by accident.

| # | Topic | Conflicting sources | Resolution (authority) | Action |
|---|---|---|---|---|
| C-01 | Backend stack | Architecture v2: .NET modular monolith. Build Plan: Node.js + NestJS + Python AI worker. October plan: Next.js 16 + App Service | **.NET 10 (ASP.NET Core, C# 14) modular monolith** (PRD v2 §07) | ADR-001. Build Plan stack table is historical only |
| C-02 | Database | Architecture docs, Build Plan and Azure Architecture: PostgreSQL Flexible Server + Prisma/RLS policies | **Azure SQL Database** with EF Core 10, RLS security predicates, Always Encrypted, ledger tables (PRD v2 §07b) | Map ERD types: `jsonb` → `json`/`nvarchar(max)`; exclusion constraint → unique slot index; RLS policies → security predicates; money stays integer minor units. Ticket MDV-P0-016 |
| C-03 | AI worker language | Architecture docs and Build Plan: Python FastAPI worker | **.NET 10 worker** using Microsoft.Extensions.AI. **Python only for offline evaluation** (PRD v2 §07) | P3-07 tickets |
| C-04 | Hosting in early phases | Azure Architecture Phase 1 and Build Plan: AKS (or Container Apps). Software Architecture: Container Apps + Static Web Apps | **Azure App Service (Linux) per country** until P7. Container Apps/AKS only for P7 stamps (PRD v2 §07) | P0-02 Bicep targets App Service |
| C-05 | Jobs and messaging | Software Architecture: Postgres outbox → Storage Queue. Azure Architecture: Service Bus + Redis | **Hangfire on Azure SQL in P1**; **Azure Service Bus + .NET workers from P3**; Redis only on measured need | P1 reminders on Hangfire |
| C-06 | Identity | Software Architecture: "trusted OIDC provider" | **Microsoft Entra External ID** for staff and patients; Entra federation for hospital SSO; **BFF with HTTP-only cookies, no tokens in the browser** | P0-03 |
| C-07 | Repo layout | Build Plan: pnpm + Turborepo `apps/api` (NestJS). Software Architecture: `/apps/api/DoctorMate.*` plus Python services | **PRD v2 layout:** `Medvya.sln`, `src/Host.Api`, `src/Modules/*`, `src/Workers`, `src/BuildingBlocks`, `web/clinic`, `web/portal`, `tests`, `infra/bicep`, `docs` | See [../03-architecture/repository-structure.md](../03-architecture/repository-structure.md) |
| C-08 | Phase numbering | Design inventory uses M1–M4/Later. October plan uses Phase 1–3. Architecture v2 uses milestones 0–8 | **PRD v2 phases P0–P8** with releases R0–R8 | Tickets use P-codes only |
| C-09 | Screen ID clash | Foundations: P-09 is both "Doctor revenue & payout report" and "Receipt/tax invoice print"; P-10–P-14 are listed twice | **Receipts renumbered P-15**; P-09 = revenue & payout report (PRD v2 finding) | Update Foundations when designs are next edited |
| C-10 | Portal timing | Software Architecture: patient portal in "P1" (its own numbering) | **PRD v2 P4** (R-01–R-04) | — |
| C-11 | Audio retention wording | Build Plan: "Audio deleted after note sign-off". Trust centre / H-06: "30 days, then delete" | **Retention policy per tenant/country**, default 30 days for documentation purpose, purge on schedule (P3-07, P5-12, H-06) | Confirm with legal (DPIA) in P0-08 |
| C-12 | Azure Architecture DR/RPO | Azure Architecture P1: RPO 15 min / RTO 4 h. Pooled scale: RPO 5 min / RTO 1 h | **RPO ≤ 15 min, RTO ≤ 4 h from P5** (PRD v2 X7); stricter numbers only per enterprise contract | — |
| C-13 | Brand / working name | Uploads use "DoctorMate"; one Build Plan header shows "D" | **Medvya** (working name, trademark clearance pending) | Keep the name global and replaceable (i18n key) |
| C-14 | Data plane of the Build Plan security list | "Postgres row-level security", "pgvector later" | Azure SQL equivalents (RLS predicates). Vector search is out of scope until a need is proven | — |
| C-15 | UI library | PRD v1 / October plan: **shadcn/ui + Radix + Tailwind 4**, Lucide icons ("port MUI tokens to Tailwind in P0"). Handoff, UI/UX prompt, Architecture v2 and Build Plan: **MUI** | **MUI with the Medvya theme** (PRD v2 §07: "Designs and Handoff already target MUI, so no token conversion") | Implemented in `@medvya/ui` (MDV-P0-020). **Founder confirmation requested on 21 Oct 2026**; switching is cheap while only primitives and the gallery exist |

## Open questions found while building docs and tickets (not answered by any source)

| # | Question | Needed by | Proposed default |
|---|---|---|---|
| Q-01 | Launch market: India first or UAE? (ADR-002) | P0 wk 1 | India first (PRD default). UAE pack built in P5 |
| Q-02 | Does Hangfire's SQL storage share the clinical database or use a separate one? | P1 start | Separate `jobs` schema in the same database, with its own least-privilege user |
| Q-03 | SMS provider for India DLT and for the UAE | P1 wk 4 | Azure Communication Services where DLT is supported, else a DLT-registered aggregator behind an adapter |
| Q-04 | Formulary data source per country (licence) | P2 start | Curated seed list per partner clinic + national code lists; licence decision recorded in an ADR |
| Q-05 | QuestPDF licence tier | P2 | Community tier if revenue is under the threshold; re-check at P5 |
| Q-06 | Reporting store that keeps PHI out of analytics | Before P4-10 | Read replica with masked views first; warehouse later |
| Q-07 | Who is the named clinical safety reviewer (P2-08)? | P2 start | Clinical advisor from P0 |
| Q-08 | Year-end holiday impact on the P1 pilot exit (2 consecutive weeks) | P1 | Run the measured pilot weeks in January 2027 |

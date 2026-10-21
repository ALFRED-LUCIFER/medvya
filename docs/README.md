# Medvya documentation

Medvya is a clinic and doctor platform for India and the UAE. It covers front desk, EMR, AI-drafted notes under clinician sign-off, billing, care follow-up and a patient portal, with hospital integration later.

These docs were built from the Claude Design project **"DOQVI UI mockups and foundations"**. The raw design files are kept unchanged in [`../design/`](../design). Each document below names the design file it came from. Where two sources disagree, **PRD v2 (20 Oct 2026) wins**, and the conflict is recorded in [01-prd/source-conflicts.md](01-prd/source-conflicts.md).

> All patient data in the designs and in these docs is **synthetic**. Hosting on Azure does not make Medvya compliant by itself. Real patient data needs qualified legal and clinical review in each market first: P1 for demographics, P2 for clinical data, P3 for audio.

## Map

| Folder | What is inside |
|---|---|
| [00-overview](00-overview) | Product summary, problem, markets, principles, personas, glossary |
| [01-prd](01-prd) | PRD v2 phase by phase: roadmap and calendar, the full requirement catalogue, exit gates, cross-cutting NFRs, metrics, risks, open decisions, traceability, completeness check, source conflicts |
| [02-design](02-design) | Design system (tokens, type, status primitives, component states, Handoff theme reference, microcopy), screen inventory, role sitemap and permission matrix, journeys, responsive/dark rules, accessibility checklist |
| [03-architecture](03-architecture) | Tech stack, proposed repository structure, module boundaries, data model / ERD mapped to Azure SQL, state machines, API sketch, Azure deployment stages, integrations, AI pipeline |
| [04-security-compliance](04-security-compliance) | 12-layer database security, security by layer, data residency, consent model, retention, India/UAE regulatory notes |
| [05-delivery](05-delivery) | Release process, environments, feature flags, versioning, Definition of Ready/Done, ticket conventions and confidence scale |
| [06-gap-analysis](06-gap-analysis) | What a clinic would still miss (outside history, legacy migration, doctor/reception gaps, voice assistant), with priorities and phases |
| [adr](adr) | Architecture decision records (ADR-001 stack, ADR-002 launch market, and others proposed by the PRD) |

Delivery tickets live in [`../tickets/`](../tickets): Jira-style, one folder per phase. Each ticket has acceptance criteria, source traceability and a confidence level.

## Source files (design project)

| Design file | Role in these docs |
|---|---|
| `Medvya PRD v2.dc.html` | **Authoritative.** Phases P0–P8, requirement IDs, exit gates, stack, DB security, traceability |
| `Medvya PRD.dc.html` | PRD v1.0. Superseded by v2 and kept for history |
| `Medvya Foundations.dc.html` | Tokens, status primitives, role sitemap, permission matrix, screen inventory, ERD coverage, state machines |
| `Medvya Handoff.dc.html` | Component states, MUI theme, routes ↔ Figma frames, microcopy, a11y checklist, ERD additions |
| `Medvya Clinician App.dc.html` | Hi-fi clinician app: Today, Patient 360, consent → sign flow, Rx/orders, front desk, nurse, inbox, care, billing, devices, settings |
| `Medvya Clinical Screens.dc.html` | N-01, reconciliation, addendum, merge review, contact outcomes, report matching |
| `Medvya Patient Registration.dc.html` | P-02 India/UAE registration (quick and full), duplicate check, data map |
| `Medvya Patient Portal.dc.html` | R-01–R-04 portal, including Arabic RTL |
| `Medvya Access and Onboarding.dc.html` | F-01–F-07 landing, region, sign-in/MFA, onboarding, invite, trust centre, error states |
| `Medvya Enterprise Admin.dc.html` / `Enterprise Pages.dc.html` | H-01–H-10 hospital admin |
| `Medvya Mobile and Tablet.dc.html` | X-01 mobile and tablet, X-02 dark mode, responsive rules |
| `Medvya Journeys.dc.html` | J1–J6 end-to-end journeys (X-03) |
| `Medvya Wireframes.dc.html` | Lo-fi frames for every screen and system state |
| `Medvya Build Plan.dc.html` | 32-week plan and security baseline. **Its stack (NestJS/Postgres/AKS) is superseded by PRD v2** |
| `Medvya Azure Architecture.dc.html` | Azure stages 1–4, stamps, residency. **AKS/Postgres in its Phase 1 are superseded by PRD v2 (App Service + Azure SQL until P7)** |
| `Medvya Gap Analysis.dc.html` | Gap items G-01–G-07 with Must/Should/Later |
| `uploads/DoctorMate_Architecture_v2_PMS_EMR_AI_C4.md` | Architecture v2 (C4, blocks A–F, ERD) |
| `uploads/DoctorMate_Software_Architecture_Azure_India_UAE_2026 (1).md` | Software architecture, repo, ERD, security by layer, FSMs |
| `uploads/DoctorMate_Claude_Complete_UIUX_Wireframe_Prompt.md` | UI/UX master prompt: tokens, inventory, safety rules, 13 acceptance tests |

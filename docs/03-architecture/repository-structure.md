# Proposed repository structure

**Basis:** the PRD v2 §07 layout (`Medvya.sln`, `src/Host.Api`, `src/Modules/*`, `src/Workers`, `src/BuildingBlocks`, `web/clinic`, `web/portal`, `tests`, `infra/bicep`, `docs`). This page expands it with folder conventions from the Software Architecture blueprint §5, translated from its PostgreSQL/Python assumptions to the PRD v2 stack (.NET 10, Azure SQL, .NET AI worker).

Status: **proposed**. It is created by ticket MDV-P0-001 and recorded in ADR-001.

```text
medvya/
├── README.md                       # what Medvya is, how to run locally, links to docs/
├── AGENTS.md                       # rules for AI coding assistants: synthetic data only, no PHI in logs, module boundaries
├── SECURITY.md                     # vulnerability disclosure, code-review rules (P0-10, P5-11)
├── CODEOWNERS
├── .editorconfig · .gitignore · .gitattributes
├── .env.example                    # variable NAMES only; real values come from Key Vault
├── global.json                     # pins the .NET 10 SDK
├── Directory.Build.props           # nullable, warnings-as-errors, analyzers, deterministic builds
├── Directory.Packages.props        # central NuGet versions
├── Medvya.sln
├── package.json · pnpm-workspace.yaml   # web workspaces only (web/*, packages/*)
├── docker-compose.yml              # local: SQL Server 2022, Azurite, MailHog/smtp4dev, fake SMS, seeded synthetic tenant
│
├── src/
│   ├── Host.Api/                   # ONE deployable: ASP.NET Core host
│   │   ├── Program.cs              # composition root: modules register themselves
│   │   ├── Bff/                    # cookie session, antiforgery, Entra External ID OIDC, token cache (server-side only)
│   │   ├── Middleware/             # CorrelationId, TenantScope (SESSION_CONTEXT), ExceptionBoundary, RateLimiting, SecurityHeaders
│   │   ├── PublicSite/             # Razor Pages: landing, region, trust centre, doctor profiles (no PHI)
│   │   ├── OpenApi/                # OpenAPI document → generates the typed TS client
│   │   └── appsettings*.json       # no secrets
│   │
│   ├── BuildingBlocks/
│   │   ├── Medvya.BuildingBlocks.Domain/        # Entity, ValueObject, strongly-typed IDs, Money (minor units), Result
│   │   ├── Medvya.BuildingBlocks.Application/   # mediator pipeline: validation (FluentValidation), authorisation, audit, unit of work
│   │   ├── Medvya.BuildingBlocks.Infrastructure/# EF Core base context, tenant query filter, outbox, ledger audit writer, Blob, Key Vault
│   │   ├── Medvya.BuildingBlocks.Security/      # policies (tenant, facility, role, relationship, purpose), data classification attributes, PHI redaction
│   │   ├── Medvya.BuildingBlocks.Country/       # country rules: IN / AE (IDs, address, tax, numbering, retention) — no country if-statements elsewhere
│   │   └── Medvya.BuildingBlocks.Contracts/     # integration events (PatientRegistered.v1 …), shared DTO primitives
│   │
│   ├── Modules/                    # each module = 4 projects; no module touches another module's tables
│   │   ├── Identity/               # users, sessions, MFA/passkeys, invitations, staff lifecycle            (P0, P1)
│   │   ├── Tenancy/                # organisation, facility, membership, roles/permissions, feature flags  (P0, P1)
│   │   ├── Audit/                  # ledger-backed audit events, access log queries, break-glass           (P0, P1, P7)
│   │   ├── Practice/               # practice profile, locations, hours, holidays, doctors/licences, services & prices, formulary (P1, P2, P4)
│   │   ├── Patients/               # registration, identifiers (Always Encrypted), relationships, consents, dedup/merge (P1)
│   │   ├── Scheduling/             # schedules, slots, appointments, waitlist, queue/board, tokens         (P1)
│   │   ├── Notifications/          # email/SMS/WhatsApp templates, attempts, delivery status, notification centre (P1, P4)
│   │   ├── Encounters/             # encounter, vitals/observations, problems, allergies, meds, notes, signing, addenda (P2)
│   │   ├── Prescribing/            # Rx, dose builder, safety checks, orders, referrals, print/e-Rx          (P2)
│   │   ├── Documents/              # blob documents, malware scan, secure viewer, extraction review         (P2, P5)
│   │   ├── Scribe/                 # recording consent, sessions, chunks, transcripts, speakers, drafts, evidence (P3)
│   │   ├── Billing/                # invoices, payments, receipts/numbering, packages, dues, day close, reports (P4)
│   │   ├── Care/                   # results inbox, matching, acknowledgement, care plans/tasks, contacts, messages (P4)
│   │   ├── Portal/                 # portal access, released content, proxies, patient booking               (P4)
│   │   ├── Insurance/              # coverage, eligibility, pre-auth, claims, remittance (adapters per payer) (P5)
│   │   ├── Migration/              # legacy connectors, mapping approval, dry-run report, CDC, data quality  (P5)
│   │   ├── Platform/               # tenant catalogue, subscriptions, support access, status                (P5)
│   │   ├── Devices/                # clip pairing, health, firmware, revoke                                  (P6)
│   │   └── Integrations/           # FHIR/HL7, HIE, write-back queue, record authority                       (P5, P7)
│   │       # each module folder:
│   │       #   Medvya.<M>.Domain/          aggregates, invariants, domain events
│   │       #   Medvya.<M>.Application/     use cases (commands/queries), policies.cs, validators
│   │       #   Medvya.<M>.Infrastructure/  EF configuration (own schema), repositories, country/{in,ae}.cs
│   │       #   Medvya.<M>.Api/             minimal-API endpoint groups + OpenAPI metadata
│   │
│   └── Workers/
│       ├── Worker.Jobs/            # Hangfire server (P1): reminders, retention purges, digests, statements
│       ├── Worker.Ai/              # P3: Service Bus consumer, Microsoft.Extensions.AI, Azure AI Speech/OpenAI; NO DB credentials
│       └── Worker.Integration/     # P5/P7: HL7 v2 / FHIR adapters, HIE push/pull, write-back with retry/dead-letter
│
├── web/
│   ├── clinic/                     # staff SPA: React 19 + TS + Vite + shadcn/ui + Tailwind 4 (ADR-010), TanStack Query, RHF + Zod, React Router
│   │   └── src/
│   │       ├── app/                # composition: providers, router, controllers → components
│   │       ├── features/<feature>/{domain,services,hooks,components}/   # hard rule, see frontend-architecture.md (ADR-009)
│   │       └── dev/                # UI gallery and other developer-only pages
│   └── portal/                     # patient PWA (P4): same stack, 17px min type, 48px targets, RTL
│
├── packages/                       # shared TS packages (pnpm workspaces)
│   ├── ui/                         # design system: shadcn/ui components, Tailwind tokens, clinical primitives (IdentityBanner, StatusChip, ProvenanceTag, RecordingPill, Alert)
│   ├── api-client/                 # generated from Host.Api OpenAPI (never hand-written)
│   ├── i18n/                       # en · hi · ar strings (+ ta, ml for portal/AI later); microcopy keys from Handoff
│   └── test-fixtures/              # synthetic patients (Meera Iyer, Deepak Nair, Fatima Al Mansoori …)
│
├── tests/
│   ├── Medvya.ArchitectureTests/   # NetArchTest: module boundaries, no cross-module EF access, Domain has no infra refs
│   ├── Medvya.<Module>.UnitTests/
│   ├── Medvya.IntegrationTests/    # Testcontainers (SQL Server): RLS cross-tenant, Always Encrypted, concurrency (double booking)
│   ├── Medvya.SecurityTests/       # IDOR, authorisation matrix, audit completeness, injection, rate limits
│   ├── e2e/                        # Playwright: journeys J1–J6, a11y (axe), RTL, print
│   └── ai-evals/                   # P3: de-identified evaluation set + Python offline evaluation scripts (only Python in repo)
│
├── infra/
│   ├── bicep/
│   │   ├── main.bicep
│   │   ├── modules/{network,appservice,sql,storage,keyvault,monitoring,defender,servicebus,acs,budgets}.bicep
│   │   ├── envs/{dev,staging,prod}.{in,ae}.bicepparam
│   │   └── policies/{deny-cross-region,require-private-endpoints,tagging}.json
│   ├── sql/                        # RLS predicates, Always Encrypted key provisioning scripts, ledger digest verification
│   └── scripts/{bootstrap,restore-drill,rotate-keys,seed-synthetic}.sh
│
├── .github/
│   ├── workflows/{pr.yml,deploy-staging.yml,deploy-prod.yml,security-scan.yml,nightly.yml}
│   ├── dependabot.yml
│   └── pull_request_template.md    # checklist: X1–X8, data classification, migration review, synthetic data only
│
├── design/                         # imported Claude Design files (read-only)
├── docs/                           # this documentation (adr/, compliance/, runbooks/ grow here)
├── tickets/                        # phase-by-phase delivery tickets
└── tools/                          # prd-extract, tickets build, synthetic-data generator
```

## Repository rules (Build Plan B-04, adapted to PRD v2)

1. Modules talk through application services, contracts or domain events, **never through another module's tables**. Architecture tests enforce this.
2. Country logic lives in `BuildingBlocks.Country` / `country/{in,ae}.cs`. No `if (country == …)` scattered through the code.
3. Every PR runs: build, .NET analyzers, `tsc`, ESLint, unit and integration tests (Testcontainers), CodeQL, dependency and secret scanning.
4. `main` is protected. Deploys happen only from tagged releases.
5. No secrets in the repo. Key Vault + managed identity only. GitHub OIDC to Azure.
6. Each architecture choice gets a short ADR in `docs/adr`.
7. **Synthetic data only** in code, tests, CI logs, fixtures and AI evaluation sets.
8. Database migrations are EF Core migration bundles: forward-only and reviewed.

## Why not the other layouts in the sources

- **Build Plan** `apps/api` (NestJS) + `apps/ai-worker` (Python) + Prisma `db/`: superseded by the PRD v2 stack decision (ADR-001).
- **Software Architecture** `/apps/api/DoctorMate.*` + `/services/ai-worker` (Python) + `/firmware`: the module list is reused. Firmware stays out of this repository until P6 (a separate repo with a signed release channel is recommended).

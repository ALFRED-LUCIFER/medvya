# Medvya

Medvya is a clinic and doctor platform for India and the UAE: front desk, EMR, AI-drafted notes under clinician sign-off, billing, care follow-up and a patient portal, with hospital integration later.

This repository holds the product knowledge base, the delivery plan and the application code (from ticket [MDV-P0-001](docs/05-delivery/tickets/P0-foundation/MDV-P0-001.md)). Read [AGENTS.md](AGENTS.md), [SECURITY.md](SECURITY.md) and the [secure development policy](docs/compliance/secure-development.md) before changing code.

## Run locally

Prerequisites: .NET SDK 10 (pinned in `global.json`), Node.js 22+, pnpm 11, and Docker with Compose.

```bash
pnpm services:up                              # PostgreSQL 16 :5432, Azurite blob :10000, email sink UI http://localhost:5025
dotnet build Medvya.sln                       # API + modules, warnings are errors
dotnet run --project src/Host.Api             # API, health check at /health
pnpm install && pnpm build                    # web workspaces (web/*, packages/*)
pnpm dev                                      # clinic app on http://localhost:5173
dotnet test Medvya.sln                        # includes tenant-isolation tests on a throwaway PostgreSQL (Testcontainers)
```

One command starts the services, applies the EF Core migrations of every module the app runs (Tenancy, Audit, Identity, Leads, Patients, Scheduling) and seeds the synthetic cast (MDV-P0-027): the India tenant "Iyer Family Clinic (synthetic)", facility Adyar, with one membership per role. Add `--uae` for "Al Noor Family Clinic (synthetic)". Re-running is safe (fixed ids, existing rows are kept). The seeder refuses any environment other than Development, Staging or Test.

```bash
pnpm dev:db                                   # or: pnpm dev:db --uae
ConnectionStrings__Medvya="Host=localhost;Port=5432;Database=medvya;Username=medvya;Password=Local_dev_only_1" \
  dotnet run --project src/Host.Api           # API on http://localhost:5080, the port the clinic proxy and dev sign-in expect
```

Sign in at http://localhost:5173 (with `pnpm dev` and the API running): **Sign in** opens the development sign-in page, a minimal OIDC provider inside the API that lists the seeded synthetic users and patients. It exists only when `ASPNETCORE_ENVIRONMENT=Development`; every other environment uses Entra External ID over https (ADR-004). The cast is in [tools/synthetic-data-generator/SyntheticSeed.cs](tools/synthetic-data-generator/SyntheticSeed.cs). The public site runs on the API itself: http://localhost:5080/ (also `/hi`, `/ar`).

Point Testcontainers at your Docker socket first:

```bash
# Colima
export DOCKER_HOST="unix://$HOME/.colima/default/docker.sock" TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock
# Docker Desktop
export DOCKER_HOST="unix://$HOME/.docker/run/docker.sock" TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock
```

## Repository structure

Decided in [ADR-013](docs/adr/ADR-013-repository-layout.md), built by ticket [MDV-P0-040](docs/05-delivery/tickets/P0-foundation/MDV-P0-040.md). One monorepo for backend, web, mobile, shared packages, infrastructure and docs. A folder marked *planned* is created by the ticket that builds it, never before.

```text
medvya/
├── AGENTS.md · CLAUDE.md · README.md · SECURITY.md
├── Medvya.sln · global.json · Directory.Build.props · Directory.Packages.props
├── package.json · pnpm-workspace.yaml     # web/*, mobile/*, packages/*
├── docker-compose.yml                     # PostgreSQL, Azurite, email sink (MDV-P0-032)
├── src/
│   ├── Host.Api/            # composition only: Program, auth/BFF, context, middleware, observability, public site
│   │                        #   today also holds feature endpoints (moved to modules by P0-040)
│   ├── BuildingBlocks/      # Domain, Application, Infrastructure, Security, Contracts, Country
│   ├── Modules/             # exist: Identity, Tenancy, Audit, Patients, Scheduling, Billing, Notifications, Leads
│   │                        # planned: Practice, Encounters, Prescribing, Documents, Scribe, Care, Portal,
│   │                        #   Insurance, Inventory, Laboratory, Migration, Platform, Devices, Integrations
│   └── Workers/             # planned: Worker.Jobs, Worker.Ai, Worker.Integration (P3+; jobs run in Host today)
├── web/
│   ├── clinic/              # exists: clinic app with clinic-admin routes
│   ├── platform-admin/      # planned (P5-040): Medvya team console
│   └── portal/              # planned (P4): patient PWA
├── mobile/
│   ├── clinician/           # planned (P5-034/035): Expo staff app
│   └── patient/             # planned (P5-036/037): Expo patient app
├── packages/
│   ├── ui/ · i18n/          # exist (ui is web only)
│   └── tokens · api-client · domain · auth-client · ui-native · config · test-fixtures   # planned
├── tests/
│   ├── Medvya.ArchitectureTests/ · Medvya.IntegrationTests/ · e2e/     # exist
│   └── Medvya.SecurityTests · Medvya.ContractTests · mobile-e2e · ai-evals   # planned
├── infra/
│   ├── bicep/               # exists; PostgreSQL replaces Azure SQL (MDV-P0-038)
│   └── postgres · monitoring     # planned (roles and RLS policies, alerts); runbooks stay in docs/runbooks
├── tools/                   # tickets, prd-extract, erd, synthetic data, db scripts
├── docs/                    # 01-prd · 02-design · 03-architecture · 04-security-compliance · 05-delivery · adr · compliance · runbooks
│   ├── 02-design/source/    #   imported design files
│   └── 05-delivery/tickets/ #   ticket sources, generated pages, schedule
└── .github/                 # workflows with path filters, CODEOWNERS, dependabot
```

| Folder today | Status |
|---|---|
| [docs/](docs/README.md) | Matches the target; holds the tickets (`docs/05-delivery/tickets/`) and the design sources (`docs/02-design/source/`) |
| [src/](src) | Matches; Host.Api feature endpoints move into modules (P0-040 chunk 1) |
| [web/](web) · [packages/](packages) | Match; `tokens` and `api-client` added (P0-040 chunk 2) |
| [tests/](tests) · [infra/](infra) · [tools/](tools) | Match; `infra/sql` is replaced by PostgreSQL scripts (MDV-P0-034, MDV-P0-038) |

The run commands above are for PostgreSQL (MDV-P0-032); the Azure deployment still targets Azure SQL until MDV-P0-038.

Regenerate after editing sources:

```bash
python3 -I tools/prd-extract/render_docs.py      # docs/01-prd from PRD v2 data
python3 -I tools/prd-extract/render_design.py    # docs/02-design from Foundations/Handoff data
python3 -I tools/tickets/build.py                # docs/05-delivery/tickets/* + coverage check (exit 1 on any gap)
```

All patient data in this repository is **synthetic**.

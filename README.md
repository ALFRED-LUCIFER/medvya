# Medvya

Medvya is a clinic and doctor platform for India and the UAE: front desk, EMR, AI-drafted notes under clinician sign-off, billing, care follow-up and a patient portal, with hospital integration later.

This repository holds the product knowledge base, the delivery plan and the application code (from ticket [MDV-P0-001](tickets/P0-foundation/MDV-P0-001.md)). Read [AGENTS.md](AGENTS.md), [SECURITY.md](SECURITY.md) and the [secure development policy](docs/compliance/secure-development.md) before changing code.

## Run locally

Prerequisites: .NET SDK 10 (pinned in `global.json`), Node.js 22+, pnpm 11, and Docker with Compose. On Apple silicon, SQL Server needs amd64 emulation through Rosetta (Colima: `colima start --vz-rosetta --memory 4`).

```bash
pnpm services:up                              # SQL Server :1433, Azurite blob :10000, email sink UI http://localhost:5025
dotnet build Medvya.sln                       # API + modules, warnings are errors
dotnet run --project src/Host.Api             # API, health check at /health
pnpm install && pnpm build                    # web workspaces (web/*, packages/*)
pnpm dev                                      # clinic app on http://localhost:5173
dotnet test Medvya.sln                        # includes tenant-isolation tests on a throwaway SQL Server (Testcontainers)
```

One command starts the services, applies every module's EF Core migrations (Tenancy, Audit) and seeds the synthetic cast (MDV-P0-027): the India tenant "Iyer Family Clinic (synthetic)", facility Adyar, with one membership per role. Add `--uae` for "Al Noor Family Clinic (synthetic)". Re-running is safe (fixed ids, existing rows are kept). The seeder refuses any environment other than Development, Staging or Test.

```bash
pnpm dev:db                                   # or: pnpm dev:db --uae
ConnectionStrings__Medvya="Server=localhost,1433;Database=medvya;User Id=sa;Password=Local_dev_only_1;TrustServerCertificate=True" \
  dotnet run --project src/Host.Api           # API against the seeded database
```

Seeded users cannot sign in yet: sign-in arrives with MDV-P0-011, and patient records with the patient registration ticket. The cast is in [tools/synthetic-data-generator/SyntheticSeed.cs](tools/synthetic-data-generator/SyntheticSeed.cs).

With Colima, point Testcontainers at its socket first:

```bash
export DOCKER_HOST="unix://$HOME/.colima/default/docker.sock" TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock
```

| Folder | Content |
|---|---|
| [docs/](docs/README.md) | Everything from the design project and PRD v2: product, requirements, design system, architecture, security/compliance, delivery rules, gap analysis, ADRs |
| [tickets/](tickets/README.md) | 223 Jira-style tickets for phases P0–P8, each with acceptance criteria, traceability, dates and a confidence level; date-wise [schedule](tickets/schedule.md); [Jira CSV](tickets/jira-import.csv); [coverage report](tickets/coverage-report.md) |
| [src/](src) | .NET 10 modular monolith: `Host.Api` and modules (`Identity`, `Tenancy`, `Audit`), each split into Domain / Application / Infrastructure / Api |
| [web/](web) · [packages/](packages) | React 19 + Vite clinic app; `@medvya/ui` (shadcn/ui + Tailwind 4 design system, ADR-010) |
| [design/](design/README.md) | Imported Claude Design source files (read-only) |
| [tools/](tools) | `prd-extract` (design data → docs), `tickets/build.py` (ticket sources → files + coverage check), `synthetic-data-generator` (migrate + seed, `pnpm dev:db`), `check-synthetic` |

Regenerate after editing sources:

```bash
python3 -I tools/prd-extract/render_docs.py      # docs/01-prd from PRD v2 data
python3 -I tools/prd-extract/render_design.py    # docs/02-design from Foundations/Handoff data
python3 -I tools/tickets/build.py                # tickets/* + coverage check (exit 1 on any gap)
```

All patient data in this repository is **synthetic**.

# ADR-021 · AI runs in a separate Python service (FastAPI), called only by Medvya's API

- **Status:** Accepted 16 Nov 2026 (owner decision) for development with synthetic data. The conditions under "Before real patient data" must be met before any pilot use. Ticket: MDV-P3-028.
- **Supersedes:** the "AI" row of [ADR-001](ADR-001-stack.md) (".NET worker with Microsoft.Extensions.AI … Python only for offline evaluation"). Everything else in ADR-001 stands.

## Context

ADR-001 planned the AI worker in .NET so that the backend has one language. The owner has built and tested a working AI scribe backend in Python (the HealthOps project): per-participant recording consent with withdrawal, encrypted chunked upload, Azure Speech fast transcription with speakers, Azure AI Foundry strict-JSON drafting with an evidence quote per fact, a grounding check that drops invented medicines and diagnoses, per-fact review, a manual-note path, audio purge and an evaluation harness. It has 241 tests (unit, integration on real PostgreSQL, evaluation replay) and has been run against the live Azure services. Rewriting it in .NET would cost the P3 timeline without adding safety. Most current AI tooling (model SDKs, evaluation, speech) is also Python-first, and the coming AI features (WhatsApp and clinic chat, support desk, voice) need the same provider adapters.

## Decision

1. **One AI service: `src/AiService`** (Python 3.13, FastAPI API plus a job worker, one container image). It holds every model and speech call in Medvya. Package `medvya_ai`. It starts with the scribe pipeline (ai-notes, P3) imported from HealthOps; conversation features (chat, WhatsApp, support) add their orchestration here later under their own decision (planned ADR-022). Business logic, records and every user-facing API stay in the .NET modular monolith.
2. **Medvya stays the system of record.** Patients, encounters, appointments, consent receipts and signed notes live only in Medvya. The AI service keeps working data only: encrypted audio chunks, transcripts and corrections, drafts, facts with their evidence, jobs. It returns drafts. It never signs and never becomes a second source of truth. The HealthOps approve, export (PDF), analytics, privacy-request and receptionist endpoints were removed (#1417): Medvya signs, prints and handles data requests itself.
3. **Only Medvya's API calls it, server to server.** The browser and the apps never call the AI service (ADR-004: no tokens in the browser). The Encounters module holds the client and checks capability, tenant, care relationship, consent and the tenant feature flag before every call. Its endpoints are in Host.Api's OpenAPI document, so the web client is generated as usual. The AI service has no public ingress.
4. **Tenant and patient mapping.** The AI service's clinic id is the Medvya tenant id. `patient_ref` carries the Medvya encounter id only, never a name, phone number or record number (the column is a `uuid` since migration 0008). Participants reach the model as roles, never names. The AI service keeps its own two isolation layers: row-level security on `clinic_id` (`ENABLE` + `FORCE`), and an application role that is not the owner and has no `BYPASSRLS`.
5. **Its own database and blob container.** The AI service uses its own PostgreSQL database (`medvya_ai`, its own roles, Alembic migrations, forward-reviewed like EF migrations) and its own container, in the same country and region as the Medvya data plane (ADR-003). In development it may share the PostgreSQL 16 server; it never shares tables with a .NET module. CI tests it on PostgreSQL 16, the version Azure runs.
6. **Same rules as the rest of Medvya.** Synthetic data only. No PHI in logs, telemetry, URLs or job errors (the service never records bodies and scrubs span attributes and logs by key name and pattern, `medvya_ai/telemetry.py`; Medvya's allow-list scrubber is stricter, so moving to an allow-list is a pilot condition below). Secrets live in Key Vault or local user secrets (`src/AiService/.env.example` lists names only). Its dependencies are pinned in `uv.lock`, updated by Dependabot and scanned with `pip-audit` in CI.
7. **CI.** The `ai-service` job runs ruff, the format check, strict mypy, every test (unit, integration on real PostgreSQL, evaluation replay) and `pip-audit` whenever `src/AiService/` changes. It is part of the required `dotnet` (backend) check. The integration tests fail, never skip, when CI has no database.

## Development mode (allowed now)

- `AUTH_MODE=disabled` with `X-Clinic-Id` (required; there is no default clinic), only with `ENVIRONMENT=development`, on a private network that only Medvya's API can reach. Fake providers in CI, and the live Azure Speech and Foundry resources in dev, with synthetic or consented test audio only.

## Retention of working data (proposed, owner to confirm)

- The hourly retention sweep purges a session's working data (transcript, corrections, facts, evidence, rejections, AI draft, and participant names, which become their role) **30 days** after the session finished, or was created if nothing was recorded (`RETENTION_SESSION_DAYS`). Audio follows the shorter audio rules. The consent log and the audit trail are kept. Medvya holds the signed note, so nothing the clinic needs is lost. The period is a proposal: the owner confirms it (or a shorter one) with the legal review before the pilot.

## Before real patient data (the pilot gate for AI)

1. **Service identity.** *Built in the AI service (#1417); Host.Api side and Azure set-up pending.* `AUTH_MODE=service` verifies the Entra token (audience, issuer), requires an allow-listed caller (`azp`/`appid` in `AUTH_ALLOWED_CLIENT_IDS`) with the app role `AUTH_REQUIRED_ROLE`, and only then trusts `X-Clinic-Id`, `X-User-Id` and `X-User-Role` (validated; role limited to `doctor`/`admin`) for the RLS tenant and the audit actor (`deps.get_tenant`). `AUTH_MODE=disabled` is refused unless `ENVIRONMENT=development`. Still needed: Host.Api sends its managed-identity token and the three headers (MDV-P3-029), the app registration with the app role assigned to Host.Api's identity, and the settings in Key Vault.
2. **Processing region.** Speech, the Foundry deployment (deployment type Standard, regional), storage and the database all sit in the launch country, with portal evidence attached (ADR-007, MDV-P3-001; `src/AiService/docs/decisions/0003-processing-region-and-processors.md`). Fast transcription is not offered in UAE North, so the UAE needs its own answer in ADR-007. The worker refuses to start when a real Speech or Foundry endpoint host is not on `PROCESSING_ENDPOINT_HOSTS` (the country's allow-list), and each job records the host it called.
3. **Worker privileges.** [ai-pipeline.md](../03-architecture/ai-pipeline.md) asks for a worker with no database credentials. Today the worker uses the least-privilege application role and claims jobs across tenants from `processing_job` (no PHI, no row-level security). Either narrow it to the scoped job and callback design (MDV-P3-007) or accept the deviation in ADR-007 with the security review (P1-038).
4. **Keys and telemetry.** The data-key wrapping key moves from an environment variable to Key Vault (`KeyWrapper`). Telemetry scrubbing becomes an allow-list like `src/Host.Api/Observability`.
5. **Consent.** Medvya's consent receipts (MDV-P3-002, MDV-P3-003) are the source. The AI service records what Medvya passes, so the two cannot disagree.
6. **Vendor and legal.** AI vendor DPA, no-training terms and recording-consent review (MDV-P3-022, MDV-P3-023).

## Not chosen

- **Rewrite in .NET now (ADR-001 as written):** weeks of P3 time to rebuild tested behaviour, with no safety gain.
- **Let the browser call the AI service directly:** needs bearer tokens in the browser (against ADR-004), duplicates authorisation, and skips the generated contract.
- **Keep HealthOps as a separate repository:** the AI service is built and tested with Medvya's tenancy, consent and CI. A second repository would need its own release and contract sync for no gain (ADR-013 §9).

## Consequences

- Two production languages: .NET for the product, Python for AI. The AI service has its own lint, type, test and audit gates in CI.
- The HealthOps web app is not imported. Medvya builds its own ai-notes screens in `web/clinic` (ADR-009, `@medvya/ui`, i18n). The browser recorder there must not keep raw audio in browser storage (AGENTS.md 1.2).
- Imported HealthOps decisions, threat model, runbook and reports live in `src/AiService/docs/`.

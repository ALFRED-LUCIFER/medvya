# Medvya AI service (`src/AiService`)

The Python service that makes every AI and speech call in Medvya ([ADR-021](../../docs/adr/ADR-021-ai-service.md)). It started as the HealthOps scribe backend and was imported in MDV-P3-028. Today it holds the **ai-notes** pipeline: record a consultation with per-participant consent, transcribe Hindi, English and Hinglish with speakers, draft a SOAP note and structured facts with an evidence quote each, check every fact against the transcript, and hand the draft back for the doctor to review.

**Development and synthetic data only** until the pilot conditions in ADR-021 are met (service identity, in-country processing, Key Vault, worker privileges, consent source, vendor terms).

## Where it sits

```
web/clinic ──cookie──▶ Host.Api (Encounters module: capability, tenant, consent, feature flag)
                          │ server to server, private network
                          ▼
                    src/AiService  (FastAPI API + job worker)
                          ├─ PostgreSQL database medvya_ai (FORCE RLS on clinic_id)
                          ├─ Blob container (encrypted audio chunks)
                          └─ Azure Speech · Azure AI Foundry (in region)
```

- Medvya is the system of record. This service keeps working data only and never signs a note.
- Clinic id = Medvya tenant id. `patient_ref` = Medvya encounter id, never a name, phone or record number.
- The browser never calls this service. The Medvya client and endpoints are MDV-P3-029.

## Run it locally

Needs Docker, Python 3.13 and [uv](https://docs.astral.sh/uv/).

```sh
cd src/AiService
cp .env.example .env     # names only; fill locally, never commit
# PostgreSQL with the app role and the medvya_ai / medvya_ai_test databases (docker/01-roles.sh runs on first start):
docker run -d --name medvya-ai-pg -p 127.0.0.1:5440:5432 \
  -e POSTGRES_PASSWORD -e MEDVYA_AI_APP_PASSWORD -v "$PWD/docker:/docker-entrypoint-initdb.d:ro" postgres:16
uv sync
uv run --env-file .env alembic upgrade head                      # owner role (DATABASE_URL_MIGRATE)
uv run uvicorn medvya_ai.main:create_app --factory --reload      # API on http://localhost:8000, docs at /docs
uv run python -m medvya_ai.worker                                # second terminal: job worker
```

Blob storage: Azurite from `pnpm services:up` (port 10001), with the development connection string from the [Azurite docs](https://learn.microsoft.com/azure/storage/common/storage-use-azurite). Without Azure keys keep `TRANSCRIPTION_PROVIDER=fake` and `LLM_PROVIDER=fake`. With real providers the worker refuses to start unless each endpoint host is on `PROCESSING_ENDPOINT_HOSTS` (in-country allow-list). `/healthz` is liveness, `/readyz` checks the database and that the blob container exists (it creates nothing). The image has no `HEALTHCHECK` because the worker shares it: probes belong to the API's deployment config.

### Who may call it (`AUTH_MODE`)

- `service` (default, the pilot path, ADR-021 gate 1): Host.Api calls with its managed-identity token. The service checks audience and issuer, the caller's client id (`AUTH_ALLOWED_CLIENT_IDS`) and app role (`AUTH_REQUIRED_ROLE`); only then does it trust `X-Clinic-Id` (tenant, sets row-level security), `X-User-Id` (the Medvya user, audit actor) and `X-User-Role` (`doctor` or `admin`). A missing or malformed header is 400.
- `entra`: a per-user token whose `oid` matches a provisioned `app_user` (`python -m medvya_ai.seed --provision ...`).
- `disabled`: no token; `X-Clinic-Id` is required and the request acts as that clinic's first user (`python -m medvya_ai.seed` creates one and prints its id). Refused unless `ENVIRONMENT=development`; never with patient data.

Outside development the OpenAPI document and `/docs` are not served.

## Tests

```sh
uv run ruff check . && uv run ruff format --check . && uv run mypy
uv run pytest -q tests/unit tests/evals      # no database needed
MEDVYA_AI_TEST_DATABASE_URL=… MEDVYA_AI_TEST_DATABASE_URL_MIGRATE=… uv run pytest -q   # + integration on real PostgreSQL
uv run pytest -q -m live tests/evals         # extraction gate against the deployed model (off by default)
```

Locally the integration tests skip when no database is reachable; in CI (`CI` set) they fail instead. CI runs everything on PostgreSQL 16 in the `ai-service` job (`.github/workflows/pr.yml`).

## API (prefix `/api/v1`)

| Area | Endpoints |
|---|---|
| Sessions and consent | `POST /sessions`, `GET /sessions/{id}`, `POST /sessions/{id}/participants`, `GET /consent-notice`, `POST /sessions/{id}/consents` (stop vs withdraw: [decision 0001](docs/decisions/0001-stop-vs-withdraw.md)) |
| Recording | `POST /sessions/{id}/segments`, `PUT …/segments/{n}/chunks/{seq}` (SHA-256 checked), `POST …/segments/{n}/complete`, `POST /sessions/{id}/finish` |
| Draft and review | `GET /sessions/{id}/result`, `GET …/facts/{fact}/evidence-audio`, `PATCH /facts/{id}`, `PATCH /sessions/{id}/speakers`, `PATCH …/transcript/turns/{turn}`, `POST …/transcript/apply-suggested-roles`, `POST …/reprocess` (re-reads the clinician's corrections; 409 `FACTS_REVIEWED` once a fact was reviewed), `POST /sessions/{id}/notes` (manual note), `PATCH /sessions/{id}/note`, `GET …/notes/revisions` |

The HealthOps approve, export (PDF), analytics, privacy-request and receptionist endpoints were removed (#1417, ADR-021 §2): Medvya signs, prints and handles data requests itself. The receptionist intake is in git history for the AI communication middleware ([ADR-022](../../docs/adr/ADR-022-ai-communication-middleware.md), MDV-P4-038).

Retention: audio is deleted after `RETENTION_DRAFT_AUDIO_DAYS`; all working data of a session (transcript, facts, draft, participant names) is purged `RETENTION_SESSION_DAYS` (30, proposed) after it finished.

## Documents

- [docs/provider-notes.md](docs/provider-notes.md): Azure Speech and Foundry facts checked on Microsoft Learn.
- [docs/decisions/](docs/decisions/0002-transcription-and-translation.md): HealthOps decisions (stop vs withdraw, transcription and translation, processing region).
- [docs/ops/threat-model.md](docs/ops/threat-model.md) · [docs/ops/runbook.md](docs/ops/runbook.md).
- [docs/reports/](docs/reports/live-run-three-speaker-2026-11-10.md): completion report and the live three-speaker run.

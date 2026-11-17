# Operator runbook

## Deploy / upgrade

```sh
cd src/AiService
uv run --env-file .env alembic upgrade head      # owner role; reversible (downgrade -1)
docker build -t medvya-ai-service src/AiService
docker run --env-file .env -p 8000:8000 medvya-ai-service                 # API
docker run --env-file .env medvya-ai-service python -m medvya_ai.worker   # worker (scale horizontally)
```

Required for production: `ENVIRONMENT=production`, `AUTH_MODE=service` with `AUTH_ISSUER`,
`AUTH_AUDIENCE`, `AUTH_ALLOWED_CLIENT_IDS` (Host.Api's managed identity) and `AUTH_REQUIRED_ROLE`,
real providers with `PROCESSING_ENDPOINT_HOSTS`, `ENCRYPTION_MASTER_KEY` injected from Key Vault,
`APPLICATIONINSIGHTS_CONNECTION_STRING`. No user provisioning in service mode: the clinic row and
one user row per Medvya user are created on first use. The API container probes `/healthz` and
`/readyz` (the image has no HEALTHCHECK: the worker shares it).

## Rollback

Deploy the previous image; run `alembic downgrade <previous revision>` only if the new release
wrote no rows the old code cannot read (enum values cannot be removed: 0003 and 0005 add
`revoked`, `cancelled`, `purge_session`, `session_mode`, privacy enums; the old code treats unknown
states as unprocessable, so a downgrade of code without the schema is safe for reads).

## Health

`GET /healthz` (process), `GET /readyz` (database + blob). Worker logs `job done|queued|failed|
cancelled kind=... id=...` with codes only.

## Queue operations (psql as owner, no PHI in `processing_job`)

```sql
-- dead letters
SELECT id, clinic_id, session_id, kind, attempts, last_error, updated_at
FROM processing_job WHERE status = 'failed' ORDER BY updated_at DESC;
-- re-enqueue one after fixing the cause (purge jobs are idempotent)
UPDATE processing_job SET status = 'queued', attempts = 0, run_after = now() WHERE id = '<id>';
-- stuck running jobs are reclaimed after 15 minutes without heartbeat while attempts remain;
-- on the last attempt the worker marks them failed with last_error = 'WORKER_LOST'
-- model jobs (transcribe, normalise, extract) have max_attempts 3; provider calls 3 tries each
```

A failed `purge_session` must be re-enqueued the same day: until it runs, audio of a revoked
session still exists (encrypted) in Blob.

## Consent withdrawal and erasure

Withdrawal is done by the clinician through Medvya, which calls `POST /sessions/{id}/consents` with
`status=withdrawn`; the purge job deletes the session's audio and working data. Medvya handles
patient data requests itself; this service's working data also expires `RETENTION_SESSION_DAYS`
after the session finished (hourly retention sweep, audit action `session_retention_expired`).

## Cost and usage

`processing_job.usage` holds counters per job (audio ms, tokens, model, endpoint host). Cross-check
with Azure cost management for the Speech and Foundry resources. There is no per-clinic cost cap
yet (shortcut in `jobs/queue.py`).

## Tests

```sh
cd src/AiService && set -a && source .env && set +a
uv run pytest -q                      # unit + integration (Postgres via docker compose)
uv run pytest -q tests/evals          # replay eval gate; add -m live for the deployed model
uv run ruff check . && uv run mypy
uv run python scripts/e2e_smoke.py
```

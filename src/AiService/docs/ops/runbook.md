# Operator runbook

## Deploy / upgrade

```sh
cd src/AiService
uv run --env-file .env alembic upgrade head      # owner role; reversible (downgrade -1)
docker build -t medvya-ai-service src/AiService
docker run --env-file .env -p 8000:8000 medvya-ai-service                 # API
docker run --env-file .env medvya-ai-service python -m medvya_ai.worker   # worker (scale horizontally)
```

Required for production: `AUTH_MODE=entra` with `AUTH_ISSUER`/`AUTH_AUDIENCE`, real providers,
`ENCRYPTION_MASTER_KEY` injected from Key Vault, `APPLICATIONINSIGHTS_CONNECTION_STRING`.
Provision users: `python -m medvya_ai.seed --provision --email ... --subject <oid> --clinic-id ...`.

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
-- stuck running jobs are reclaimed automatically after 15 minutes without heartbeat
```

A failed `purge_session` must be re-enqueued the same day: until it runs, audio of a revoked
session still exists (encrypted) in Blob.

## Consent withdrawal and erasure

Withdrawal is done by the clinician through Medvya, which calls `POST /sessions/{id}/consents` with
`status=withdrawn`. Erasure requests: `POST /privacy-requests` with the verification note;
the decision lists purged and retained (approved) sessions. Approved records follow the clinic's
record retention; audio is purged in both cases.

## Cost and usage

`GET /analytics/usage` (per month and job kind: audio ms, tokens). Cross-check with Azure cost
management for the Speech and Foundry resources.

## Tests

```sh
cd src/AiService && set -a && source .env && set +a
uv run pytest -q                      # unit + integration (Postgres via docker compose)
uv run pytest -q tests/evals          # replay eval gate; add -m live for the deployed model
uv run ruff check . && uv run mypy
uv run python scripts/e2e_smoke.py
```

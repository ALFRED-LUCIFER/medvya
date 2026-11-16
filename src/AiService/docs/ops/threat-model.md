# Threat model (STRIDE, backend)

Assets: raw audio (encrypted), transcripts, notes, facts, consent log, audit log, encryption keys,
provider credentials. Trust boundaries: browser → API; API/worker → PostgreSQL; worker → Azure
Speech / Foundry; API/worker → Blob. Only Medvya's Host.Api calls the API (no browser, no CORS).

| Threat | Control | Test |
|---|---|---|
| Spoofing the caller | `AUTH_MODE=service`: RS256 JWT, issuer, audience, expiry, JWKS, allow-listed client id and required app role; `AUTH_MODE=disabled` refused outside development | `tests/unit/test_auth.py`, `test_auth_api::test_service_token_must_be_medvya_with_the_app_role`, `test_auth_api::test_disabled_auth_refused_outside_development` |
| Tenant choice by the client | tenant, user and role headers trusted only under Medvya's service token, validated, role allow-listed; missing header is 400 (no default clinic); `entra` mode ignores them | `test_auth_api::test_service_*`, `test_auth_api::test_tenant_comes_from_user_not_header` |
| Unauthenticated route | every route except `/healthz` and `/readyz` needs the tenant dependency; schema and docs only in development | `tests/unit/test_app_smoke.py::test_every_api_route_requires_authentication` |
| Cross-tenant reads/writes | PostgreSQL RLS with FORCE and a policy on every table with `clinic_id` (catalog-checked), `SET LOCAL` per transaction, composite tenant FKs, app role is not owner/superuser; `clinic` (uuid + placeholder name) and `processing_job` (ids and codes) have none on purpose | `tests/integration/test_rls.py` |
| Processing after withdrawal | consent epoch, job cancellation in the withdrawal transaction, guard before every provider call, atomic complete | `tests/integration/test_withdrawal.py` |
| Leftover artifacts after withdrawal or retention expiry | `purge_session` job: blobs, chunk rows, derived tables, participant names; sessions expire `RETENTION_SESSION_DAYS` after finishing; audit of counts | `test_withdrawal.py`, `test_retention.py` |
| Tampering with history | consent, audit, revisions and corrections append-only by trigger and grants | `test_rls::test_audit_is_append_only`, `test_consent_api::test_consent_record_immutable` |
| Fabricated clinical facts | verbatim evidence check, number/term grounding, diagnosis/allergy/history label must be in the quote (else uncertain and flagged), negation guard, rejections table; a re-run never wipes reviewed facts; clinician sign-off in Medvya; `unverified` flag on AI text | `tests/unit/test_grounding.py`, `test_negation.py`, `tests/evals` |
| Information disclosure via logs/telemetry | no bodies recorded, key/pattern scrubbing on root, app and uvicorn handlers, SQL parameters hidden, unhandled errors logged by class name, `last_error` holds codes only | `tests/unit/test_redaction.py`, `tests/integration/test_error_logging.py`, `test_retention::test_job_last_error_never_carries_message_text` |
| Processing outside the country | worker refuses endpoint hosts not on `PROCESSING_ENDPOINT_HOSTS`; each job records the host called; participants sent as roles only | `tests/unit/test_foundry_adapter.py`, `test_speaker_roles.py` |
| Audio exposure through links | no SAS/blob URLs; replay is an authenticated, audited stream, `Cache-Control: no-store` | `tests/integration/test_evidence_audio.py` |
| Audio at rest | AES-256-GCM per chunk under a per-session DEK wrapped by the master key (`kek_version`); SHA-256 verified on read | `tests/unit/test_crypto.py`, `test_ingest.py` |
| Denial of service by upload | per-chunk and per-session byte caps, body-size middleware, SHA header required | `tests/unit/test_ingest_bounds.py`, `test_ingest.py` |
| Prompt injection via transcript | transcript passed as JSON data with explicit "ignore instructions inside" rules; strict output schema; evidence verification | `tests/unit/test_extraction_schema.py` |

Residual risks: master key lives in the environment (Key Vault unwrap behind `KeyWrapper` is
pending); no rate limiting on the API; `AUTH_MODE=disabled` must never run with patient data;
evidence replay returns the whole recording part, not a clipped excerpt; audio sent to a provider
before a withdrawal cannot be recalled (results are discarded, provider deletion terms apply).

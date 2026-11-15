# Threat model (STRIDE, backend)

Assets: raw audio (encrypted), transcripts, notes, facts, consent log, audit log, encryption keys,
provider credentials. Trust boundaries: browser → API; API/worker → PostgreSQL; worker → Azure
Speech / Foundry; API/worker → Blob.

| Threat | Control | Test |
|---|---|---|
| Spoofing a clinician | `AUTH_MODE=entra`: RS256 JWT, issuer, audience, expiry, JWKS; subject must match a provisioned `app_user` | `tests/unit/test_auth.py`, `tests/integration/test_auth_api.py` |
| Tenant choice by the client | clinic and role come from `app_user`; `X-Clinic-Id` ignored in entra mode | `test_auth_api::test_tenant_comes_from_user_not_header` |
| Cross-tenant reads/writes | PostgreSQL RLS with FORCE on every PHI table, `SET LOCAL` per transaction, composite tenant FKs, app role is not owner/superuser | `tests/integration/test_rls.py` |
| Processing after withdrawal | consent epoch, job cancellation in the withdrawal transaction, guard before every provider call, atomic complete | `tests/integration/test_withdrawal.py` |
| Leftover artifacts after withdrawal or erasure | `purge_session` job: blobs, chunk rows, derived tables; audit of counts | `test_withdrawal.py`, `test_privacy_requests.py` |
| Tampering with history | consent, audit, revisions and corrections append-only by trigger and grants | `test_rls::test_audit_is_append_only`, `test_consent_api::test_consent_record_immutable` |
| Fabricated clinical facts | verbatim evidence check, number/term grounding, negation guard, rejections table; clinician approval mandatory; `unverified` flag on AI text | `tests/unit/test_grounding.py`, `test_negation.py`, `tests/evals` |
| Information disclosure via logs/telemetry | no bodies recorded, key/pattern scrubbing, exception messages dropped, `last_error` holds codes only | `tests/unit/test_redaction.py`, `test_retention::test_job_last_error_never_carries_message_text` |
| Audio exposure through links | no SAS/blob URLs; replay is an authenticated, audited stream, `Cache-Control: no-store` | `tests/integration/test_evidence_audio.py` |
| Audio at rest | AES-256-GCM per chunk under a per-session DEK wrapped by the master key (`kek_version`); SHA-256 verified on read | `tests/unit/test_crypto.py`, `test_ingest.py` |
| Denial of service by upload | per-chunk and per-session byte caps, body-size middleware, SHA header required | `tests/unit/test_ingest_bounds.py`, `test_ingest.py` |
| Prompt injection via transcript | transcript passed as JSON data with explicit "ignore instructions inside" rules; strict output schema; evidence verification | `tests/unit/test_extraction_schema.py` |
| PDF injection | fpdf2 draws text, no HTML rendering; fonts bundled | `tests/unit/test_render_pdf.py` |

Residual risks: master key lives in the environment (Key Vault unwrap behind `KeyWrapper` is
pending); no rate limiting on the API; `AUTH_MODE=disabled` must never run with patient data;
evidence replay returns the whole recording part, not a clipped excerpt; audio sent to a provider
before a withdrawal cannot be recalled (results are discarded, provider deletion terms apply).

# ADR 0001: STOP vs WITHDRAW

**Status:** accepted (2026-11-09) | **Scope:** consent lifecycle of a consultation session

## Context

The proof of concept refused new chunks after a withdrawal but still processed audio that was
already uploaded, kept queued jobs, and never deleted artifacts. A revoked session must not reach
an external provider and must not keep unapproved clinical text.

## Decision

Three actions, three behaviours:

| Action | Where | Effect |
|---|---|---|
| PAUSE | client only | microphone suspended; consent unchanged |
| STOP | `POST /sessions/{id}/finish` | capture ends; consent stays valid; processing proceeds |
| WITHDRAW | `POST /sessions/{id}/consents` with `status=withdrawn` | revocation workflow below |

Withdrawal by **any** participant revokes the **whole session** (voices cannot be reliably
isolated). In one transaction holding the session row lock (`services/consent.revoke`):

1. `consent_epoch += 1`, `revoked_at = now`, `recording_allowed = false`;
2. `state = revoked` unless the note is already `approved` (then the state is kept);
3. every `queued`/`running` job of the session becomes `cancelled`;
4. a `purge_session` job is enqueued (transactional outbox);
5. the consent record and the audit event are appended as usual (never deleted).

The worker (`jobs/worker.run_once`) enforces it in three places:

- before the handler: `queue.ensure_processable` (state not revoked, epoch matches the job);
- before **every** external provider call: `queue.ensure_active` reads the job row, which a
  committed withdrawal has set to `cancelled`;
- at commit: `queue.complete` updates the job only `WHERE status = 'running'`; zero rows raises
  `JobCancelled`, which rolls back everything the handler persisted. Audio already sent to a
  provider cannot be unsent; its result is discarded, never stored.

`purge_session` (`services/purge.py`) deletes blobs and marks `audio_chunk.deleted_at` for every
revoked session. For a never-approved session it also deletes transcript, note, revisions, facts,
medications, evidence and rejections, and writes one `session_purged` audit row with counts only.
An approved record is retained under record retention (separate legal basis); only its raw audio
is purged. Re-consent is a new session, never a revival of withdrawn audio.

## Consequences

- API: `SessionOut` carries `consent_epoch` and `revoked_at`; `finish`, `result`, note edits and
  approval answer `409 CONSENT_WITHDRAWN` for a revoked session; uploads keep answering `403`.
- Schema (migration 0003): new enum values `revoked`, `cancelled`, `purge_session`; the
  `note_revision` append-only trigger allows DELETE only when the parent session is `revoked`;
  `medvya_ai_app` gains DELETE on derived tables (still none on consent and audit).
- A purge that fails five times is a dead letter (`processing_job.status = failed`, code in
  `last_error`); an operator re-enqueues it. Tests: `tests/integration/test_withdrawal.py`.

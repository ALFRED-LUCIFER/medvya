# Completion report, final-MVP backend work (2026-11-09)

> Imported from the original HealthOps repository on 2026-11-16 (MDV-P3-028). Paths such as `apps/api` refer to that repository; the code now lives in `src/AiService`.

Status per blueprint item. Evidence: commits on `main`, tests under `apps/api/tests`.
Test run at the end of the work: `uv run pytest -q` 229 passed (unit + integration + evals
replay), ruff and mypy strict clean, web typecheck and 18 vitest tests green.

| Item | Status | Evidence |
|---|---|---|
| Two failing retention DB tests (actually five) | DONE | shared-queue isolation + UPDATE helper fix, `tests/integration/test_retention.py` |
| STOP vs WITHDRAW, consent epoch, cancel, outbox purge | DONE | migration 0003, `services/consent.revoke`, `jobs/queue`, `services/purge.py`, `test_withdrawal.py` (queued, mid-call, post-processing, post-approval), ADR 0001 |
| Authenticated tenant identity and RBAC | DONE (backend + web client); user provisioning via CLI | migration 0004, `auth.py`, `deps.get_tenant`, `test_auth.py`, `test_auth_api.py`; MSAL in `apps/web/src/auth.ts` |
| Processing region / processors | BLOCKED on owner decision and portal evidence | ADR 0003; usage records adapter/model/region per job |
| Clinical evaluation dataset (30+ recordings) | PARTIAL: harness and 6 synthetic text cases; no recordings | `tests/evals`; live mode ready; recordings and clinician gold outstanding |
| Original/translated separation, immutable transcript, corrections | DONE | ADR 0002, `transcript_correction`, `PATCH .../transcript/turns/{id}` |
| Speaker attribution: unknown, per-part ids, global offsets | DONE | `providers/transcription/base.speaker_label`, `services/transcribe` |
| Overlap indicators | NOT DONE | fast transcription response carries no overlap flag; would need word-level timing analysis |
| Evidence validator incl. negation, dose source constraints | DONE | `extraction/grounding.negated`, `persist.py`, `test_negation.py`, `test_grounding.py` |
| Evidence-aligned audio replay, 410 when deleted | DONE with documented shortcut (whole recording part, client seeks) | `services/evidence_audio.py`, `test_evidence_audio.py` |
| Manual encounter notes | DONE | `POST /sessions/{id}/notes`, `test_manual_note.py`, web button |
| Guardian/representative consent | DONE (backend fields + form) | `ConsentIn.representative_*`, migration 0005 |
| SOAP + concise renderers | DONE | `services/render.py`, `test_render_pdf.py` |
| PDF export (Hindi/Marathi shaping), approved only, hash, audit | DONE | `services/pdf.py`, fonts under `medvya_ai/fonts` (OFL), `test_review_flow.py` |
| Revision-read API | DONE | `GET /sessions/{id}/notes/revisions` |
| Privacy access/erasure requests | DONE (synchronous decision, state tracked) | `services/privacy.py`, `test_privacy_requests.py` |
| Observability / cost accounting | DONE (per-job usage, `/analytics/usage`); App Insights redaction pre-existing | `test_usage.py`, `telemetry.py` |
| Idempotency-Key on mutating commands | NOT DONE | chunk upload, finish and purge are idempotent by design; consent replays append harmless duplicate rows |
| Signature workflow, second ASR provider, wider Marathi | NOT DONE (P2) | `digital_signature` enum exists; no signature is ever fabricated |
| Fact provenance (`reported_by`), phrase confidence flags, result `quality` block | DONE | `services/note.py`, web transcript/facts components |
| AI receptionist intake route (owner request, POC) | DONE (synchronous LLM turn, code-side red-flag stop, opens a session) | migration 0006, `services/receptionist.py`, `test_receptionist.py`, web `#/receptionist` |
| Threat model, runbook, ADRs | DONE | `docs/ops/threat-model.md`, `docs/ops/runbook.md`, `docs/adr/000{1,2,3}` |

## Eval replay results (synthetic, gate on, 2026-11-09)

| case | slice | proposed | accepted | rejected by gate | negation flagged | precision | recall | WER / CER |
|---|---|---|---|---|---|---|---|---|
| 01_en_fever | en | 3 | 3 | 0 | 0 | 1.0 | 1.0 | 0.038 / 0.010 |
| 02_hi_negation | hi | 3 | 3 | 0 | 2 | 1.0 | 1.0 | 0.0 / 0.0 |
| 03_mr_dose | mr | 3 | 3 | 0 | 1 | 1.0 | 1.0 | n/a |
| 04_hinglish_switch | en-hi switch | 6 | 5 | 1 (invented diagnosis) | 0 | 1.0 | 1.0 | n/a |
| 05_three_speakers_overlap | 3-speaker | 4 | 3 | 1 (invented drug) | 1 | 1.0 | 1.0 | n/a |
| 06_dose_correction_noisy | negatives/doses | 3 | 3 | 0 | 1 (wrong strength dropped) | 1.0 | 1.0 | 0.095 / 0.080 |

These numbers measure the deterministic gate on recorded model output, not the deployed model
and not real speech. No clinical-safety claim follows from them.

## Remaining risks and open decisions

- India processing location unproven (ADR 0003). Blocks any real-patient pilot.
- No consented/synthetic recordings yet: WER, diarization error and model accuracy are unmeasured.
- Master key in environment; Key Vault unwrap pending behind `KeyWrapper`.
- Evidence replay streams the whole recording part (up to 60 minutes) rather than a clip.
- Local `.env` files created before this work lack `AUTH_MODE`; the API now defaults to `entra`
  and refuses to start without issuer and audience. Set `AUTH_MODE=disabled` for local work.

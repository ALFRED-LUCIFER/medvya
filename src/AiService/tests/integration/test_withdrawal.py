"""WITHDRAW (revoke) vs STOP (finish). ADR: src/AiService/docs/decisions/0001-stop-vs-withdraw.md.

A withdrawal must stop capture, cancel queued and running work, never reach an external provider
afterwards, discard in-flight results, purge unapproved artifacts and keep the audit trail."""

from collections.abc import Sequence
from typing import Any

import pytest
from sqlalchemy import text

from medvya_ai.providers.transcription.base import Phrase
from medvya_ai.providers.transcription.fake import DEFAULT_PHRASES
from tests.integration.helpers import (
    API,
    consent,
    finished_session,
    mark_approved,
    put_chunk,
    segment,
)
from tests.integration.pipeline import deps_for, drain

pytestmark = pytest.mark.integration

DERIVED = ("transcript_segment", "clinical_note", "note_revision", "clinical_fact", "fact_evidence")


async def _rows(app: Any, cid: Any, sql: str, **p: Any) -> list[Any]:
    async with app.state.sessionmaker() as db, db.begin():
        await db.execute(text("SELECT set_config('app.clinic_id', :c, true)"), {"c": str(cid)})
        return (await db.execute(text(sql), p)).all()


async def _counts(app: Any, cid: Any, sid: str) -> dict[str, int]:
    out = {}
    for t in DERIVED:
        col = "note_id IN (SELECT id FROM clinical_note WHERE session_id = :s)"
        if t == "fact_evidence":
            col = "fact_id IN (SELECT id FROM clinical_fact WHERE session_id = :s)"
        elif t != "note_revision":
            col = "session_id = :s"
        out[t] = (await _rows(app, cid, f"SELECT count(*) FROM {t} WHERE {col}", s=sid))[0][0]  # noqa: S608
    return out


async def _jobs(app: Any, sid: str) -> dict[str, str]:
    async with app.state.sessionmaker() as db, db.begin():
        rows = await db.execute(
            text("SELECT kind::text, status::text FROM processing_job WHERE session_id = :s"),
            {"s": sid},
        )
        return {r[0]: r[1] for r in rows}


async def _withdraw(c: Any, h: Any, sid: str) -> Any:
    s = (await c.get(f"{API}/sessions/{sid}", headers=h)).json()
    return await consent(c, h, sid, s["participants"][1]["id"], "withdrawn")


class CountingTranscriber:
    def __init__(self) -> None:
        self.calls = 0

    async def transcribe(self, audio: bytes, **_: Any) -> list[Phrase]:
        self.calls += 1
        return list(DEFAULT_PHRASES)


async def test_withdraw_while_queued_cancels_job_purges_and_blocks_everything(
    app_ctx, make_clinic, blobs
) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, _, h = await make_clinic()
    sid = await finished_session(c, h)
    assert len(blobs.blobs) == 2

    r = await _withdraw(c, h, sid)
    assert r.status_code == 201 and r.json()["session_state"] == "revoked"
    s = (await c.get(f"{API}/sessions/{sid}", headers=h)).json()
    assert s["state"] == "revoked" and s["consent_epoch"] == 1 and s["revoked_at"]
    assert await _jobs(app, sid) == {"transcribe": "cancelled", "purge_session": "queued"}

    deps = deps_for(app)
    counting = CountingTranscriber()
    deps.transcriber = counting
    await drain(app, deps)
    assert counting.calls == 0, "a revoked session must never reach the ASR provider"
    assert blobs.blobs == {}
    assert await _jobs(app, sid) == {"transcribe": "cancelled", "purge_session": "done"}
    live = await _rows(app, cid, "SELECT count(*) FROM audio_chunk WHERE deleted_at IS NULL")
    assert live[0][0] == 0
    audit = await _rows(
        app,
        cid,
        "SELECT action, metadata FROM audit_event WHERE action IN ('consent_withdrawn', "
        "'session_purged') AND (entity_id = :s OR metadata->>'session_id' = :sid) ORDER BY id",
        s=sid,
        sid=sid,
    )
    assert [a[0] for a in audit] == ["consent_withdrawn", "session_purged"]
    assert audit[1][1]["chunks"] == 2

    # nothing can continue: upload, finish, result and edits are all refused
    assert (await segment(c, h, sid, 1)).status_code == 403
    assert (await put_chunk(c, h, sid, 5, b"zzz")).status_code == 403
    r = await c.post(f"{API}/sessions/{sid}/finish", headers=h)
    assert r.status_code == 409 and r.json()["error"]["code"] == "CONSENT_WITHDRAWN"
    r = await c.get(f"{API}/sessions/{sid}/result", headers=h)
    assert r.status_code == 409 and r.json()["error"]["code"] == "CONSENT_WITHDRAWN"
    # withdrawing again is idempotent (consent log grows, nothing else changes)
    assert (await _withdraw(c, h, sid)).status_code == 201
    assert (await c.get(f"{API}/sessions/{sid}", headers=h)).json()["consent_epoch"] == 1


async def test_withdraw_during_provider_call_discards_results(app_ctx, make_clinic, blobs) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, _, h = await make_clinic()
    sid = await finished_session(c, h)

    class WithdrawMidCall:
        async def transcribe(self, audio: bytes, **_: Any) -> Sequence[Phrase]:
            await _withdraw(c, h, sid)  # the patient withdraws while audio is at the provider
            return DEFAULT_PHRASES

    deps = deps_for(app)
    deps.transcriber = WithdrawMidCall()
    assert await drain(app, deps) >= 1
    assert (await _counts(app, cid, sid))["transcript_segment"] == 0, "in-flight result stored"
    jobs = await _jobs(app, sid)
    assert jobs["transcribe"] == "cancelled" and jobs["purge_session"] == "done"
    assert blobs.blobs == {}


async def test_withdraw_after_processing_purges_transcript_note_and_facts(
    app_ctx, make_clinic, blobs
) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, _, h = await make_clinic()
    sid = await finished_session(c, h)
    assert await drain(app, deps_for(app)) == 3
    before = await _counts(app, cid, sid)
    assert all(v > 0 for v in before.values()), before

    await _withdraw(c, h, sid)
    await drain(app, deps_for(app))
    assert set((await _counts(app, cid, sid)).values()) == {0}
    assert blobs.blobs == {}
    # consent history and audit survive the purge
    n = await _rows(app, cid, "SELECT count(*) FROM consent_record WHERE session_id = :s", s=sid)
    assert n[0][0] == 3


async def test_withdraw_after_approval_keeps_record_but_purges_audio(
    app_ctx, make_clinic, blobs
) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, _, h = await make_clinic()
    sid = await finished_session(c, h)
    await drain(app, deps_for(app))
    await mark_approved(app, cid, sid)
    before = await _counts(app, cid, sid)

    r = await _withdraw(c, h, sid)
    assert r.json()["session_state"] == "approved"
    await drain(app, deps_for(app))
    assert blobs.blobs == {}
    assert await _counts(app, cid, sid) == before  # the approved record follows its own policy
    s = (await c.get(f"{API}/sessions/{sid}", headers=h)).json()
    assert s["state"] == "approved" and s["revoked_at"]


async def test_stop_is_not_withdraw(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    """STOP (finish) ends capture; consent stays valid and processing proceeds."""
    app, c = app_ctx
    _, _, h = await make_clinic()
    sid = await finished_session(c, h)
    s = (await c.get(f"{API}/sessions/{sid}", headers=h)).json()
    assert s["state"] == "processing" and s["consent_epoch"] == 0 and s["revoked_at"] is None
    assert await drain(app, deps_for(app)) == 3
    assert (await c.get(f"{API}/sessions/{sid}/result", headers=h)).status_code == 200

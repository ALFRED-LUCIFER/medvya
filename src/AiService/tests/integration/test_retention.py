import uuid
from datetime import UTC, datetime, timedelta
from typing import Any

import pytest
from sqlalchemy import text

from medvya_ai.config import Settings
from medvya_ai.db import set_tenant
from medvya_ai.jobs.scheduler import enqueue_retention_sweeps
from medvya_ai.jobs.worker import run_once
from medvya_ai.services.retention import sweep
from tests.integration.helpers import finished_session
from tests.integration.pipeline import deps_for, drain

pytestmark = pytest.mark.integration
APPROVED = datetime(2026, 11, 1, 9, 0, tzinfo=UTC)


async def _q(app: Any, cid: Any, sql: str, **p: Any) -> Any:
    async with app.state.sessionmaker() as db, db.begin():
        await set_tenant(db, cid)
        res = await db.execute(text(sql), p)
        return res.all() if res.returns_rows else []


async def _sweep(app: Any, cid: Any, now: datetime, settings: Settings) -> int:
    async with app.state.sessionmaker() as db, db.begin():
        await set_tenant(db, cid)
        return await sweep(db, app.state.blobs, clinic_id=cid, now=now, settings=settings)


async def _approved_session(app: Any, c: Any, cid: Any, h: Any) -> str:
    sid = await finished_session(c, h)
    assert await drain(app, deps_for(app)) >= 3  # transcript, note and facts now exist
    await _q(
        app,
        cid,
        "UPDATE consultation_session SET state = 'approved', approved_at = :t WHERE id = :s",
        t=APPROVED,
        s=uuid.UUID(sid),
    )
    return sid


async def _record_counts(app: Any, cid: Any) -> list[int]:
    out = []
    for t in ("transcript_segment", "clinical_note", "clinical_fact", "note_revision"):
        out.append((await _q(app, cid, f"SELECT count(*) FROM {t}"))[0][0])  # noqa: S608
    return out


async def test_approved_audio_deleted_at_24h_record_untouched_and_idempotent(
    app_ctx, make_clinic, settings, blobs
) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, _, h = await make_clinic()
    await _approved_session(app, c, cid, h)
    before = await _record_counts(app, cid)
    assert before[0] > 0 and len(blobs.blobs) == 2

    assert await _sweep(app, cid, APPROVED + timedelta(hours=23, minutes=59), settings) == 0
    assert len(blobs.blobs) == 2

    now = APPROVED + timedelta(hours=24)
    assert await _sweep(app, cid, now, settings) == 2
    assert blobs.blobs == {}
    live = await _q(app, cid, "SELECT count(*) FROM audio_chunk WHERE deleted_at IS NULL")
    assert live[0][0] == 0
    audit = await _q(
        app,
        cid,
        "SELECT metadata->>'chunks', actor_user_id FROM audit_event WHERE action = 'audio_deleted'",
    )
    assert [(a[0], a[1]) for a in audit] == [("2", None)]

    assert await _sweep(app, cid, now + timedelta(hours=1), settings) == 0  # rerun: no-op
    audit = await _q(app, cid, "SELECT 1 FROM audit_event WHERE action = 'audio_deleted'")
    assert len(audit) == 1
    assert await _record_counts(app, cid) == before


async def test_draft_audio_deleted_at_7_days(app_ctx, make_clinic, settings, blobs) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, _, h = await make_clinic()
    await finished_session(c, h)
    created = datetime.now(UTC)
    assert await _sweep(app, cid, created + timedelta(days=6, hours=23), settings) == 0
    assert await _sweep(app, cid, created + timedelta(days=7, minutes=5), settings) == 2
    assert blobs.blobs == {}


async def test_clinic_policy_overrides_env_default(app_ctx, make_clinic, settings, blobs) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, _, h = await make_clinic()
    await _approved_session(app, c, cid, h)
    await _q(app, cid, "UPDATE retention_policy SET audio_after_approval_hours = 1")
    other, _, _ = await make_clinic("other")
    assert await _sweep(app, other, APPROVED + timedelta(hours=2), settings) == 0  # tenant scoped
    assert await _sweep(app, cid, APPROVED + timedelta(hours=1), settings) == 2


async def test_scheduler_enqueues_one_sweep_per_clinic(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, _ = app_ctx
    a, _, _ = await make_clinic("a")
    b, _, _ = await make_clinic("b")
    await enqueue_retention_sweeps(app.state.sessionmaker)
    await enqueue_retention_sweeps(app.state.sessionmaker)
    async with app.state.sessionmaker() as db, db.begin():
        n = await db.scalar(
            text(
                "SELECT count(*) FROM processing_job WHERE kind = 'retention_sweep' "
                "AND clinic_id IN (:a, :b)"
            ),
            {"a": a, "b": b},
        )
    assert n == 2


async def test_job_last_error_never_carries_message_text(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, _ = app_ctx
    cid, _, _ = await make_clinic()
    await enqueue_retention_sweeps(app.state.sessionmaker)

    async def boom(*_: Any) -> None:
        raise ValueError("patient has diabetes")

    handlers = {k: boom for k in ("transcribe", "normalise", "extract", "retention_sweep")}
    deps = deps_for(app)
    for _ in range(100):
        await run_once(app.state.sessionmaker, deps, "w", handlers)  # type: ignore[arg-type]
        async with app.state.sessionmaker() as db, db.begin():
            err = await db.scalar(
                text("SELECT last_error FROM processing_job WHERE clinic_id = :c"), {"c": cid}
            )
        if err:
            break
    assert err == "ValueError"

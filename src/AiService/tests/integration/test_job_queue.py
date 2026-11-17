"""Job queue on real PostgreSQL: one claimant, stale reclaim within the attempt limit, ownership."""

import asyncio
from typing import Any

import pytest
from sqlalchemy import text

from medvya_ai.jobs import queue
from tests.integration.helpers import finished_session

pytestmark = pytest.mark.integration


async def _sql(app: Any, sql: str, **params: Any) -> Any:
    async with app.state.sessionmaker() as db, db.begin():
        r = await db.execute(text(sql), params)
        return r.all() if r.returns_rows else None


async def _claim(app: Any, worker: str) -> queue.Job | None:
    async with app.state.sessionmaker() as db, db.begin():
        return await queue.claim(db, worker)


async def _sweep_job(app: Any, make_clinic: Any) -> Any:
    cid, _, _ = await make_clinic()
    async with app.state.sessionmaker() as db, db.begin():
        await queue.enqueue(db, clinic_id=cid, session_id=None, kind="retention_sweep")
    return cid


async def test_two_workers_claim_concurrently_only_one_gets_the_job(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, _ = app_ctx
    await _sweep_job(app, make_clinic)
    got = await asyncio.gather(*(_claim(app, f"w{i}") for i in range(4)))
    assert sum(j is not None for j in got) == 1


async def test_stale_job_is_reclaimed_until_attempts_are_used_up(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    # SAFETY: mutation check: removed `AND attempts < max_attempts` from queue._CLAIM ->
    # this test failed (the exhausted job was claimed again)
    app, c = app_ctx
    _, _, h = await make_clinic()
    sid = await finished_session(c, h)
    first = await _claim(app, "dead")
    assert first is not None and first.kind == "transcribe" and first.max_attempts == 3
    stale = "UPDATE processing_job SET locked_at = now() - interval '1 hour' WHERE id = :j"
    await _sql(app, stale, j=first.id)
    again = await _claim(app, "alive")  # the dead worker's job is picked up again
    assert again is not None and again.id == first.id and again.attempts == 2
    await _sql(app, "UPDATE processing_job SET attempts = max_attempts WHERE id = :j", j=first.id)
    await _sql(app, stale, j=first.id)
    assert await _claim(app, "third") is None  # last attempt killed the worker: not reclaimed
    async with app.state.sessionmaker() as db, db.begin():
        assert await queue.fail_exhausted_stale(db) == 1
    (row,) = await _sql(
        app, "SELECT status::text, last_error FROM processing_job WHERE id = :j", j=first.id
    )
    assert tuple(row) == ("failed", "WORKER_LOST")
    assert (await c.get(f"/api/v1/sessions/{sid}", headers=h)).json()["state"] == "failed"


async def test_writes_need_the_claiming_worker(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    # SAFETY: mutation check: dropped the locked_by condition from queue._ours ->
    # this test failed (the reclaimed job was completed by the first worker)
    app, _ = app_ctx
    await _sweep_job(app, make_clinic)
    job = await _claim(app, "w1")
    assert job is not None
    # a heartbeat stall: another worker reclaimed the row
    await _sql(app, "UPDATE processing_job SET locked_by = 'w2' WHERE id = :j", j=job.id)
    async with app.state.sessionmaker() as db, db.begin():
        with pytest.raises(queue.JobCancelled):
            await queue.ensure_active(db, job)
    with pytest.raises(queue.JobCancelled):
        async with app.state.sessionmaker() as db, db.begin():
            await queue.complete(db, job, {})
    async with app.state.sessionmaker() as db, db.begin():
        assert await queue.fail(db, job, "X") == "lost"
        await queue.touch(db, job)
    (row,) = await _sql(
        app, "SELECT status::text, locked_by FROM processing_job WHERE id = :j", j=job.id
    )
    assert tuple(row) == ("running", "w2")


async def test_fail_never_requeues_a_cancelled_job(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, _ = app_ctx
    await _sweep_job(app, make_clinic)
    job = await _claim(app, "w1")
    assert job is not None
    await _sql(app, "UPDATE processing_job SET status = 'cancelled' WHERE id = :j", j=job.id)
    async with app.state.sessionmaker() as db, db.begin():
        assert await queue.fail(db, job, "X") == "lost"
    (row,) = await _sql(app, "SELECT status::text FROM processing_job WHERE id = :j", j=job.id)
    assert row[0] == "cancelled"

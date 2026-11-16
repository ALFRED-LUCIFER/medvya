"""Postgres job queue: claim with FOR UPDATE SKIP LOCKED, retry with exponential run_after.

processing_job holds no PHI and has no RLS, so the worker claims across tenants and then scopes
each unit of work with set_tenant(job.clinic_id). last_error stores a code, never provider text.
"""

import uuid
from dataclasses import dataclass
from datetime import timedelta
from typing import Any

from sqlalchemy import func, select, text, update
from sqlalchemy.dialects.postgresql import insert as pg_insert
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai.db import set_tenant, uuid7
from medvya_ai.models import ConsultationSession, ProcessingJob

STALE_LOCK_SECONDS = 15 * 60  # a worker that died mid-job: the row becomes claimable again
HEARTBEAT_SECONDS = 60  # a live worker refreshes locked_at, so a long job is never reclaimed
# Jobs that process consented audio: gated on consent, and their final failure fails the session.
SESSION_PIPELINE = frozenset({"transcribe", "normalise", "extract"})
# Each attempt already retries the provider 3 times; 3 attempts caps one job at 9 paid calls.
# shortcut: no per-clinic cost cap on model calls (only these attempt limits and the usage
# counters on each job); add a monthly per-clinic budget check before the pilot scales up.
MODEL_JOB_MAX_ATTEMPTS = 3


class JobCancelled(Exception):
    """The job's session was revoked (or its consent epoch is stale): discard, never retry."""


_CLAIM = text(
    """
    UPDATE processing_job SET
        status = 'running', locked_at = now(), locked_by = :worker,
        attempts = attempts + 1, updated_at = now()
    WHERE id = (
        SELECT id FROM processing_job
        WHERE (status = 'queued' AND run_after <= now())
           OR (status = 'running' AND locked_at < now() - make_interval(secs => :stale)
               AND attempts < max_attempts)
        ORDER BY run_after
        FOR UPDATE SKIP LOCKED
        LIMIT 1)
    RETURNING id, clinic_id, session_id, kind, attempts, max_attempts, consent_epoch
    """
)


@dataclass(frozen=True)
class Job:
    id: uuid.UUID
    clinic_id: uuid.UUID
    session_id: uuid.UUID | None
    kind: str
    attempts: int
    max_attempts: int
    consent_epoch: int = 0
    worker: str = ""  # locked_by at claim: every later write checks the row is still ours


def backoff_seconds(attempts: int) -> int:
    """Delay before the next attempt, given the attempts already made (1-based)."""
    return int(min(30 * 2 ** max(attempts - 1, 0), 3600))


async def enqueue(
    db: AsyncSession,
    *,
    clinic_id: uuid.UUID,
    session_id: uuid.UUID | None,
    kind: str,
    consent_epoch: int = 0,
) -> None:
    """Idempotent: the partial unique index (session_id, kind) on active jobs absorbs duplicates."""
    await db.execute(
        pg_insert(ProcessingJob)
        .values(
            id=uuid7(),
            clinic_id=clinic_id,
            session_id=session_id,
            kind=kind,
            consent_epoch=consent_epoch,
            **({"max_attempts": MODEL_JOB_MAX_ATTEMPTS} if kind in SESSION_PIPELINE else {}),
        )
        .on_conflict_do_nothing()
    )


async def cancel_active(db: AsyncSession, session_id: uuid.UUID) -> int:
    """Withdrawal: queued and running work of the session is cancelled in the same transaction.
    A running worker finds its row cancelled at its next guard or at complete()."""
    rows = (
        await db.execute(
            update(ProcessingJob)
            .where(
                ProcessingJob.session_id == session_id,
                ProcessingJob.status.in_(("queued", "running")),
            )
            .values(status="cancelled", locked_at=None, locked_by=None, updated_at=text("now()"))
            .returning(ProcessingJob.id)
        )
    ).all()
    return len(rows)


def processable(state: str, session_epoch: int, job_epoch: int) -> bool:
    """Pure gate run before any handler touches consented audio."""
    return state != "revoked" and session_epoch == job_epoch


async def ensure_processable(db: AsyncSession, job: Job) -> None:
    """Raises JobCancelled when the session was revoked or the job predates a withdrawal."""
    row = (
        await db.execute(
            select(ConsultationSession.state, ConsultationSession.consent_epoch).where(
                ConsultationSession.id == job.session_id
            )
        )
    ).first()
    if row is None or not processable(row.state, row.consent_epoch, job.consent_epoch):
        raise JobCancelled


def _ours(job: Job) -> Any:
    """The row is still running under this worker: not cancelled, not reclaimed by another."""
    return (
        (ProcessingJob.id == job.id)
        & (ProcessingJob.status == "running")
        & (ProcessingJob.locked_by == job.worker)
    )


async def ensure_active(db: AsyncSession, job: Job) -> None:
    """Cancellation token, checked right before every external provider call: a withdrawal
    committed since the claim has set the row to cancelled, or another worker reclaimed it."""
    if await db.scalar(select(ProcessingJob.id).where(_ours(job))) is None:
        raise JobCancelled


async def claim(db: AsyncSession, worker_id: str) -> Job | None:
    row = (await db.execute(_CLAIM, {"worker": worker_id, "stale": STALE_LOCK_SECONDS})).first()
    if row is None:
        return None
    return Job(
        row.id,
        row.clinic_id,
        row.session_id,
        row.kind,
        row.attempts,
        row.max_attempts,
        row.consent_epoch,
        worker_id,
    )


async def touch(db: AsyncSession, job: Job) -> None:
    await db.execute(update(ProcessingJob).where(_ours(job)).values(locked_at=text("now()")))


async def complete(db: AsyncSession, job: Job, usage: dict[str, Any] | None = None) -> None:
    """Atomic consent assertion: runs in the handler's transaction, so when a withdrawal has
    already cancelled the row, raising here rolls back everything the handler persisted.
    `usage` holds provider counters only (audio ms, tokens, model, region): never content."""
    done = (
        await db.execute(
            update(ProcessingJob)
            .where(_ours(job))
            .values(
                status="done",
                usage=usage or {},
                last_error=None,
                locked_at=None,
                locked_by=None,
                updated_at=text("now()"),
            )
            .returning(ProcessingJob.id)
        )
    ).first()
    if done is None:
        raise JobCancelled


async def mark_cancelled(db: AsyncSession, job: Job) -> None:
    await db.execute(
        update(ProcessingJob)
        .where(_ours(job))
        .values(status="cancelled", locked_at=None, locked_by=None, updated_at=text("now()"))
    )


async def fail(db: AsyncSession, job: Job, error: str, *, permanent: bool = False) -> str:
    """Requeue with backoff, or mark failed (and the session failed) when attempts are used up.
    Only a row still running under this worker changes: a cancelled job is never requeued and a
    reclaimed one belongs to the other worker. Returns the new job status, or "lost"."""
    final = permanent or job.attempts >= job.max_attempts
    values: dict[str, object] = {
        "last_error": error[:200],
        "locked_at": None,
        "locked_by": None,
        "updated_at": text("now()"),
    }
    if final:
        values["status"] = "failed"
    else:
        values["status"] = "queued"
        values["run_after"] = text(f"now() + interval '{backoff_seconds(job.attempts)} seconds'")
    changed = (
        await db.execute(
            update(ProcessingJob).where(_ours(job)).values(**values).returning(ProcessingJob.id)
        )
    ).first()
    if changed is None:
        return "lost"
    if final and job.session_id is not None and job.kind in SESSION_PIPELINE:
        await _fail_session(db, job.clinic_id, job.session_id)
    return "failed" if final else "queued"


async def _fail_session(db: AsyncSession, clinic_id: uuid.UUID, session_id: uuid.UUID) -> None:
    await set_tenant(db, clinic_id)
    await db.execute(
        update(ConsultationSession)
        .where(ConsultationSession.id == session_id, ConsultationSession.state != "revoked")
        .values(state="failed")
    )


async def fail_exhausted_stale(db: AsyncSession) -> int:
    """A job whose worker died on its last attempt is never reclaimed (see _CLAIM): mark it
    failed, and its session failed, instead of leaving it running forever."""
    rows = (
        await db.execute(
            update(ProcessingJob)
            .where(
                ProcessingJob.status == "running",
                ProcessingJob.locked_at < func.now() - timedelta(seconds=STALE_LOCK_SECONDS),
                ProcessingJob.attempts >= ProcessingJob.max_attempts,
            )
            .values(
                status="failed",
                last_error="WORKER_LOST",
                locked_at=None,
                locked_by=None,
                updated_at=text("now()"),
            )
            .returning(ProcessingJob.clinic_id, ProcessingJob.session_id, ProcessingJob.kind)
        )
    ).all()
    for clinic_id, session_id, kind in rows:
        if session_id is not None and kind in SESSION_PIPELINE:
            await _fail_session(db, clinic_id, session_id)
    return len(rows)

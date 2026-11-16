"""Job handlers. Each runs in the job's own transaction, enqueues its successor in it and
returns provider usage counters (stored on the job for cost accounting; never content)."""

import logging
import uuid
from collections.abc import Awaitable, Callable
from datetime import UTC, datetime
from functools import partial
from typing import Any

from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai.extraction.normalise import run_normalise
from medvya_ai.jobs.context import Deps
from medvya_ai.jobs.queue import Job, enqueue, ensure_active
from medvya_ai.models import RecordingSegment
from medvya_ai.providers.errors import PipelineError
from medvya_ai.providers.llm.base import Task, Usage
from medvya_ai.services import purge, retention
from medvya_ai.services.extract import run_extract
from medvya_ai.services.transcribe import run_transcribe

log = logging.getLogger(__name__)
Counters = dict[str, Any] | None
Handler = Callable[[AsyncSession, Deps, Job], Awaitable[Counters]]


def _sid(job: Job) -> uuid.UUID:
    if job.session_id is None:
        raise PipelineError("JOB_WITHOUT_SESSION")
    return job.session_id


def _llm_usage(deps: Deps, start: Usage, task: Task) -> dict[str, Any]:
    return {
        **deps.llm.usage.since(start),
        "provider": type(deps.llm).__name__,  # the adapter actually used
        "model": deps.llm.model_names.get(task),
        "endpoint": deps.settings.foundry_endpoint,
    }


async def transcribe(db: AsyncSession, deps: Deps, job: Job) -> Counters:
    sid = _sid(job)
    n = await run_transcribe(
        db,
        session_id=sid,
        blobs=deps.blobs,
        wrapper=deps.wrapper,
        transcriber=deps.transcriber,
        settings=deps.settings,
        before_call=partial(ensure_active, db, job),
    )
    log.info("transcribe done session=%s phrases=%d", sid, n)
    await enqueue(
        db,
        clinic_id=job.clinic_id,
        session_id=sid,
        kind="normalise",
        consent_epoch=job.consent_epoch,
    )
    audio_ms = await db.scalar(
        select(func.coalesce(func.sum(RecordingSegment.duration_ms), 0)).where(
            RecordingSegment.session_id == sid
        )
    )
    return {
        "audio_ms": int(audio_ms or 0),
        "phrases": n,
        "provider": type(deps.transcriber).__name__,
        "region": deps.settings.speech_region,
    }


async def normalise(db: AsyncSession, deps: Deps, job: Job) -> Counters:
    sid = _sid(job)
    start = deps.llm.usage.snapshot()
    n = await run_normalise(db, deps.llm, sid, before_call=partial(ensure_active, db, job))
    log.info("normalise done session=%s translated=%d", sid, n)
    await enqueue(
        db, clinic_id=job.clinic_id, session_id=sid, kind="extract", consent_epoch=job.consent_epoch
    )
    return _llm_usage(deps, start, "normalise")


async def extract(db: AsyncSession, deps: Deps, job: Job) -> Counters:
    sid = _sid(job)
    start = deps.llm.usage.snapshot()
    c = await run_extract(
        db,
        deps.llm,
        clinic_id=job.clinic_id,
        session_id=sid,
        before_call=partial(ensure_active, db, job),
    )
    log.info(
        "extract done session=%s facts=%d medications=%d rejected=%d",
        sid,
        c.facts,
        c.medications,
        c.rejected,
    )
    return {**_llm_usage(deps, start, "extract"), "facts": c.facts, "rejected": c.rejected}


async def retention_sweep(db: AsyncSession, deps: Deps, job: Job) -> Counters:
    n = await retention.sweep(
        db, deps.blobs, clinic_id=job.clinic_id, now=datetime.now(UTC), settings=deps.settings
    )
    log.info("retention sweep clinic=%s chunks_deleted=%d", job.clinic_id, n)
    return {"chunks_deleted": n}


async def purge_session(db: AsyncSession, deps: Deps, job: Job) -> Counters:
    counts = await purge.purge_session(
        db, deps.blobs, clinic_id=job.clinic_id, session_id=_sid(job), now=datetime.now(UTC)
    )
    log.info("purge done session=%s %s", job.session_id, counts)
    return dict(counts)


HANDLERS: dict[str, Handler] = {
    "transcribe": transcribe,
    "normalise": normalise,
    "extract": extract,
    "retention_sweep": retention_sweep,
    "purge_session": purge_session,
}

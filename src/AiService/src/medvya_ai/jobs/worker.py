"""Worker loop: claim, run in a tenant-scoped transaction, complete or retry. Graceful shutdown."""

import asyncio
import contextlib
import logging
import os
import signal
import socket
import time
import uuid
from collections.abc import Mapping

from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from medvya_ai.config import Settings, get_settings
from medvya_ai.crypto import LocalKeyWrapper
from medvya_ai.db import make_engine, make_sessionmaker, set_tenant
from medvya_ai.jobs import queue
from medvya_ai.jobs.context import Deps
from medvya_ai.jobs.handlers import HANDLERS, Handler
from medvya_ai.jobs.scheduler import RETENTION_INTERVAL_SECONDS, enqueue_retention_sweeps
from medvya_ai.providers.errors import ProviderError
from medvya_ai.providers.llm import build_llm
from medvya_ai.providers.transcription import build_transcriber
from medvya_ai.storage import AzureBlobStore
from medvya_ai.telemetry import configure_telemetry

log = logging.getLogger("medvya_ai.worker")
# Retrying these cannot help: same input, same deterministic refusal.
PERMANENT_ERRORS = frozenset({"LLM_CONTENT_FILTER", "LLM_LENGTH", "NO_HANDLER"})


def safe_error(exc: BaseException) -> str:
    """Stored in processing_job.last_error: a code or class name, never message text (PHI risk)."""
    return exc.code if isinstance(exc, ProviderError) else type(exc).__name__


async def heartbeat(
    sm: async_sessionmaker[AsyncSession], job_id: uuid.UUID, every: float = queue.HEARTBEAT_SECONDS
) -> None:
    """Runs beside a handler; cancelled when it finishes. A failed touch is retried next tick."""
    while True:
        await asyncio.sleep(every)
        try:
            async with sm() as db, db.begin():
                await queue.touch(db, job_id)
        except Exception:
            log.warning("heartbeat failed id=%s", job_id)


async def run_once(
    sm: async_sessionmaker[AsyncSession],
    deps: Deps,
    worker_id: str,
    handlers: Mapping[str, Handler] = HANDLERS,
) -> bool:
    """Claims and processes at most one job. Returns False when the queue had nothing due."""
    async with sm() as db, db.begin():
        job = await queue.claim(db, worker_id)
    if job is None:
        return False
    handler = handlers.get(job.kind)
    beat = asyncio.create_task(heartbeat(sm, job.id))
    try:
        if handler is None:
            raise ProviderError("NO_HANDLER")
        async with sm() as db, db.begin():
            await set_tenant(db, job.clinic_id)
            if job.kind in queue.SESSION_PIPELINE:
                await queue.ensure_processable(db, job)
            usage = await handler(db, deps, job)
            await queue.complete(db, job, usage)  # JobCancelled -> whole transaction rolls back
        beat.cancel()
        log.info("job done kind=%s id=%s attempt=%d", job.kind, job.id, job.attempts)
    except queue.JobCancelled:
        beat.cancel()
        async with sm() as db, db.begin():
            await queue.mark_cancelled(db, job)
        log.info("job cancelled kind=%s id=%s (consent revoked)", job.kind, job.id)
    except Exception as exc:  # worker must survive any single job
        beat.cancel()
        error = safe_error(exc)
        async with sm() as db, db.begin():
            status = await queue.fail(db, job, error, permanent=error in PERMANENT_ERRORS)
        log.warning(
            "job %s kind=%s id=%s attempt=%d error=%s",
            status,
            job.kind,
            job.id,
            job.attempts,
            error,
        )
    return True


async def run_forever(
    sm: async_sessionmaker[AsyncSession], deps: Deps, stop: asyncio.Event, worker_id: str
) -> None:
    poll = deps.settings.worker_poll_seconds
    next_sweep = 0.0
    while not stop.is_set():
        try:
            if time.monotonic() >= next_sweep:
                await enqueue_retention_sweeps(sm)
                next_sweep = time.monotonic() + RETENTION_INTERVAL_SECONDS
            worked = await run_once(sm, deps, worker_id)
        except Exception:  # DB briefly unreachable: back off, keep running
            log.exception("worker loop error")
            worked = False
        if not worked:
            with contextlib.suppress(TimeoutError):
                await asyncio.wait_for(stop.wait(), timeout=poll)
    log.info("worker stopped")


def build_deps(settings: Settings) -> Deps:
    conn = settings.azure_storage_connection_string
    if conn is None:
        raise RuntimeError("AZURE_STORAGE_CONNECTION_STRING is required")
    return Deps(
        settings=settings,
        blobs=AzureBlobStore(conn.get_secret_value(), settings.blob_container),
        wrapper=LocalKeyWrapper(settings.master_key_bytes),
        transcriber=build_transcriber(settings),
        llm=build_llm(settings),
    )


async def amain() -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")
    settings = get_settings()
    configure_telemetry(settings)
    deps = build_deps(settings)
    engine = make_engine(settings.database_url.get_secret_value())
    stop = asyncio.Event()
    loop = asyncio.get_running_loop()
    for sig in (signal.SIGINT, signal.SIGTERM):
        loop.add_signal_handler(sig, stop.set)  # finish the current job, then exit
    try:
        await run_forever(
            make_sessionmaker(engine), deps, stop, f"{socket.gethostname()}:{os.getpid()}"
        )
    finally:
        await engine.dispose()


def main() -> None:
    asyncio.run(amain())

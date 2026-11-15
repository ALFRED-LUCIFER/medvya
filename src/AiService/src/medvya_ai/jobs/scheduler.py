"""Periodic work: the worker enqueues one retention_sweep per clinic every hour."""

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from medvya_ai.jobs.queue import enqueue
from medvya_ai.models import Clinic

RETENTION_INTERVAL_SECONDS = 3600


async def enqueue_retention_sweeps(sm: async_sessionmaker[AsyncSession]) -> int:
    """clinic has no RLS, so the worker can list tenants. enqueue absorbs a still-active sweep
    through the partial unique index (clinic_id, kind) where session_id is NULL."""
    async with sm() as db, db.begin():
        ids = (await db.scalars(select(Clinic.id))).all()
        for cid in ids:
            await enqueue(db, clinic_id=cid, session_id=None, kind="retention_sweep")
    return len(ids)

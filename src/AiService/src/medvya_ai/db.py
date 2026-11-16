"""Engine, sessions and per-transaction tenant scoping."""

import secrets
import threading
import time
import uuid

from sqlalchemy import text
from sqlalchemy.ext.asyncio import (
    AsyncEngine,
    AsyncSession,
    async_sessionmaker,
    create_async_engine,
)

_lock = threading.Lock()
_last = (0, 0)


def uuid7() -> uuid.UUID:
    """Time-ordered UUID (RFC 9562 v7), monotonic within the process. 3.13 has no uuid.uuid7."""
    global _last
    with _lock:
        ms = time.time_ns() // 1_000_000
        last_ms, last_seq = _last
        if ms <= last_ms:
            ms, seq = last_ms, last_seq + 1
            if seq > 0xFFF:  # 4096 ids in one ms: borrow the next millisecond
                ms, seq = ms + 1, 0
        else:
            seq = secrets.randbits(11)  # leaves headroom for the counter
        _last = (ms, seq)
    rand_b = secrets.randbits(62)
    value = (ms << 80) | (0x7 << 76) | (seq << 64) | (0b10 << 62) | rand_b
    return uuid.UUID(int=value)


def make_engine(url: str) -> AsyncEngine:
    # hide_parameters: bound values (PHI) never appear in SQLAlchemy exception text or logs.
    return create_async_engine(url, pool_pre_ping=True, hide_parameters=True)


def make_sessionmaker(engine: AsyncEngine) -> async_sessionmaker[AsyncSession]:
    return async_sessionmaker(engine, expire_on_commit=False)


async def set_tenant(session: AsyncSession, clinic_id: uuid.UUID) -> None:
    """Scope the current transaction to one clinic. is_local=true: reverts at commit/rollback,
    so a pooled connection never carries a tenant into its next transaction."""
    await session.execute(
        text("SELECT set_config('app.clinic_id', :id, true)"), {"id": str(clinic_id)}
    )

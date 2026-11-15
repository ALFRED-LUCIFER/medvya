"""Audio retention. Only audio is removed; transcript, note and facts are never touched here.

medvya_ai_app has no DELETE grant, so DB rows are kept and audio_chunk.deleted_at is set.
Idempotent: a session with no live chunks is never selected, and a blob delete of a missing
blob is a no-op, so a crash between blob delete and the UPDATE is repaired by the next sweep.
"""

import uuid
from collections.abc import Sequence
from dataclasses import dataclass
from datetime import datetime, timedelta

from sqlalchemy import select, update
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai import audit
from medvya_ai.config import Settings
from medvya_ai.models import AudioChunk, ConsultationSession, RetentionPolicy
from medvya_ai.storage import BlobStore


@dataclass(frozen=True)
class Policy:
    audio_after_approval_hours: int
    draft_audio_days: int


@dataclass(frozen=True)
class SessionAge:
    id: uuid.UUID
    state: str
    created_at: datetime
    approved_at: datetime | None


def due(sessions: Sequence[SessionAge], now: datetime, policy: Policy) -> list[uuid.UUID]:
    """Sessions whose audio must be deleted at `now`. Boundaries are inclusive (>=)."""
    approved_after = timedelta(hours=policy.audio_after_approval_hours)
    draft_after = timedelta(days=policy.draft_audio_days)
    out: list[uuid.UUID] = []
    for s in sessions:
        if s.state == "approved" and s.approved_at is not None:
            if now - s.approved_at >= approved_after:
                out.append(s.id)
        elif s.state != "approved" and now - s.created_at >= draft_after:
            out.append(s.id)
    return out


def env_policy(settings: Settings) -> Policy:
    return Policy(
        settings.retention_audio_after_approval_hours, settings.retention_draft_audio_days
    )


async def sweep(
    db: AsyncSession, blobs: BlobStore, *, clinic_id: uuid.UUID, now: datetime, settings: Settings
) -> int:
    """Deletes due audio for one clinic. `db` must already be scoped with set_tenant.
    Returns the number of chunks deleted."""
    row = await db.get(RetentionPolicy, clinic_id)  # per-clinic override of the env defaults
    policy = (
        Policy(row.audio_after_approval_hours, row.draft_audio_days)
        if row
        else env_policy(settings)
    )
    live = select(AudioChunk.session_id).where(AudioChunk.deleted_at.is_(None))
    rows = (
        await db.scalars(select(ConsultationSession).where(ConsultationSession.id.in_(live)))
    ).all()
    ages = [SessionAge(s.id, s.state, s.created_at, s.approved_at) for s in rows]
    total = 0
    for sid in due(ages, now, policy):
        n = await delete_audio(db, blobs, session_id=sid, now=now)
        await audit.record(
            db,
            clinic_id=clinic_id,
            actor_user_id=None,
            action="audio_deleted",
            entity_type="consultation_session",
            entity_id=sid,
            ip=None,
            metadata={"chunks": n},
        )
        total += n
    return total


async def delete_audio(
    db: AsyncSession, blobs: BlobStore, *, session_id: uuid.UUID, now: datetime
) -> int:
    """Blob delete first, then deleted_at: a crash in between is repaired by the next run."""
    chunks = (
        await db.scalars(
            select(AudioChunk)
            .where(AudioChunk.session_id == session_id, AudioChunk.deleted_at.is_(None))
            .order_by(AudioChunk.seq)
        )
    ).all()
    for c in chunks:
        await blobs.delete(c.blob_path)
    if chunks:
        await db.execute(
            update(AudioChunk)
            .where(AudioChunk.id.in_([c.id for c in chunks]))
            .values(deleted_at=now)
        )
    return len(chunks)

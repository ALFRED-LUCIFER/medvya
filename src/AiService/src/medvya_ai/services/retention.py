"""Retention. sweep() deletes due audio (audio_chunk rows keep deleted_at); a blob delete of a
missing blob is a no-op, so a crash between blob delete and the UPDATE is repaired by the next
sweep. expire_sessions() hands sessions past RETENTION_SESSION_DAYS to the purge job, which
deletes the working data: Medvya, not this service, keeps the signed record (ADR-021 s2).
"""

import uuid
from collections.abc import Sequence
from dataclasses import dataclass
from datetime import datetime, timedelta

from sqlalchemy import func, select, update
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai import audit
from medvya_ai.config import Settings
from medvya_ai.models import AudioChunk, ConsultationSession, RetentionPolicy
from medvya_ai.services.consent import revoke
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


async def expire_sessions(
    db: AsyncSession, *, clinic_id: uuid.UUID, now: datetime, settings: Settings
) -> int:
    """Sessions finished (or, never recorded, created) RETENTION_SESSION_DAYS ago go through the
    same revoke -> purge_session path as a withdrawal: jobs cancelled, working data deleted,
    consent log and audit kept. `db` must already be scoped with set_tenant."""
    cutoff = now - timedelta(days=settings.retention_session_days)
    rows = (
        await db.scalars(
            select(ConsultationSession)
            .where(
                ConsultationSession.revoked_at.is_(None),
                func.coalesce(ConsultationSession.finished_at, ConsultationSession.created_at)
                <= cutoff,
            )
            .with_for_update(skip_locked=True)
        )
    ).all()
    for s in rows:
        await revoke(db, s)
        await audit.record(
            db,
            clinic_id=clinic_id,
            actor_user_id=None,
            action="session_retention_expired",
            entity_type="consultation_session",
            entity_id=s.id,
            ip=None,
            metadata={"days": settings.retention_session_days},
        )
    return len(rows)


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

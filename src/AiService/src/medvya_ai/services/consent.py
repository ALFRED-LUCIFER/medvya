"""Consent decision (pure) and the DB operations that keep session flags in step."""

import uuid
from collections.abc import Collection, Mapping
from datetime import UTC, datetime
from enum import StrEnum
from importlib import resources

from sqlalchemy import select, text
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai.errors import AppError
from medvya_ai.jobs.queue import cancel_active, enqueue
from medvya_ai.models import ConsentRecord, ConsultationSession, Participant

NOTICE_VERSIONS = {"en-v1": "en"}


class Decision(StrEnum):
    ALLOWED = "allowed"
    REQUIRED = "CONSENT_REQUIRED"
    WITHDRAWN = "CONSENT_WITHDRAWN"


def decide(participants: Collection[uuid.UUID], latest: Mapping[uuid.UUID, str]) -> Decision:
    """Pure gate. Any latest 'withdrawn' wins; otherwise every participant needs latest 'granted'
    (no row or 'refused' means required). No participants means required."""
    if not participants:
        return Decision.REQUIRED
    statuses = [latest.get(p) for p in participants]
    if "withdrawn" in statuses:
        return Decision.WITHDRAWN
    return Decision.ALLOWED if all(s == "granted" for s in statuses) else Decision.REQUIRED


def notice_text(version: str) -> str:
    return (
        (resources.files("medvya_ai") / "consent_notices" / f"{version}.txt")
        .read_text("utf-8")
        .strip()
    )


async def load_decision(db: AsyncSession, session_id: uuid.UUID) -> Decision:
    ids = list(
        (await db.scalars(select(Participant.id).where(Participant.session_id == session_id))).all()
    )
    rows = await db.execute(
        text("SELECT participant_id, status FROM v_current_consent WHERE session_id = :s"),
        {"s": session_id},
    )
    return decide(ids, {r[0]: r[1] for r in rows})


async def lock_session(db: AsyncSession, session_id: uuid.UUID) -> ConsultationSession:
    s = await db.scalar(
        select(ConsultationSession).where(ConsultationSession.id == session_id).with_for_update()
    )
    if s is None:
        raise AppError(404, "NOT_FOUND", "Session not found")
    return s


async def refresh_flags(db: AsyncSession, s: ConsultationSession) -> Decision:
    """Recompute recording_allowed and the pre-recording state from the consent log."""
    decision = await load_decision(db, s.id)
    s.recording_allowed = decision is Decision.ALLOWED
    if s.state in ("created", "ready"):
        rows = await db.execute(
            text("SELECT status FROM v_current_consent WHERE session_id = :s"), {"s": s.id}
        )
        statuses = [r[0] for r in rows]
        n = len(
            (await db.scalars(select(Participant.id).where(Participant.session_id == s.id))).all()
        )
        if n and len(statuses) == n and all(x == "refused" for x in statuses):
            s.state = "closed_no_consent"
        else:
            s.state = "ready" if s.recording_allowed else "created"
    await db.flush()
    return decision


async def add_participant(
    db: AsyncSession, s: ConsultationSession, role: str, name: str
) -> Participant:
    count = len(
        (await db.scalars(select(Participant.id).where(Participant.session_id == s.id))).all()
    )
    if count >= 3:
        raise AppError(409, "PARTICIPANT_LIMIT", "A session has at most 3 participants")
    if s.state in ("processing", "review", "approved", "failed", "closed_no_consent"):
        raise AppError(409, "SESSION_STATE", "Participants can no longer be added")
    p = Participant(clinic_id=s.clinic_id, session_id=s.id, role=role, display_name=name)
    db.add(p)
    await db.flush()
    await refresh_flags(db, s)
    return p


async def record_consent(
    db: AsyncSession,
    s: ConsultationSession,
    *,
    participant_id: uuid.UUID,
    status: str,
    method: str,
    on_behalf_of: str,
    notice_version: str,
    attested_by: uuid.UUID,
    representative_name: str | None = None,
    representative_relationship: str | None = None,
) -> ConsentRecord:
    if notice_version not in NOTICE_VERSIONS:
        raise AppError(422, "UNKNOWN_NOTICE_VERSION", "Unknown consent notice version")
    if status != "withdrawn" and s.state not in ("created", "ready", "recording"):
        raise AppError(409, "SESSION_STATE", "Consent can only be withdrawn at this stage")
    owns = await db.scalar(
        select(Participant.id).where(
            Participant.id == participant_id, Participant.session_id == s.id
        )
    )
    if owns is None:
        raise AppError(404, "NOT_FOUND", "Participant not found in session")
    rec = ConsentRecord(
        clinic_id=s.clinic_id,
        session_id=s.id,
        participant_id=participant_id,
        status=status,
        method=method,
        on_behalf_of=on_behalf_of,
        attested_by_user_id=attested_by,
        notice_version=notice_version,
        notice_locale=NOTICE_VERSIONS[notice_version],
        representative_name=representative_name,
        representative_relationship=representative_relationship,
    )
    db.add(rec)
    await db.flush()
    await db.refresh(rec)
    await refresh_flags(db, s)
    if status == "withdrawn":
        await revoke(db, s)
    return rec


async def revoke(db: AsyncSession, s: ConsultationSession) -> None:
    """WITHDRAW (ADR 0001). Caller holds the session row lock. In this one transaction: bump the
    consent epoch, stop capture, cancel queued/running jobs and enqueue the purge (outbox).
    An approved record keeps its state and follows record retention; only its audio is purged.
    Idempotent: a second withdrawal only appends to the consent log."""
    if s.revoked_at is not None:
        return
    s.consent_epoch += 1
    s.revoked_at = datetime.now(UTC)
    s.recording_allowed = False
    if s.state != "approved":
        s.state = "revoked"
    await db.flush()
    await cancel_active(db, s.id)
    await enqueue(db, clinic_id=s.clinic_id, session_id=s.id, kind="purge_session")

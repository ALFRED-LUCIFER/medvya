"""Purge of a revoked session (outbox job enqueued by consent.revoke).

Audio is always purged (blob + deleted_at). Derived artifacts (transcript, note, revisions,
facts, medications, evidence, rejections) are deleted only while the session is `revoked`,
i.e. never approved; an approved record is kept under record retention (ADR 0001).
Consent records and audit events are never touched. Idempotent: reruns find nothing.
"""

import uuid
from datetime import datetime
from typing import Any, cast

from sqlalchemy import CursorResult, delete, select
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai import audit
from medvya_ai.models import (
    ClinicalFact,
    ClinicalNote,
    ConsultationSession,
    ExtractionRejection,
    FactEvidence,
    Medication,
    NoteRevision,
    TranscriptCorrection,
    TranscriptSegment,
)
from medvya_ai.providers.errors import PipelineError
from medvya_ai.services.retention import delete_audio
from medvya_ai.storage import BlobStore


async def purge_session(
    db: AsyncSession,
    blobs: BlobStore,
    *,
    clinic_id: uuid.UUID,
    session_id: uuid.UUID,
    now: datetime,
) -> dict[str, int]:
    s = await db.get(ConsultationSession, session_id)
    if s is None or s.revoked_at is None:
        raise PipelineError("SESSION_NOT_REVOKED")
    counts = {"chunks": await delete_audio(db, blobs, session_id=session_id, now=now)}
    if s.state == "revoked":
        facts = select(ClinicalFact.id).where(ClinicalFact.session_id == session_id)
        notes = select(ClinicalNote.id).where(ClinicalNote.session_id == session_id)
        for name, stmt in (
            ("evidence", delete(FactEvidence).where(FactEvidence.fact_id.in_(facts))),
            ("medications", delete(Medication).where(Medication.fact_id.in_(facts))),
            ("facts", delete(ClinicalFact).where(ClinicalFact.session_id == session_id)),
            (
                "rejections",
                delete(ExtractionRejection).where(ExtractionRejection.session_id == session_id),
            ),
            (
                "corrections",
                delete(TranscriptCorrection).where(TranscriptCorrection.session_id == session_id),
            ),
            (
                "transcript",
                delete(TranscriptSegment).where(TranscriptSegment.session_id == session_id),
            ),
            ("revisions", delete(NoteRevision).where(NoteRevision.note_id.in_(notes))),
            ("notes", delete(ClinicalNote).where(ClinicalNote.session_id == session_id)),
        ):
            counts[name] = cast(CursorResult[Any], await db.execute(stmt)).rowcount
    if any(counts.values()):
        await audit.record(
            db,
            clinic_id=clinic_id,
            actor_user_id=None,
            action="session_purged",
            entity_type="consultation_session",
            entity_id=session_id,
            ip=None,
            metadata=counts,
        )
    return counts

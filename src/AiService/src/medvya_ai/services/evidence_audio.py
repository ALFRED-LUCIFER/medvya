"""Authorized replay of the audio behind a fact. The caller is tenant-scoped (RLS) and the
response is streamed through the API: no blob URLs or SAS tokens ever leave the server.

shortcut: returns the whole recording segment that contains the evidence turn (the client seeks to
X-Evidence-Start-Ms); cutting an Opus/WebM excerpt needs a decoder (ffmpeg) the image does not ship.
Upgrade to a bounded clip when replay traffic or segment length makes this too heavy."""

import uuid
from dataclasses import dataclass

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai import crypto
from medvya_ai.errors import AppError
from medvya_ai.models import (
    AudioChunk,
    ClinicalFact,
    ConsultationSession,
    FactEvidence,
    RecordingSegment,
    TranscriptSegment,
)
from medvya_ai.services.note import ensure_ready, get_session
from medvya_ai.services.transcribe import assemble_audio
from medvya_ai.storage import BlobStore


@dataclass(frozen=True)
class Clip:
    audio: bytes
    mime_type: str
    start_ms: int  # evidence turn position inside `audio`
    end_ms: int


async def evidence_clip(
    db: AsyncSession,
    blobs: BlobStore,
    wrapper: crypto.KeyWrapper,
    *,
    session_id: uuid.UUID,
    fact_id: uuid.UUID,
) -> Clip:
    s: ConsultationSession = await get_session(db, session_id)
    ensure_ready(s)
    fact = await db.scalar(
        select(ClinicalFact).where(ClinicalFact.id == fact_id, ClinicalFact.session_id == s.id)
    )
    if fact is None:
        raise AppError(404, "NOT_FOUND", "Fact not found")
    turn = await db.scalar(
        select(TranscriptSegment)
        .join(FactEvidence, FactEvidence.segment_id == TranscriptSegment.id)
        .where(FactEvidence.fact_id == fact.id)
        .order_by(TranscriptSegment.start_ms)
        .limit(1)
    )
    if turn is None:
        raise AppError(404, "NOT_FOUND", "The fact has no transcript evidence")
    seg = await db.get(RecordingSegment, turn.recording_segment_id)
    if seg is None:
        raise AppError(404, "NOT_FOUND", "Recording segment not found")
    live = await db.scalar(
        select(AudioChunk.id)
        .where(AudioChunk.segment_id == seg.id, AudioChunk.deleted_at.is_(None))
        .limit(1)
    )
    if live is None:  # retention or withdrawal removed it: say so, never invent audio
        raise AppError(410, "AUDIO_NOT_AVAILABLE", "The audio has been deleted under retention")
    dek = wrapper.unwrap(s.dek_wrapped, s.id)
    audio = await assemble_audio(db, blobs, dek, s.id, seg)
    return Clip(
        audio=audio,
        mime_type=seg.mime_type,
        start_ms=max(turn.start_ms - seg.start_offset_ms, 0),
        end_ms=max(turn.end_ms - seg.start_offset_ms, 0),
    )

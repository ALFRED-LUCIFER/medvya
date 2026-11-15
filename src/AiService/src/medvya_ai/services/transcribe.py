"""Transcribe job: decrypt and concatenate chunks per segment, one provider call per segment,
shift phrase offsets onto the session timeline, upsert transcript rows (idempotent on retry)."""

import uuid
from collections.abc import Awaitable, Callable, Sequence
from dataclasses import dataclass

from sqlalchemy import select
from sqlalchemy.dialects.postgresql import insert as pg_insert
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai import crypto
from medvya_ai.config import Settings
from medvya_ai.db import uuid7
from medvya_ai.models import (
    AudioChunk,
    ConsultationSession,
    RecordingSegment,
    TranscriptSegment,
)
from medvya_ai.providers.errors import PipelineError
from medvya_ai.providers.transcription.base import Phrase, Transcriber, speaker_label
from medvya_ai.storage import BlobStore

_INSERT_BATCH = 500  # 11 bind params per row, well under asyncpg's 32767 limit


async def _noop() -> None:
    return None


@dataclass(frozen=True)
class TimelinePhrase:
    recording_segment_id: uuid.UUID
    start_ms: int
    end_ms: int
    phrase: Phrase


def to_timeline(
    segment_id: uuid.UUID, segment_start_ms: int, phrases: Sequence[Phrase]
) -> list[TimelinePhrase]:
    """Segment-relative offsets -> session timeline (segment.start_offset_ms applied)."""
    return [
        TimelinePhrase(
            segment_id,
            segment_start_ms + p.offset_ms,
            segment_start_ms + p.offset_ms + max(p.duration_ms, 0),
            p,
        )
        for p in phrases
    ]


async def assemble_audio(
    db: AsyncSession,
    blobs: BlobStore,
    dek: bytes,
    session_id: uuid.UUID,
    seg: RecordingSegment,
) -> bytes:
    chunks = (
        await db.scalars(
            select(AudioChunk)
            .where(AudioChunk.segment_id == seg.id, AudioChunk.deleted_at.is_(None))
            .order_by(AudioChunk.seq)
        )
    ).all()
    if not chunks or (seg.chunk_count is not None and len(chunks) != seg.chunk_count):
        raise PipelineError("AUDIO_INCOMPLETE")
    parts: list[bytes] = []
    for c in chunks:
        cipher = await blobs.get(c.blob_path)
        plain = crypto.decrypt_chunk(
            dek, c.nonce, cipher, crypto.chunk_aad(session_id, seg.segment_no, c.seq)
        )
        if crypto.sha256_hex(plain) != c.sha256:
            raise PipelineError("AUDIO_INTEGRITY")
        parts.append(plain)
    return b"".join(parts)


async def run_transcribe(
    db: AsyncSession,
    *,
    session_id: uuid.UUID,
    blobs: BlobStore,
    wrapper: crypto.KeyWrapper,
    transcriber: Transcriber,
    settings: Settings,
    before_call: Callable[[], Awaitable[None]] = _noop,
) -> int:
    """Returns the number of transcript rows written. `before_call` runs right before every
    provider call (consent cancellation token); audio already sent cannot be unsent."""
    session = await db.get(ConsultationSession, session_id)
    if session is None:
        raise PipelineError("SESSION_NOT_FOUND")
    dek = wrapper.unwrap(session.dek_wrapped, session.id)
    segments = (
        await db.scalars(
            select(RecordingSegment)
            .where(RecordingSegment.session_id == session_id)
            .order_by(RecordingSegment.segment_no)
        )
    ).all()
    if not segments:
        raise PipelineError("NO_SEGMENTS")
    locales = [x.strip() for x in settings.speech_locales.split(",") if x.strip()]
    timeline: list[TimelinePhrase] = []
    for seg in segments:
        audio = await assemble_audio(db, blobs, dek, session_id, seg)
        await before_call()
        phrases = await transcriber.transcribe(
            audio,
            mime_type=seg.mime_type,
            locales=locales,
            max_speakers=settings.speech_max_speakers,
        )
        timeline += to_timeline(seg.id, seg.start_offset_ms, phrases)
    timeline.sort(key=lambda t: t.start_ms)
    parts = {seg.id: seg.segment_no + 1 for seg in segments} if len(segments) > 1 else {}
    rows = [
        {
            "id": uuid7(),
            "clinic_id": session.clinic_id,
            "session_id": session_id,
            "recording_segment_id": t.recording_segment_id,
            "idx": i,
            "speaker_label": speaker_label(
                t.phrase.speaker, part=parts.get(t.recording_segment_id)
            ),
            "original_text": t.phrase.text,
            "language": t.phrase.locale or "und",
            "english_text": None,
            "start_ms": t.start_ms,
            "end_ms": t.end_ms,
            "confidence": t.phrase.confidence,
        }
        for i, t in enumerate(timeline)
    ]
    for start in range(0, len(rows), _INSERT_BATCH):
        ins = pg_insert(TranscriptSegment).values(rows[start : start + _INSERT_BATCH])
        await db.execute(
            ins.on_conflict_do_update(
                constraint="uq_transcript_session_idx",
                set_={
                    c: getattr(ins.excluded, c)
                    for c in (
                        "recording_segment_id",
                        "speaker_label",
                        "original_text",
                        "language",
                        "english_text",
                        "start_ms",
                        "end_ms",
                        "confidence",
                    )
                },
            )
        )
    return len(rows)

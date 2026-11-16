"""Segment and chunk ingest: consent gate in the insert statement, encrypted, idempotent."""

import uuid
from collections.abc import AsyncIterator
from datetime import UTC, datetime
from typing import Any

from sqlalchemy import select, text
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai import crypto
from medvya_ai.db import uuid7
from medvya_ai.errors import AppError
from medvya_ai.jobs.queue import enqueue
from medvya_ai.models import (
    AudioChunk,
    ConsultationSession,
    RecordingSegment,
)
from medvya_ai.schemas import SegmentComplete, SegmentIn
from medvya_ai.services.consent import Decision, load_decision
from medvya_ai.storage import BlobStore

RECORDING_STATES = ("ready", "recording")

# Consent is evaluated inside the INSERT: a withdrawal committed before this statement runs can
# never be followed by a stored chunk. Zero rows back means "not inserted" (duplicate or gate).
_GUARDED_INSERT = text(
    """
    INSERT INTO audio_chunk
        (id, clinic_id, session_id, segment_id, seq, size_bytes, sha256, blob_path, nonce)
    SELECT :id, s.clinic_id, s.id, :segment_id, :seq, :size, :sha, :path, :nonce
    FROM consultation_session s
    WHERE s.id = :session_id
      AND s.recording_allowed
      AND s.state IN ('ready', 'recording')
      AND EXISTS (SELECT 1 FROM participant p WHERE p.session_id = s.id)
      AND NOT EXISTS (
          SELECT 1 FROM participant p
          LEFT JOIN v_current_consent c ON c.participant_id = p.id
          WHERE p.session_id = s.id AND c.status IS DISTINCT FROM 'granted')
    ON CONFLICT (segment_id, seq) DO NOTHING
    RETURNING id
    """
)


async def _session(
    db: AsyncSession, session_id: uuid.UUID, *, lock: bool = False
) -> ConsultationSession:
    q = select(ConsultationSession).where(ConsultationSession.id == session_id)
    s = await db.scalar(q.with_for_update() if lock else q)
    if s is None:
        raise AppError(404, "NOT_FOUND", "Session not found")
    return s


async def _segment(db: AsyncSession, session_id: uuid.UUID, segment_no: int) -> RecordingSegment:
    seg = await db.scalar(
        select(RecordingSegment).where(
            RecordingSegment.session_id == session_id, RecordingSegment.segment_no == segment_no
        )
    )
    if seg is None:
        raise AppError(404, "NOT_FOUND", "Segment not found")
    return seg


def _gate_error(decision: Decision) -> AppError:
    msg = (
        "Consent was withdrawn; recording must stop"
        if decision is Decision.WITHDRAWN
        else "Every participant must consent before recording"
    )
    return AppError(403, decision.value, msg)


async def create_segment(
    db: AsyncSession, session_id: uuid.UUID, body: SegmentIn
) -> RecordingSegment:
    s = await _session(db, session_id, lock=True)
    existing = await db.scalar(
        select(RecordingSegment).where(
            RecordingSegment.session_id == session_id,
            RecordingSegment.segment_no == body.segment_no,
        )
    )
    if existing is not None:  # idempotent on segment_no
        return existing
    decision = await load_decision(db, session_id)
    if decision is not Decision.ALLOWED:
        raise _gate_error(decision)
    if s.state not in RECORDING_STATES:
        raise AppError(409, "SESSION_NOT_RECORDING", "Session is not accepting recordings")
    seg = RecordingSegment(
        clinic_id=s.clinic_id,
        session_id=s.id,
        segment_no=body.segment_no,
        mime_type=body.mime_type,
        start_offset_ms=body.start_offset_ms,
    )
    db.add(seg)
    s.state = "recording"
    await db.flush()
    return seg


async def read_capped(stream: AsyncIterator[bytes], limit: int) -> bytes:
    buf = bytearray()
    async for part in stream:
        buf += part
        if len(buf) > limit:
            raise AppError(413, "CHUNK_TOO_LARGE", f"Chunk exceeds {limit} bytes")
    return bytes(buf)


async def precheck(db: AsyncSession, session_id: uuid.UUID) -> None:
    """Cheap early 403 before the body is read; the guarded INSERT remains authoritative."""
    await _session(db, session_id)
    decision = await load_decision(db, session_id)
    if decision is not Decision.ALLOWED:
        raise _gate_error(decision)


async def store_chunk(
    db: AsyncSession,
    blobs: BlobStore,
    wrapper: crypto.KeyWrapper,
    *,
    session_id: uuid.UUID,
    segment_no: int,
    seq: int,
    data: bytes,
    claimed_sha: str,
    max_chunks: int,
    max_bytes: int,
) -> str:
    actual = crypto.sha256_hex(data)
    if actual != claimed_sha:
        raise AppError(400, "CHUNK_HASH_MISMATCH", "X-Chunk-Sha256 does not match the body")
    s = await _session(db, session_id)
    seg = await _segment(db, session_id, segment_no)
    if seg.state == "complete":
        raise AppError(409, "SEGMENT_COMPLETE", "Segment is already complete")
    existing = await db.scalar(
        select(AudioChunk).where(AudioChunk.segment_id == seg.id, AudioChunk.seq == seq)
    )
    if existing is not None:
        if existing.sha256 == actual:
            return "duplicate"
        raise AppError(409, "CHUNK_CONFLICT", "Different content already stored for this chunk")

    used = (
        await db.execute(
            text(
                "SELECT count(*), coalesce(sum(size_bytes), 0) FROM audio_chunk"
                " WHERE session_id = :s"
            ),
            {"s": session_id},
        )
    ).one()
    if used[0] >= max_chunks or int(used[1]) + len(data) > max_bytes:
        raise AppError(413, "SESSION_QUOTA_EXCEEDED", "Session audio limit reached")
    dek = wrapper.unwrap(s.dek_wrapped, s.id)
    nonce, ciphertext = crypto.encrypt_chunk(dek, data, crypto.chunk_aad(s.id, segment_no, seq))
    # Nonce in the path: a racing writer of the same key never overwrites the winner's blob.
    path = f"{s.clinic_id}/{s.id}/{segment_no}/{seq}-{nonce.hex()}.bin"
    await blobs.put(path, ciphertext)
    try:
        row = (
            await db.execute(
                _GUARDED_INSERT,
                {
                    "id": uuid7(),
                    "segment_id": seg.id,
                    "seq": seq,
                    "size": len(data),
                    "sha": actual,
                    "path": path,
                    "nonce": nonce,
                    "session_id": session_id,
                },
            )
        ).first()
    except Exception:
        await blobs.delete(path)
        raise
    if row is not None:
        return "stored"
    await blobs.delete(path)  # nothing may remain in Blob for a rejected chunk
    winner = await db.scalar(
        select(AudioChunk).where(AudioChunk.segment_id == seg.id, AudioChunk.seq == seq)
    )
    if winner is not None:
        if winner.sha256 == actual:
            return "duplicate"
        raise AppError(409, "CHUNK_CONFLICT", "Different content already stored for this chunk")
    decision = await load_decision(db, session_id)
    if decision is not Decision.ALLOWED:
        raise _gate_error(decision)
    raise AppError(409, "SESSION_NOT_RECORDING", "Session is not accepting recordings")


async def complete_segment(
    db: AsyncSession, session_id: uuid.UUID, segment_no: int, body: SegmentComplete
) -> tuple[RecordingSegment, dict[str, Any]]:
    """Returns the segment and summary counts for the audit trail. Allowed after a consent
    withdrawal so audio already uploaded can be closed out."""
    await _session(db, session_id, lock=True)
    seg = await _segment(db, session_id, segment_no)
    seqs = set(
        (await db.scalars(select(AudioChunk.seq).where(AudioChunk.segment_id == seg.id))).all()
    )
    missing = [i for i in range(body.chunk_count) if i not in seqs]
    if missing:
        raise AppError(409, "MISSING_CHUNKS", "Chunks are missing", missing=missing)
    extra = sorted(q for q in seqs if q >= body.chunk_count)
    if extra:
        raise AppError(409, "CHUNK_COUNT_MISMATCH", "Chunks beyond chunk_count exist", extra=extra)
    size = (
        await db.execute(
            text("SELECT coalesce(sum(size_bytes), 0) FROM audio_chunk WHERE segment_id = :i"),
            {"i": seg.id},
        )
    ).scalar_one()
    seg.chunk_count, seg.duration_ms, seg.state = body.chunk_count, body.duration_ms, "complete"
    seg.client_events = [e.model_dump() for e in body.client_events]
    seg.completed_at = datetime.now(UTC)
    await db.flush()
    return seg, {
        "segment_no": segment_no,
        "chunk_count": body.chunk_count,
        "bytes": int(size),
        "client_events": len(body.client_events),
    }


async def finish_session(db: AsyncSession, session_id: uuid.UUID) -> ConsultationSession:
    s = await _session(db, session_id, lock=True)
    if s.state == "processing":
        return s  # idempotent
    if s.state == "revoked":
        raise AppError(409, Decision.WITHDRAWN.value, "Consent was withdrawn; nothing to process")
    if s.state not in RECORDING_STATES:
        raise AppError(409, "SESSION_STATE", "Session cannot be finished from its current state")
    segs = (
        await db.scalars(select(RecordingSegment).where(RecordingSegment.session_id == s.id))
    ).all()
    open_ = [x.segment_no for x in segs if x.state != "complete"]
    if not segs or open_:
        raise AppError(409, "SEGMENTS_INCOMPLETE", "All segments must be complete", segments=open_)
    s.state, s.finished_at = "processing", datetime.now(UTC)
    # Transactional enqueue: job and state change commit together.
    await enqueue(
        db, clinic_id=s.clinic_id, session_id=s.id, kind="transcribe", consent_epoch=s.consent_epoch
    )
    await db.flush()
    return s

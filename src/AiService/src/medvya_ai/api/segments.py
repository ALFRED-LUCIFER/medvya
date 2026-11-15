import re
import uuid
from typing import Annotated

from fastapi import APIRouter, Header, Path, Request

from medvya_ai.deps import BlobsDep, SettingsDep, TenantDep, WrapperDep
from medvya_ai.errors import AppError
from medvya_ai.schemas import (
    MAX_INDEX,
    ChunkOut,
    FinishOut,
    SegmentComplete,
    SegmentIn,
    SegmentOut,
)
from medvya_ai.services import ingest

router = APIRouter(prefix="/sessions/{session_id}", tags=["ingest"])
_SHA = re.compile(r"^[0-9a-f]{64}$")
Index = Annotated[int, Path(ge=0, le=MAX_INDEX)]


def _out(seg: object) -> SegmentOut:
    return SegmentOut.model_validate(seg, from_attributes=True)


@router.post("/segments", response_model=SegmentOut)
async def create_segment(session_id: uuid.UUID, body: SegmentIn, t: TenantDep) -> SegmentOut:
    return _out(await ingest.create_segment(t.db, session_id, body))


@router.put("/segments/{segment_no}/chunks/{seq}", response_model=ChunkOut)
async def put_chunk(
    session_id: uuid.UUID,
    segment_no: Index,
    seq: Index,
    request: Request,
    t: TenantDep,
    blobs: BlobsDep,
    wrapper: WrapperDep,
    settings: SettingsDep,
    x_chunk_sha256: Annotated[str | None, Header()] = None,
) -> ChunkOut:
    sha = (x_chunk_sha256 or "").lower()
    if not _SHA.match(sha):
        raise AppError(400, "CHUNK_HASH_REQUIRED", "X-Chunk-Sha256 must be 64 hex characters")
    if not (request.headers.get("content-type", "").startswith("audio/")):
        raise AppError(415, "UNSUPPORTED_MEDIA_TYPE", "Content-Type must be audio/*")
    declared = request.headers.get("content-length")
    if declared and declared.isdigit() and int(declared) > settings.max_chunk_bytes:
        raise AppError(413, "CHUNK_TOO_LARGE", "Chunk too large")
    await ingest.precheck(t.db, session_id)
    data = await ingest.read_capped(request.stream(), settings.max_chunk_bytes)
    status = await ingest.store_chunk(
        t.db,
        blobs,
        wrapper,
        session_id=session_id,
        segment_no=segment_no,
        seq=seq,
        data=data,
        claimed_sha=sha,
        max_chunks=settings.max_session_chunks,
        max_bytes=settings.max_session_bytes,
    )
    return ChunkOut(status="stored" if status == "stored" else "duplicate")


@router.post("/segments/{segment_no}/complete", response_model=SegmentOut)
async def complete_segment(
    session_id: uuid.UUID, segment_no: Index, body: SegmentComplete, t: TenantDep
) -> SegmentOut:
    seg, summary = await ingest.complete_segment(t.db, session_id, segment_no, body)
    await t.audit("chunks_uploaded", "recording_segment", seg.id, summary)
    return _out(seg)


@router.post("/finish", response_model=FinishOut, status_code=202)
async def finish(session_id: uuid.UUID, t: TenantDep) -> FinishOut:
    s = await ingest.finish_session(t.db, session_id)
    await t.audit("session_finish", "consultation_session", s.id)
    return FinishOut(state=s.state)

import uuid

from fastapi import APIRouter, Response

from medvya_ai.deps import BlobsDep, TenantDep, WrapperDep
from medvya_ai.schemas import ResultOut
from medvya_ai.services import note as note_svc
from medvya_ai.services.evidence_audio import evidence_clip

router = APIRouter(prefix="/sessions", tags=["review"])


@router.get("/{session_id}/result", response_model=ResultOut)
async def get_result(session_id: uuid.UUID, t: TenantDep) -> ResultOut:
    out = await note_svc.load_result(t.db, session_id)
    await t.audit("result_view", "consultation_session", session_id, {"label": out.label})
    return out


@router.get(
    "/{session_id}/facts/{fact_id}/evidence-audio",
    responses={200: {"content": {"audio/*": {}}}, 410: {"description": "audio deleted"}},
)
async def evidence_audio(
    session_id: uuid.UUID, fact_id: uuid.UUID, t: TenantDep, blobs: BlobsDep, wrapper: WrapperDep
) -> Response:
    """Decrypted audio of the recording part holding the fact's evidence; the turn position is in
    X-Evidence-Start-Ms / X-Evidence-End-Ms. 410 AUDIO_NOT_AVAILABLE once the audio is deleted."""
    clip = await evidence_clip(t.db, blobs, wrapper, session_id=session_id, fact_id=fact_id)
    await t.audit(
        "evidence_audio",
        "clinical_fact",
        fact_id,
        {"session_id": str(session_id), "bytes": len(clip.audio), "start_ms": clip.start_ms},
    )
    return Response(
        clip.audio,
        media_type=clip.mime_type.split(";")[0],
        headers={
            "X-Evidence-Start-Ms": str(clip.start_ms),
            "X-Evidence-End-Ms": str(clip.end_ms),
            "Cache-Control": "no-store",
        },
    )

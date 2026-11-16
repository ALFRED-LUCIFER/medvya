import uuid

from fastapi import APIRouter, Response

from medvya_ai.deps import TenantDep
from medvya_ai.errors import AppError
from medvya_ai.schemas import (
    ApplyRolesOut,
    FactOut,
    FactReviewIn,
    ManualNoteIn,
    NoteEditIn,
    NoteOut,
    ReprocessOut,
    RevisionsOut,
    SpeakerRelabelIn,
    TranscriptCorrectionIn,
    TranscriptCorrectionOut,
)
from medvya_ai.services import note as note_svc

router = APIRouter(tags=["review"])


@router.patch("/sessions/{session_id}/speakers", status_code=204)
async def relabel_speaker(session_id: uuid.UUID, body: SpeakerRelabelIn, t: TenantDep) -> Response:
    n = await note_svc.relabel_speaker(t.db, session_id, body)
    await t.audit("speaker_relabel", "consultation_session", session_id, {"segments": n})
    return Response(status_code=204)


@router.patch(
    "/sessions/{session_id}/transcript/turns/{turn_id}", response_model=TranscriptCorrectionOut
)
async def correct_turn(
    session_id: uuid.UUID, turn_id: uuid.UUID, body: TranscriptCorrectionIn, t: TenantDep
) -> TranscriptCorrectionOut:
    out = await note_svc.correct_turn(t.db, session_id, turn_id, body, t.user_id)
    await t.audit(
        "transcript_correct",
        "transcript_segment",
        turn_id,
        {"session_id": str(session_id), "affected_facts": len(out.affected_fact_ids)},
    )
    return out


@router.post(
    "/sessions/{session_id}/transcript/apply-suggested-roles", response_model=ApplyRolesOut
)
async def apply_suggested_roles(session_id: uuid.UUID, t: TenantDep) -> ApplyRolesOut:
    out = await note_svc.apply_suggested_roles(t.db, session_id, t.user_id)
    await t.audit(
        "speaker_roles_apply",
        "consultation_session",
        session_id,
        {"applied": out.applied, "skipped": out.skipped_low_confidence},
    )
    return out


@router.post("/sessions/{session_id}/reprocess", response_model=ReprocessOut, status_code=202)
async def reprocess(session_id: uuid.UUID, t: TenantDep) -> ReprocessOut:
    if t.role != "doctor":
        raise AppError(403, "FORBIDDEN", "Only a doctor can re-run the extraction")
    out = await note_svc.reprocess(t.db, session_id)
    await t.audit("reprocess", "consultation_session", session_id)
    return out


@router.patch("/facts/{fact_id}", response_model=FactOut)
async def review_fact(fact_id: uuid.UUID, body: FactReviewIn, t: TenantDep) -> FactOut:
    out = await note_svc.review_fact(t.db, fact_id, body.review_state)
    await t.audit("fact_review", "clinical_fact", fact_id, {"review_state": body.review_state})
    return out


@router.post("/sessions/{session_id}/notes", response_model=NoteOut, status_code=201)
async def create_manual_note(session_id: uuid.UUID, body: ManualNoteIn, t: TenantDep) -> NoteOut:
    if t.role != "doctor":
        raise AppError(403, "FORBIDDEN", "Only a doctor can write a clinical note")
    out = await note_svc.create_manual_note(
        t.db, session_id, body.content, body.template, t.user_id
    )
    await t.audit("note_manual_create", "clinical_note", out.id, {"session_id": str(session_id)})
    return out


@router.patch("/sessions/{session_id}/note", response_model=NoteOut)
async def edit_note(session_id: uuid.UUID, body: NoteEditIn, t: TenantDep) -> NoteOut:
    out = await note_svc.edit_note(t.db, session_id, body.content, t.user_id)
    await t.audit("note_edit", "clinical_note", out.id, {"rev_no": out.rev_no})
    return out


@router.get("/sessions/{session_id}/notes/revisions", response_model=RevisionsOut)
async def list_revisions(session_id: uuid.UUID, t: TenantDep) -> RevisionsOut:
    out = await note_svc.list_revisions(t.db, session_id)
    await t.audit("revisions_view", "clinical_note", out.note_id, {"n": len(out.revisions)})
    return out

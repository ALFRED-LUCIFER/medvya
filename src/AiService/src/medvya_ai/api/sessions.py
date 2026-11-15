import uuid

from fastapi import APIRouter
from sqlalchemy import select, text

from medvya_ai import crypto
from medvya_ai.db import uuid7
from medvya_ai.deps import TenantDep, WrapperDep
from medvya_ai.errors import AppError
from medvya_ai.models import (
    ConsultationSession,
    Participant,
    ProcessingJob,
    RecordingSegment,
    RetentionPolicy,
)
from medvya_ai.schemas import (
    ConsentState,
    JobState,
    ParticipantIn,
    ParticipantOut,
    SegmentState,
    SessionCreate,
    SessionOut,
)
from medvya_ai.services import consent as consent_svc

router = APIRouter(prefix="/sessions", tags=["sessions"])


async def _view(t: TenantDep, s: ConsultationSession) -> SessionOut:
    db = t.db
    parts = (
        await db.scalars(
            select(Participant).where(Participant.session_id == s.id).order_by(Participant.id)
        )
    ).all()
    cons = await db.execute(
        text("SELECT participant_id, status FROM v_current_consent WHERE session_id = :s"),
        {"s": s.id},
    )
    segs = (
        await db.scalars(
            select(RecordingSegment)
            .where(RecordingSegment.session_id == s.id)
            .order_by(RecordingSegment.segment_no)
        )
    ).all()
    jobs = (
        await db.scalars(
            select(ProcessingJob).where(ProcessingJob.session_id == s.id).order_by(ProcessingJob.id)
        )
    ).all()
    return SessionOut(
        id=s.id,
        state=s.state,
        mode=s.mode,
        recording_allowed=s.recording_allowed,
        consent_epoch=s.consent_epoch,
        revoked_at=s.revoked_at,
        patient_ref=s.patient_ref,
        participants=[ParticipantOut.model_validate(p) for p in parts],
        consent=[ConsentState(participant_id=r[0], status=r[1]) for r in cons],
        segments=[
            SegmentState(segment_no=x.segment_no, state=x.state, chunk_count=x.chunk_count)
            for x in segs
        ],
        jobs=[JobState(kind=j.kind, status=j.status) for j in jobs],
        created_at=s.created_at,
    )


@router.post("", response_model=SessionOut, status_code=201)
async def create_session(body: SessionCreate, t: TenantDep, wrapper: WrapperDep) -> SessionOut:
    sid = uuid7()
    s = ConsultationSession(
        id=sid,
        clinic_id=t.clinic_id,
        doctor_id=t.user_id,
        patient_ref=body.patient_ref,
        specialty=body.specialty,
        scheduled_date=body.scheduled_date,
        dek_wrapped=wrapper.wrap(crypto.new_dek(), sid),
        kek_version=wrapper.version,
    )
    t.db.add(s)
    await t.db.flush()
    for p in body.participants:
        t.db.add(
            Participant(
                clinic_id=t.clinic_id, session_id=sid, role=p.role, display_name=p.display_name
            )
        )
    await t.db.flush()
    if await t.db.get(RetentionPolicy, t.clinic_id) is None:
        t.db.add(RetentionPolicy(clinic_id=t.clinic_id))
    await t.audit(
        "session_create", "consultation_session", sid, {"participants": len(body.participants)}
    )
    await t.db.refresh(s)
    return await _view(t, s)


@router.get("", response_model=list[SessionOut])
async def list_sessions(t: TenantDep) -> list[SessionOut]:
    rows = (
        await t.db.scalars(
            select(ConsultationSession).order_by(ConsultationSession.id.desc()).limit(100)
        )
    ).all()
    return [await _view(t, s) for s in rows]


@router.get("/{session_id}", response_model=SessionOut)
async def get_session(session_id: uuid.UUID, t: TenantDep) -> SessionOut:
    s = await t.db.get(ConsultationSession, session_id)
    if s is None:
        raise AppError(404, "NOT_FOUND", "Session not found")
    await t.audit("session_view", "consultation_session", s.id)
    return await _view(t, s)


@router.post("/{session_id}/participants", response_model=ParticipantOut, status_code=201)
async def add_participant(
    session_id: uuid.UUID, body: ParticipantIn, t: TenantDep
) -> ParticipantOut:
    s = await consent_svc.lock_session(t.db, session_id)
    p = await consent_svc.add_participant(t.db, s, body.role, body.display_name)
    await t.audit("participant_add", "participant", p.id, {"session_id": str(s.id)})
    return ParticipantOut.model_validate(p)

import uuid

from fastapi import APIRouter

from medvya_ai.deps import TenantDep
from medvya_ai.schemas import ConsentIn, ConsentOut, NoticeOut
from medvya_ai.services import consent as consent_svc

router = APIRouter(tags=["consent"])
CURRENT_NOTICE = "en-v1"


@router.get("/consent-notice", response_model=NoticeOut)
async def consent_notice(_: TenantDep) -> NoticeOut:
    return NoticeOut(
        version=CURRENT_NOTICE,
        locale=consent_svc.NOTICE_VERSIONS[CURRENT_NOTICE],
        text=consent_svc.notice_text(CURRENT_NOTICE),
    )


@router.post("/sessions/{session_id}/consents", response_model=ConsentOut, status_code=201)
async def post_consent(session_id: uuid.UUID, body: ConsentIn, t: TenantDep) -> ConsentOut:
    s = await consent_svc.lock_session(t.db, session_id)
    rec = await consent_svc.record_consent(
        t.db,
        s,
        participant_id=body.participant_id,
        status=body.status,
        method=body.method,
        on_behalf_of=body.on_behalf_of,
        notice_version=body.notice_version,
        attested_by=t.user_id,
        representative_name=body.representative_name,
        representative_relationship=body.representative_relationship,
    )
    await t.audit(
        "consent_" + body.status,
        "consent_record",
        rec.id,
        {"session_id": str(s.id), "participant_id": str(rec.participant_id)},
    )
    return ConsentOut(
        id=rec.id,
        participant_id=rec.participant_id,
        status=rec.status,
        method=rec.method,
        on_behalf_of=rec.on_behalf_of,
        notice_version=rec.notice_version,
        notice_locale=rec.notice_locale,
        representative_name=rec.representative_name,
        representative_relationship=rec.representative_relationship,
        created_at=rec.created_at,
        recording_allowed=s.recording_allowed,
        session_state=s.state,
    )

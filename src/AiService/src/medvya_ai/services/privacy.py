"""Patient data-access and erasure requests (DPDP: access, correction, erasure, withdrawal).

The request is a record of what was asked, how identity was verified and what was decided. Erasure
separates deletable intermediate data (unapproved sessions: revoked and purged) from approved
records retained under the clinic's record-retention basis (their raw audio is still purged).
Access lists the patient's sessions; the clinician exports approved ones with the export API.
"""

import uuid
from datetime import UTC, datetime

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai.models import ConsultationSession, PrivacyRequest
from medvya_ai.services.consent import lock_session, revoke


async def create_request(
    db: AsyncSession,
    *,
    clinic_id: uuid.UUID,
    user_id: uuid.UUID,
    patient_ref: str,
    kind: str,
    verification_note: str,
) -> PrivacyRequest:
    sessions = (
        await db.scalars(
            select(ConsultationSession)
            .where(ConsultationSession.patient_ref == patient_ref)
            .order_by(ConsultationSession.id)
        )
    ).all()
    decision: dict[str, object]
    if kind == "access":
        decision = {
            "sessions": [str(s.id) for s in sessions],
            "exportable": [str(s.id) for s in sessions if s.state == "approved"],
        }
    else:
        purged: list[str] = []
        retained: list[str] = []
        for s in sessions:
            locked = await lock_session(db, s.id)
            (retained if locked.state == "approved" else purged).append(str(locked.id))
            await revoke(db, locked)  # approved: audio only; otherwise everything unapproved
        decision = {"purged": purged, "retained_approved_records": retained}
    req = PrivacyRequest(
        clinic_id=clinic_id,
        patient_ref=patient_ref,
        kind=kind,
        status="completed",
        identity_verified_by=user_id,
        verification_note=verification_note,
        decision=decision,
        resolved_at=datetime.now(UTC),
    )
    db.add(req)
    await db.flush()
    await db.refresh(req)
    return req

import uuid

from fastapi import APIRouter

from medvya_ai.deps import TenantDep
from medvya_ai.errors import AppError
from medvya_ai.models import PrivacyRequest
from medvya_ai.schemas import PrivacyRequestIn, PrivacyRequestOut
from medvya_ai.services import privacy

router = APIRouter(prefix="/privacy-requests", tags=["privacy"])


@router.post("", response_model=PrivacyRequestOut, status_code=201)
async def create(body: PrivacyRequestIn, t: TenantDep) -> PrivacyRequestOut:
    req = await privacy.create_request(
        t.db,
        clinic_id=t.clinic_id,
        user_id=t.user_id,
        patient_ref=body.patient_ref,
        kind=body.kind,
        verification_note=body.verification_note,
    )
    await t.audit(
        f"privacy_{body.kind}",
        "privacy_request",
        req.id,
        {k: len(v) for k, v in req.decision.items() if isinstance(v, list)},
    )
    return PrivacyRequestOut.model_validate(req)


@router.get("/{request_id}", response_model=PrivacyRequestOut)
async def get(request_id: uuid.UUID, t: TenantDep) -> PrivacyRequestOut:
    req = await t.db.get(PrivacyRequest, request_id)
    if req is None:
        raise AppError(404, "NOT_FOUND", "Privacy request not found")
    return PrivacyRequestOut.model_validate(req)

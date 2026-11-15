import uuid
from hashlib import sha256
from typing import Literal

from fastapi import APIRouter, Response

from medvya_ai.deps import TenantDep
from medvya_ai.schemas import ExportOut, ExportSession
from medvya_ai.services import note as note_svc
from medvya_ai.services.pdf import Document, build_pdf

router = APIRouter(prefix="/sessions", tags=["export"])


@router.get(
    "/{session_id}/export",
    response_model=ExportOut,
    responses={200: {"content": {"application/pdf": {}}}},
)
async def export(
    session_id: uuid.UUID, t: TenantDep, format: Literal["json", "pdf"] = "json"
) -> Response:
    """Approved notes only (409 NOTE_NOT_APPROVED otherwise). The PDF is rendered from the same
    payload as the JSON; both carry its sha256 in X-Document-Hash and in the audit trail."""
    s, r = await note_svc.load_export(t.db, session_id)
    out = ExportOut(
        session=ExportSession(
            id=s.id,
            patient_ref=s.patient_ref,
            specialty=s.specialty,
            scheduled_date=s.scheduled_date,
            approved_at=s.approved_at,
        ),
        note=r.note,
        facts=r.facts,
        transcript=r.transcript,
        participants=r.participants,
    )
    body = out.model_dump_json().encode()
    digest = sha256(body).hexdigest()
    await t.audit(
        "export",
        "consultation_session",
        s.id,
        {"facts": len(r.facts), "format": format, "sha256": digest},
    )
    headers = {"X-Document-Hash": digest}
    if format == "json":
        return Response(body, media_type="application/json", headers=headers)
    clinic, clinician, template = await note_svc.export_names(t.db, s)
    pdf = build_pdf(
        Document(
            clinic_name=clinic,
            clinician_name=clinician,
            patient_ref=s.patient_ref,
            specialty=s.specialty,
            scheduled_date=s.scheduled_date,
            approved_at=s.approved_at,
            rev_no=r.note.rev_no,
            template=template,
            content=r.note.content,
            facts=r.facts,
            content_hash=digest,
        )
    )
    headers["Content-Disposition"] = f'attachment; filename="note-{s.id}.pdf"'
    return Response(pdf, media_type="application/pdf", headers=headers)

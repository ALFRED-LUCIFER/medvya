"""Extract job: LLM structured output over the English transcript, then evidence verification."""

import uuid
from collections.abc import Awaitable, Callable

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai.extraction import prompts
from medvya_ai.extraction.persist import Counts, persist_extraction
from medvya_ai.extraction.schemas import ExtractionOut
from medvya_ai.models import TranscriptSegment
from medvya_ai.providers.errors import PipelineError
from medvya_ai.providers.llm.base import LLMClient
from medvya_ai.services.note import latest_corrections


async def _noop() -> None:
    return None


def speaker_hint(r: TranscriptSegment, confirmed: dict[uuid.UUID, str]) -> str:
    """The clinician-confirmed speaker when there is one; else the cluster label plus the
    content-suggested role, so the model knows who likely speaks."""
    if r.id in confirmed:
        return confirmed[r.id]
    if r.suggested_role and r.suggested_role != "unknown":
        pct = f", {float(r.role_confidence):.0%}" if r.role_confidence is not None else ""
        return f"{r.speaker_label} (likely {r.suggested_role}{pct})"
    return r.speaker_label


async def run_extract(
    db: AsyncSession,
    llm: LLMClient,
    *,
    clinic_id: uuid.UUID,
    session_id: uuid.UUID,
    before_call: Callable[[], Awaitable[None]] = _noop,
) -> Counts:
    rows = (
        await db.scalars(
            select(TranscriptSegment)
            .where(TranscriptSegment.session_id == session_id)
            .order_by(TranscriptSegment.idx)
        )
    ).all()
    if not rows or any(r.english_text is None for r in rows):
        raise PipelineError("TRANSCRIPT_NOT_NORMALISED")
    # english_text already renders the corrected text (normalise reads the corrections).
    confirmed = (await latest_corrections(db, session_id)).speaker
    await before_call()
    out = await llm.parse(
        "extract",
        system=prompts.EXTRACT_SYSTEM,
        user=prompts.extract_user(
            [(r.idx, speaker_hint(r, confirmed), r.english_text or "") for r in rows]
        ),
        schema=ExtractionOut,
    )
    return await persist_extraction(db, clinic_id=clinic_id, session_id=session_id, out=out)

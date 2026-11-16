"""Verify and store an extraction: note + revision 1 (ai), facts, medications, evidence, rejections.

Runs inside the extract job's transaction: all or nothing. Rejection payloads hold clinical text, so
they live in the RLS-protected extraction_rejection table and are never logged.
"""

import math
import uuid
from dataclasses import dataclass
from decimal import Decimal
from typing import Any

from sqlalchemy import delete, func, select, update
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai.db import uuid7
from medvya_ai.extraction.evidence import SegText, verify_evidence
from medvya_ai.extraction.grounding import has_number, has_term, negated
from medvya_ai.extraction.normalise import frequency_code, generic_name, route_code, unit_code
from medvya_ai.extraction.schemas import ExtractionOut, FactIn, MedicationIn
from medvya_ai.models import (
    ClinicalFact,
    ClinicalNote,
    ConsultationSession,
    ExtractionRejection,
    FactEvidence,
    Medication,
    NoteRevision,
    TranscriptSegment,
)

INT4_MAX = 2_147_483_647
# Clinical assertions: the label itself must be named in an evidence quote. "Any allergies?" is a
# real quote, but it does not back "allergy: penicillin".
ASSERTION_CATEGORIES = frozenset({"diagnosis", "allergy", "history"})


@dataclass
class Counts:
    facts: int = 0
    medications: int = 0
    rejected: int = 0
    evidence_dropped: int = 0
    negation_flagged: int = 0
    grounding_flagged: int = 0


def _dec(v: float | None, *, lo: float | None = None, hi: float | None = None) -> Decimal | None:
    if (
        v is None
        or not math.isfinite(v)
        or (lo is not None and v < lo)
        or (hi is not None and v > hi)
    ):
        return None
    return Decimal(str(v))


def _num(v: float | None) -> str | None:
    return None if v is None or not math.isfinite(v) else format(Decimal(str(v)).normalize(), "f")


def _clean(s: str | None) -> str | None:
    s = (s or "").strip()
    return s or None


def _medication(
    fact_id: uuid.UUID,
    clinic_id: uuid.UUID,
    label: str,
    m: MedicationIn | None,
    cited: str,
) -> Medication:
    if m is None:  # AC4: every medication fact has a medication row
        return Medication(fact_id=fact_id, clinic_id=clinic_id, name_as_spoken=label)
    # Every model-supplied detail must be backed by the cited segment text, else it is dropped.
    name = _clean(m.name_as_spoken)
    freq_text = m.frequency_text if has_term(cited, m.frequency_text) else None
    freq: str | None
    if freq_text is None:
        derived = frequency_code(cited)
        freq = derived if derived != "OTHER" else None
    else:
        freq = m.frequency_code
        if freq in (None, "OTHER"):  # trust the deterministic normaliser over a vague answer
            freq = frequency_code(freq_text) or freq
    duration = (
        m.duration_value
        if m.duration_value is not None
        and 0 <= m.duration_value <= INT4_MAX
        and has_number(cited, m.duration_value)
        else None
    )
    strength = (
        _dec(m.strength_value, lo=0)
        if has_number(cited, m.strength_value)
        and not negated(cited, _num(m.strength_value))  # "5 mg, not 50"
        else None
    )
    route = route_code(cited)
    return Medication(
        fact_id=fact_id,
        clinic_id=clinic_id,
        name_as_spoken=name if has_term(cited, name) and name else label,
        # shortcut: a generic name the transcript does not contain is the model's guess and there is
        # no column to mark it as a suggestion, so it is not stored; add generic_name_suggested
        # (shown as unconfirmed) when the review screen needs brand -> generic help.
        generic_name=generic_name(m.generic_name) if has_term(cited, m.generic_name) else None,
        strength_value=strength,
        strength_unit=unit_code(m.strength_unit) if strength is not None else None,
        dose_text=_clean(m.dose_text) if has_term(cited, m.dose_text) else None,
        frequency_code=freq,
        frequency_text=_clean(freq_text),
        duration_value=duration,
        duration_unit=m.duration_unit if duration is not None else None,
        route=route if route != "other" else None,
        instructions=_clean(m.instructions) if has_term(cited, m.instructions) else None,
    )


def _reject(
    session_id: uuid.UUID, clinic_id: uuid.UUID, fact: FactIn, reason: str
) -> ExtractionRejection:
    return ExtractionRejection(
        clinic_id=clinic_id,
        session_id=session_id,
        reason=reason,
        payload=fact.model_dump(mode="json"),
    )


async def persist_extraction(
    db: AsyncSession, *, clinic_id: uuid.UUID, session_id: uuid.UUID, out: ExtractionOut
) -> Counts:
    rows = (
        await db.scalars(
            select(TranscriptSegment).where(TranscriptSegment.session_id == session_id)
        )
    ).all()
    segments = {r.idx: SegText(r.id, r.english_text, r.original_text) for r in rows}
    counts = Counts()

    rev_id = uuid7()
    existing = await db.scalar(select(ClinicalNote).where(ClinicalNote.session_id == session_id))
    if existing is None:
        note_id = uuid7()
        db.add(ClinicalNote(id=note_id, clinic_id=clinic_id, session_id=session_id, status="draft"))
        rev_no = 1
    else:  # reprocess of a draft: AI facts are replaced, the note gets a new AI revision
        note_id = existing.id
        facts_q = select(ClinicalFact.id).where(ClinicalFact.session_id == session_id)
        await db.execute(delete(FactEvidence).where(FactEvidence.fact_id.in_(facts_q)))
        await db.execute(delete(Medication).where(Medication.fact_id.in_(facts_q)))
        await db.execute(delete(ClinicalFact).where(ClinicalFact.session_id == session_id))
        await db.execute(
            delete(ExtractionRejection).where(ExtractionRejection.session_id == session_id)
        )
        last = await db.scalar(
            select(func.max(NoteRevision.rev_no)).where(NoteRevision.note_id == note_id)
        )
        rev_no = int(last or 0) + 1
    await db.flush()
    flagged: list[str] = []

    for fact in out.facts:
        label = fact.label.strip()
        if not label:
            db.add(_reject(session_id, clinic_id, fact, "SCHEMA"))
            counts.rejected += 1
            continue
        verified = verify_evidence(segments, [(e.segment_idx, e.quote) for e in fact.evidence])
        counts.evidence_dropped += verified.dropped
        if verified.reason is not None:
            db.add(_reject(session_id, clinic_id, fact, verified.reason))
            counts.rejected += 1
            continue
        cited = " ".join(
            t for seg, _ in verified.accepted for t in (seg.english_text or "", seg.original_text)
        )
        med = fact.medication
        if fact.category == "medication" and not (
            has_term(cited, med.name_as_spoken if med else None) or has_term(cited, label)
        ):  # a medication nobody named in the cited text is not evidence-backed
            db.add(_reject(session_id, clinic_id, fact, "QUOTE_NOT_FOUND"))
            counts.rejected += 1
            continue
        value = fact.value_numeric if has_number(cited, fact.value_numeric) else None
        polarity = fact.polarity
        quoted = " ".join(q for _, q in verified.accepted)
        if fact.category in ASSERTION_CATEGORIES and not has_term(quoted, label):
            polarity = "uncertain"  # a clinician must confirm what the quote does not say
            flagged.append(f"{label}: not named in the quoted transcript text")
            counts.grounding_flagged += 1
        if polarity == "present" and negated(
            cited, med.name_as_spoken if med and has_term(cited, med.name_as_spoken) else label
        ):  # the transcript negates what the model asserted: a clinician must decide
            polarity = "uncertain"
            flagged.append(f"{label}: stated as present but negated in the transcript")
            counts.negation_flagged += 1
        fact_id = uuid7()
        db.add(
            ClinicalFact(
                id=fact_id,
                clinic_id=clinic_id,
                session_id=session_id,
                note_id=note_id,
                category=fact.category,
                label=label,
                polarity=polarity,
                body_site=_clean(fact.body_site),
                value_numeric=_dec(value),
                value_unit=unit_code(fact.value_unit) if value is not None else None,
                detail=_clean(fact.detail),
                confidence=_dec(fact.confidence, lo=0, hi=1),
                review_state="ai",
            )
        )
        await db.flush()  # fact row must exist before its 1:1 medication and evidence rows
        for seg, quote in verified.accepted:
            db.add(
                FactEvidence(clinic_id=clinic_id, fact_id=fact_id, segment_id=seg.id, quote=quote)
            )
        if fact.category == "medication":
            db.add(_medication(fact_id, clinic_id, label, med, cited))
            counts.medications += 1
        counts.facts += 1

    content: dict[str, Any] = {
        **out.note.model_dump(),
        "missing_or_uncertain": [*out.missing_or_uncertain, *flagged],
    }
    db.add(
        NoteRevision(
            id=rev_id,
            clinic_id=clinic_id,
            note_id=note_id,
            rev_no=rev_no,
            author_type="ai",
            author_id=None,
            content=content,
        )
    )
    await db.flush()
    await db.execute(
        update(ClinicalNote).where(ClinicalNote.id == note_id).values(current_revision_id=rev_id)
    )

    await db.execute(
        update(ConsultationSession)
        .where(ConsultationSession.id == session_id, ConsultationSession.state == "processing")
        .values(state="review")
    )
    return counts

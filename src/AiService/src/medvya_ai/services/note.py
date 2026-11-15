"""Review, edit, approve and export. Notes are draft until a clinician approves; then immutable."""

import uuid
from dataclasses import dataclass, field
from datetime import UTC, datetime
from typing import Any

from sqlalchemy import func, select, update
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai.db import uuid7
from medvya_ai.errors import AppError
from medvya_ai.jobs.queue import enqueue
from medvya_ai.models import (
    AppUser,
    Clinic,
    ClinicalFact,
    ClinicalNote,
    ConsultationSession,
    ExtractionRejection,
    FactEvidence,
    Medication,
    NoteRevision,
    Participant,
    RecordingSegment,
    TranscriptCorrection,
    TranscriptSegment,
)
from medvya_ai.schemas import (
    ApplyRolesOut,
    EvidenceOut,
    FactOut,
    MedicationOut,
    NoteContent,
    NoteOut,
    ParticipantOut,
    QualityOut,
    ReprocessOut,
    ResultOut,
    RevisionOut,
    RevisionsOut,
    SpeakerRelabelIn,
    TranscriptCorrectionIn,
    TranscriptCorrectionOut,
    TranscriptOut,
)

__all__ = ["NoteContent", "ensure_editable"]

READY_STATES = ("review", "approved")
LOW_CONFIDENCE = 0.6  # provider phrase confidence below this is flagged for the clinician
ROLE_CONFIDENCE = 0.7  # suggested roles below this are not applied in bulk
MANUAL_FROM_STATES = ("created", "ready", "closed_no_consent")  # nothing was ever recorded


def ensure_editable(status: str) -> None:
    if status != "draft":
        raise AppError(409, "NOTE_APPROVED", "The note is approved and can no longer be edited")


async def get_session(
    db: AsyncSession, session_id: uuid.UUID, *, lock: bool = False
) -> ConsultationSession:
    q = select(ConsultationSession).where(ConsultationSession.id == session_id)
    s = await db.scalar(q.with_for_update() if lock else q)
    if s is None:
        raise AppError(404, "NOT_FOUND", "Session not found")
    return s


def ensure_ready(s: ConsultationSession) -> None:
    if s.state == "revoked":
        raise AppError(409, "CONSENT_WITHDRAWN", "Consent was withdrawn; the draft was deleted")
    if s.state == "failed":
        raise AppError(409, "PROCESSING_FAILED", "Processing failed for this session")
    if s.state not in READY_STATES:
        raise AppError(409, "NOT_READY", "The session is still being processed")


async def get_note(db: AsyncSession, session_id: uuid.UUID, *, lock: bool = False) -> ClinicalNote:
    q = select(ClinicalNote).where(ClinicalNote.session_id == session_id)
    n = await db.scalar(q.with_for_update() if lock else q)
    if n is None:
        raise AppError(404, "NOT_FOUND", "Note not found")
    return n


async def _current_revision(db: AsyncSession, note: ClinicalNote) -> NoteRevision:
    rev = await db.get(NoteRevision, note.current_revision_id) if note.current_revision_id else None
    if rev is None:
        raise AppError(500, "NOTE_CORRUPT", "Note has no current revision")
    return rev


async def note_out(db: AsyncSession, note: ClinicalNote) -> NoteOut:
    rev = await _current_revision(db, note)
    return NoteOut(
        id=note.id,
        status=note.status,
        rev_no=rev.rev_no,
        content=NoteContent.model_validate(rev.content),
        unverified=rev.author_type == "ai",
    )


async def _append_revision(
    db: AsyncSession, note: ClinicalNote, content: dict[str, Any], user_id: uuid.UUID
) -> NoteRevision:
    last = await db.scalar(
        select(func.max(NoteRevision.rev_no)).where(NoteRevision.note_id == note.id)
    )
    rev = NoteRevision(
        id=uuid7(),
        clinic_id=note.clinic_id,
        note_id=note.id,
        rev_no=(last or 0) + 1,
        author_type="clinician",
        author_id=user_id,
        content=content,
    )
    db.add(rev)
    await db.flush()
    note.current_revision_id = rev.id
    return rev


async def create_manual_note(
    db: AsyncSession,
    session_id: uuid.UUID,
    content: NoteContent,
    template: str,
    user_id: uuid.UUID,
) -> NoteOut:
    """Declined-consent path: the clinician documents the encounter without any recording. The
    session becomes a manual draft in review; edit, approve and export then work as usual."""
    s = await get_session(db, session_id, lock=True)
    if s.state not in MANUAL_FROM_STATES:
        raise AppError(409, "SESSION_STATE", "A manual note needs a session without a recording")
    if await db.scalar(select(RecordingSegment.id).where(RecordingSegment.session_id == s.id)):
        raise AppError(409, "SESSION_STATE", "Audio exists; finish the recording instead")
    note = ClinicalNote(
        id=uuid7(), clinic_id=s.clinic_id, session_id=s.id, template=template, status="draft"
    )
    db.add(note)
    await db.flush()
    await _append_revision(db, note, content.model_dump(), user_id)
    s.mode, s.state, s.recording_allowed = "manual", "review", False
    await db.flush()
    return await note_out(db, note)


async def edit_note(
    db: AsyncSession, session_id: uuid.UUID, content: NoteContent, user_id: uuid.UUID
) -> NoteOut:
    s = await get_session(db, session_id)
    ensure_ready(s)
    note = await get_note(db, session_id, lock=True)
    ensure_editable(note.status)
    await _append_revision(db, note, content.model_dump(), user_id)
    await db.flush()
    return await note_out(db, note)


async def approve(
    db: AsyncSession, session_id: uuid.UUID, user_id: uuid.UUID
) -> tuple[ClinicalNote, int]:
    """Returns the approved note and the new revision number."""
    s = await get_session(db, session_id, lock=True)
    ensure_ready(s)
    note = await get_note(db, session_id, lock=True)
    ensure_editable(note.status)
    cur = await _current_revision(db, note)
    rev = await _append_revision(db, note, cur.content, user_id)
    now = datetime.now(UTC)
    note.status, note.approved_by, note.approved_at = "approved", user_id, now
    s.state, s.approved_at = "approved", now
    await db.flush()
    return note, rev.rev_no


async def relabel_speaker(db: AsyncSession, session_id: uuid.UUID, body: SpeakerRelabelIn) -> int:
    s = await get_session(db, session_id)
    ensure_ready(s)
    note = await get_note(db, session_id)
    ensure_editable(note.status)
    updated = (
        await db.execute(
            update(TranscriptSegment)
            .where(
                TranscriptSegment.session_id == session_id,
                TranscriptSegment.speaker_label == body.speaker_label,
            )
            .values(speaker_label=body.new_label)
            .returning(TranscriptSegment.id)
        )
    ).all()
    if not updated:
        raise AppError(404, "NOT_FOUND", "No segments with that speaker label")
    if body.participant_id is not None:
        p = await db.scalar(
            select(Participant).where(
                Participant.id == body.participant_id, Participant.session_id == session_id
            )
        )
        if p is None:
            raise AppError(404, "NOT_FOUND", "Participant not found")
        p.speaker_label = body.new_label
    await db.flush()
    return len(updated)


async def _facts(
    db: AsyncSession,
    *,
    session_id: uuid.UUID | None = None,
    fact_id: uuid.UUID | None = None,
    include_rejected: bool = True,
) -> list[FactOut]:
    q = select(ClinicalFact).order_by(ClinicalFact.created_at, ClinicalFact.id)
    if session_id is not None:
        q = q.where(ClinicalFact.session_id == session_id)
    if fact_id is not None:
        q = q.where(ClinicalFact.id == fact_id)
    if not include_rejected:
        q = q.where(ClinicalFact.review_state != "rejected")
    facts = (await db.scalars(q)).all()
    ids = [f.id for f in facts]
    evidence: dict[uuid.UUID, list[EvidenceOut]] = {}
    meds: dict[uuid.UUID, MedicationOut] = {}
    if ids:
        roles: dict[str, str] = {}
        if session_id is not None:
            roles = {
                p.speaker_label: p.role
                for p in (
                    await db.scalars(
                        select(Participant).where(
                            Participant.session_id == session_id,
                            Participant.speaker_label.is_not(None),
                        )
                    )
                ).all()
                if p.speaker_label
            }
        overrides = (
            (await _latest_corrections(db, session_id)).speaker if session_id is not None else {}
        )
        rows = (
            await db.execute(
                select(
                    FactEvidence,
                    TranscriptSegment.speaker_label,
                    TranscriptSegment.start_ms,
                    TranscriptSegment.confidence,
                    TranscriptSegment.suggested_role,
                )
                .join(TranscriptSegment, TranscriptSegment.id == FactEvidence.segment_id)
                .where(FactEvidence.fact_id.in_(ids))
                .order_by(TranscriptSegment.start_ms, FactEvidence.id)
            )
        ).all()
        for e, label, start_ms, conf, suggested in rows:
            confirmed = e.segment_id in overrides
            label = overrides.get(e.segment_id, label)
            speaker = roles.get(label, label)
            if not confirmed and suggested and suggested != "unknown" and speaker != suggested:
                speaker = f"{speaker} (likely {suggested})"  # unconfirmed content-based suggestion
            evidence.setdefault(e.fact_id, []).append(
                EvidenceOut(
                    segment_id=e.segment_id,
                    quote=e.quote,
                    speaker_label=speaker,
                    start_ms=start_ms,
                    low_confidence=conf is not None and float(conf) < LOW_CONFIDENCE,
                )
            )
        for m in (await db.scalars(select(Medication).where(Medication.fact_id.in_(ids)))).all():
            meds[m.fact_id] = MedicationOut(
                name_as_spoken=m.name_as_spoken,
                generic_name=m.generic_name,
                strength_value=float(m.strength_value) if m.strength_value is not None else None,
                strength_unit=m.strength_unit,
                dose_text=m.dose_text,
                frequency_code=m.frequency_code,
                frequency_text=m.frequency_text,
                duration_value=m.duration_value,
                duration_unit=m.duration_unit,
                route=m.route,
                instructions=m.instructions,
            )
    return [
        FactOut(
            id=f.id,
            category=f.category,
            label=f.label,
            polarity=f.polarity,
            body_site=f.body_site,
            value_numeric=float(f.value_numeric) if f.value_numeric is not None else None,
            value_unit=f.value_unit,
            detail=f.detail,
            review_state=f.review_state,
            evidence=evidence.get(f.id, []),
            medication=meds.get(f.id),
            reported_by=(evidence[f.id][0].speaker_label if evidence.get(f.id) else None),
        )
        for f in facts
    ]


async def review_fact(db: AsyncSession, fact_id: uuid.UUID, review_state: str) -> FactOut:
    fact = await db.scalar(select(ClinicalFact).where(ClinicalFact.id == fact_id).with_for_update())
    if fact is None:
        raise AppError(404, "NOT_FOUND", "Fact not found")
    note = await db.get(ClinicalNote, fact.note_id)
    if note is None:
        raise AppError(404, "NOT_FOUND", "Note not found")
    ensure_editable(note.status)
    fact.review_state = review_state
    await db.flush()
    return (await _facts(db, fact_id=fact_id))[0]


@dataclass
class Corrections:
    text: dict[uuid.UUID, str] = field(default_factory=dict)
    speaker: dict[uuid.UUID, str] = field(default_factory=dict)


async def _latest_corrections(db: AsyncSession, session_id: uuid.UUID) -> Corrections:
    rows = (
        await db.scalars(
            select(TranscriptCorrection)
            .where(TranscriptCorrection.session_id == session_id)
            .order_by(TranscriptCorrection.created_at.desc(), TranscriptCorrection.id.desc())
        )
    ).all()
    out = Corrections()
    for c in rows:  # newest first: the first value seen per segment wins
        if c.corrected_text is not None:
            out.text.setdefault(c.segment_id, c.corrected_text)
        if c.speaker_label is not None:
            out.speaker.setdefault(c.segment_id, c.speaker_label)
    return out


def _turn(r: TranscriptSegment, corrected: Corrections) -> TranscriptOut:
    return TranscriptOut(
        id=r.id,
        idx=r.idx,
        speaker_label=corrected.speaker.get(r.id, r.speaker_label),
        provider_speaker=r.speaker_label,
        suggested_role=r.suggested_role,
        role_confidence=float(r.role_confidence) if r.role_confidence is not None else None,
        start_ms=r.start_ms,
        end_ms=r.end_ms,
        language=r.language,
        original_text=r.original_text,
        text=corrected.text.get(r.id, r.original_text),
        english_text=r.english_text,
        edited=r.edited or r.id in corrected.speaker,
        confidence=float(r.confidence) if r.confidence is not None else None,
        low_confidence=r.confidence is not None and float(r.confidence) < LOW_CONFIDENCE,
    )


def quality(transcript: list[TranscriptOut], facts: list[FactOut], rejected: int) -> QualityOut:
    languages: dict[str, int] = {}
    for t in transcript:
        languages[t.language] = languages.get(t.language, 0) + 1
    return QualityOut(
        phrases=len(transcript),
        unknown_speaker_phrases=sum(t.speaker_label == "unknown" for t in transcript),
        low_confidence_phrases=sum(t.low_confidence for t in transcript),
        edited_phrases=sum(t.edited for t in transcript),
        languages=languages,
        duration_ms=max((t.end_ms for t in transcript), default=0),
        facts_uncertain=sum(f.polarity == "uncertain" for f in facts),
        facts_rejected_by_gate=rejected,
    )


async def _transcript(db: AsyncSession, session_id: uuid.UUID) -> list[TranscriptOut]:
    rows = (
        await db.scalars(
            select(TranscriptSegment)
            .where(TranscriptSegment.session_id == session_id)
            .order_by(TranscriptSegment.idx)
        )
    ).all()
    corrected = await _latest_corrections(db, session_id)
    return [_turn(r, corrected) for r in rows]


async def _editable_session(db: AsyncSession, session_id: uuid.UUID) -> ConsultationSession:
    s = await get_session(db, session_id)
    ensure_ready(s)
    ensure_editable((await get_note(db, session_id)).status)
    return s


async def correct_turn(
    db: AsyncSession,
    session_id: uuid.UUID,
    turn_id: uuid.UUID,
    body: TranscriptCorrectionIn,
    user_id: uuid.UUID,
) -> TranscriptCorrectionOut:
    """Append a correction (text, speaker, or both); the original row never changes, so every
    evidence quote stays valid. Facts citing the turn are returned for clinician re-review."""
    s = await _editable_session(db, session_id)
    seg = await db.scalar(
        select(TranscriptSegment).where(
            TranscriptSegment.id == turn_id, TranscriptSegment.session_id == session_id
        )
    )
    if seg is None:
        raise AppError(404, "NOT_FOUND", "Transcript turn not found")
    db.add(
        TranscriptCorrection(
            clinic_id=s.clinic_id,
            session_id=session_id,
            segment_id=seg.id,
            corrected_text=body.corrected_text,
            speaker_label=body.speaker_label,
            reason=body.reason,
            author_id=user_id,
        )
    )
    seg.edited = True
    await db.flush()
    affected = (
        await db.scalars(
            select(FactEvidence.fact_id).where(FactEvidence.segment_id == seg.id).distinct()
        )
    ).all()
    return TranscriptCorrectionOut(
        turn=_turn(seg, await _latest_corrections(db, session_id)),
        affected_fact_ids=list(affected),
    )


async def apply_suggested_roles(
    db: AsyncSession, session_id: uuid.UUID, user_id: uuid.UUID
) -> ApplyRolesOut:
    """Clinician accepts the content-based role suggestions in bulk: one speaker override per
    phrase whose suggestion is confident and differs from its current speaker. Participants with
    that role get the role as speaker_label so fact provenance resolves to the role."""
    s = await _editable_session(db, session_id)
    rows = (
        await db.scalars(
            select(TranscriptSegment).where(TranscriptSegment.session_id == session_id)
        )
    ).all()
    current = await _latest_corrections(db, session_id)
    applied = skipped = 0
    for r in rows:
        role = r.suggested_role
        if role is None or role == "unknown":
            continue
        if r.role_confidence is None or float(r.role_confidence) < ROLE_CONFIDENCE:
            skipped += 1
            continue
        if current.speaker.get(r.id, r.speaker_label) == role:
            continue
        db.add(
            TranscriptCorrection(
                clinic_id=s.clinic_id,
                session_id=session_id,
                segment_id=r.id,
                speaker_label=role,
                reason="suggested role accepted",
                author_id=user_id,
            )
        )
        applied += 1
    if applied:
        for p in (
            await db.scalars(select(Participant).where(Participant.session_id == session_id))
        ).all():
            p.speaker_label = p.role
    await db.flush()
    return ApplyRolesOut(applied=applied, skipped_low_confidence=skipped)


async def reprocess(db: AsyncSession, session_id: uuid.UUID) -> ReprocessOut:
    """Re-run normalise + extract on the stored transcript (draft only). The note keeps its
    revision chain and gains a new AI revision; AI facts are replaced."""
    s = await get_session(db, session_id, lock=True)
    ensure_ready(s)
    ensure_editable((await get_note(db, session_id)).status)
    if s.mode == "manual":
        raise AppError(409, "SESSION_STATE", "A manual note has no transcript to reprocess")
    s.state = "processing"
    await enqueue(
        db, clinic_id=s.clinic_id, session_id=s.id, kind="normalise", consent_epoch=s.consent_epoch
    )
    await db.flush()
    return ReprocessOut(state=s.state)


async def _participants(db: AsyncSession, session_id: uuid.UUID) -> list[ParticipantOut]:
    rows = (
        await db.scalars(
            select(Participant).where(Participant.session_id == session_id).order_by(Participant.id)
        )
    ).all()
    return [ParticipantOut.model_validate(p) for p in rows]


async def load_result(db: AsyncSession, session_id: uuid.UUID) -> ResultOut:
    s = await get_session(db, session_id)
    ensure_ready(s)
    note = await note_out(db, await get_note(db, session_id))
    facts = await _facts(db, session_id=session_id)
    transcript = await _transcript(db, session_id)
    rejected = await db.scalar(
        select(func.count()).where(ExtractionRejection.session_id == session_id)
    )
    return ResultOut(
        state=s.state,
        label="APPROVED" if note.status == "approved" else "DRAFT",
        summary=note.content.summary,
        summary_unverified=note.unverified,
        note=note,
        facts=facts,
        transcript=transcript,
        participants=await _participants(db, session_id),
        quality=quality(transcript, facts, int(rejected or 0)),
    )


async def list_revisions(db: AsyncSession, session_id: uuid.UUID) -> RevisionsOut:
    ensure_ready(await get_session(db, session_id))
    note = await get_note(db, session_id)
    rows = (
        await db.scalars(
            select(NoteRevision)
            .where(NoteRevision.note_id == note.id)
            .order_by(NoteRevision.rev_no)
        )
    ).all()
    return RevisionsOut(
        note_id=note.id,
        status=note.status,
        template=note.template,
        revisions=[
            RevisionOut(
                rev_no=r.rev_no,
                author_type=r.author_type,
                author_id=r.author_id,
                created_at=r.created_at,
                content=NoteContent.model_validate(r.content),
            )
            for r in rows
        ],
    )


async def export_names(db: AsyncSession, s: ConsultationSession) -> tuple[str, str, str]:
    """(clinic name, approving clinician name, note template) for the PDF header."""
    note = await get_note(db, s.id)
    clinic = await db.get(Clinic, s.clinic_id)
    user = await db.get(AppUser, note.approved_by) if note.approved_by else None
    return (
        clinic.name if clinic else "",
        user.display_name if user else "",
        note.template,
    )


async def load_export(
    db: AsyncSession, session_id: uuid.UUID
) -> tuple[ConsultationSession, ResultOut]:
    """Approved notes only. Rejected facts are left out; review_state travels with the rest."""
    s = await get_session(db, session_id)
    ensure_ready(s)
    note = await get_note(db, session_id)
    if note.status != "approved":
        raise AppError(409, "NOTE_NOT_APPROVED", "Only approved notes can be exported")
    out = await load_result(db, session_id)
    out.facts = await _facts(db, session_id=session_id, include_rejected=False)
    return s, out

"""Transcript, note and structured clinical facts (the analysable data)."""

import uuid
from datetime import datetime
from decimal import Decimal
from typing import Any

import sqlalchemy as sa
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import Mapped, mapped_column

from medvya_ai.models.base import Base, pg_enum, pk, ts_now, uuid_col


class TranscriptSegment(Base):
    __tablename__ = "transcript_segment"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    session_id: Mapped[uuid.UUID] = uuid_col()
    recording_segment_id: Mapped[uuid.UUID] = uuid_col()
    idx: Mapped[int]
    speaker_label: Mapped[str]
    original_text: Mapped[str]
    language: Mapped[str]
    english_text: Mapped[str | None]
    start_ms: Mapped[int] = mapped_column(sa.BigInteger)
    end_ms: Mapped[int] = mapped_column(sa.BigInteger)
    confidence: Mapped[Decimal | None] = mapped_column(sa.Numeric(5, 4))
    edited: Mapped[bool] = mapped_column(server_default=sa.false())
    # Role the normalise step read from the content (not identity); confirmed via a correction.
    suggested_role: Mapped[str | None]
    role_confidence: Mapped[Decimal | None] = mapped_column(sa.Numeric(5, 4))


class TranscriptCorrection(Base):
    """Append-only (DB trigger). The newest row per segment is the displayed text."""

    __tablename__ = "transcript_correction"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    session_id: Mapped[uuid.UUID] = uuid_col()
    segment_id: Mapped[uuid.UUID] = uuid_col()
    corrected_text: Mapped[str | None]  # new text, or None when only the speaker changes
    speaker_label: Mapped[str | None]  # per-phrase speaker override (role or participant label)
    reason: Mapped[str | None]
    author_id: Mapped[uuid.UUID] = uuid_col()
    created_at: Mapped[datetime] = ts_now()


class ClinicalNote(Base):
    __tablename__ = "clinical_note"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    session_id: Mapped[uuid.UUID] = uuid_col()
    template: Mapped[str] = mapped_column(pg_enum("note_template"), server_default="soap")
    status: Mapped[str] = mapped_column(pg_enum("note_status"), server_default="draft")
    current_revision_id: Mapped[uuid.UUID | None] = uuid_col(nullable=True)
    approved_by: Mapped[uuid.UUID | None] = uuid_col(nullable=True)
    approved_at: Mapped[datetime | None]
    created_at: Mapped[datetime] = ts_now()


class NoteRevision(Base):
    """Append-only (DB trigger)."""

    __tablename__ = "note_revision"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    note_id: Mapped[uuid.UUID] = uuid_col()
    rev_no: Mapped[int]
    author_type: Mapped[str] = mapped_column(pg_enum("note_author_type"))
    author_id: Mapped[uuid.UUID | None] = uuid_col(nullable=True)
    content: Mapped[dict[str, Any]] = mapped_column(JSONB)
    created_at: Mapped[datetime] = ts_now()


class ClinicalFact(Base):
    __tablename__ = "clinical_fact"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    session_id: Mapped[uuid.UUID] = uuid_col()
    note_id: Mapped[uuid.UUID] = uuid_col()
    category: Mapped[str] = mapped_column(pg_enum("fact_category"))
    label: Mapped[str]
    polarity: Mapped[str] = mapped_column(pg_enum("fact_polarity"), server_default="present")
    body_site: Mapped[str | None]
    value_numeric: Mapped[Decimal | None] = mapped_column(sa.Numeric)
    value_unit: Mapped[str | None]
    detail: Mapped[str | None]
    code_system: Mapped[str | None]
    code: Mapped[str | None]
    confidence: Mapped[Decimal | None] = mapped_column(sa.Numeric(5, 4))
    review_state: Mapped[str] = mapped_column(pg_enum("fact_review_state"), server_default="ai")
    created_at: Mapped[datetime] = ts_now()


class Medication(Base):
    __tablename__ = "medication"
    fact_id: Mapped[uuid.UUID] = mapped_column(sa.UUID(as_uuid=True), primary_key=True)
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    category: Mapped[str] = mapped_column(pg_enum("fact_category"), server_default="medication")
    name_as_spoken: Mapped[str]
    generic_name: Mapped[str | None]
    strength_value: Mapped[Decimal | None] = mapped_column(sa.Numeric)
    strength_unit: Mapped[str | None]
    dose_text: Mapped[str | None]
    frequency_code: Mapped[str | None] = mapped_column(pg_enum("frequency_code"))
    frequency_text: Mapped[str | None]
    duration_value: Mapped[int | None]
    duration_unit: Mapped[str | None] = mapped_column(pg_enum("duration_unit"))
    route: Mapped[str | None] = mapped_column(pg_enum("medication_route"))
    instructions: Mapped[str | None]


class FactEvidence(Base):
    __tablename__ = "fact_evidence"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    fact_id: Mapped[uuid.UUID] = uuid_col()
    segment_id: Mapped[uuid.UUID] = uuid_col()
    quote: Mapped[str]
    start_char: Mapped[int | None]


class ExtractionRejection(Base):
    __tablename__ = "extraction_rejection"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    session_id: Mapped[uuid.UUID] = uuid_col()
    payload: Mapped[dict[str, Any]] = mapped_column(JSONB)
    reason: Mapped[str]
    created_at: Mapped[datetime] = ts_now()

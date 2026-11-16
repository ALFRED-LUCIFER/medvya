"""Declarative base, enum value sets (mirrored in migration 0001) and column helpers."""

import uuid
from datetime import datetime
from typing import Any, ClassVar

import sqlalchemy as sa
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column

from medvya_ai.db import uuid7

ENUMS: dict[str, tuple[str, ...]] = {
    "user_role": ("doctor", "admin"),
    "session_state": (
        "created",
        "ready",
        "recording",
        "processing",
        "review",
        "approved",
        "failed",
        "closed_no_consent",
        "revoked",
    ),
    "participant_role": ("doctor", "patient", "companion", "interpreter"),
    "consent_status": ("granted", "refused", "withdrawn"),
    "consent_method": ("verbal_attested", "digital_signature"),
    "consent_on_behalf_of": ("self", "guardian", "representative"),
    "segment_state": ("open", "complete"),
    "note_template": ("soap", "concise"),
    "note_status": ("draft", "approved"),
    "note_author_type": ("ai", "clinician"),
    "fact_category": (
        "symptom",
        "diagnosis",
        "medication",
        "allergy",
        "vital",
        "exam_finding",
        "investigation",
        "follow_up",
        "advice",
        "history",
    ),
    "fact_polarity": ("present", "absent", "uncertain"),
    "fact_review_state": ("ai", "confirmed", "rejected"),
    "frequency_code": ("OD", "BD", "TDS", "QID", "HS", "SOS", "STAT", "PRN", "OTHER"),
    "duration_unit": ("day", "week", "month"),
    "medication_route": ("oral", "topical", "iv", "im", "inhalation", "other"),
    "job_kind": (
        "transcribe",
        "normalise",
        "extract",
        "retention_sweep",
        "purge_session",
    ),
    "job_status": ("queued", "running", "done", "failed", "cancelled"),
    "session_mode": ("recorded", "manual"),
}


def pg_enum(name: str) -> sa.Enum:
    """Native PG enum already created by the migration (create_type=False)."""
    return sa.Enum(*ENUMS[name], name=name, create_type=False)


class Base(DeclarativeBase):
    # all DB timestamps are timestamptz; a bare Mapped[datetime] would bind as naive TIMESTAMP
    type_annotation_map: ClassVar[dict[Any, Any]] = {datetime: sa.DateTime(timezone=True)}


def pk() -> Mapped[uuid.UUID]:
    return mapped_column(UUID(as_uuid=True), primary_key=True, default=uuid7)


def uuid_col(*, nullable: bool = False) -> Mapped[Any]:
    # FKs are declared in the migration (composite, tenant-safe); models only need the type.
    return mapped_column(UUID(as_uuid=True), nullable=nullable)


def ts_now() -> Mapped[datetime]:
    return mapped_column(sa.DateTime(timezone=True), server_default=sa.func.now())

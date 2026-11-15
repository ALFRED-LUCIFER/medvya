"""Clinic, identity, consultation session, consent, recording, audit and job tables."""

import uuid
from datetime import date, datetime
from typing import Any

import sqlalchemy as sa
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import Mapped, mapped_column

from medvya_ai.models.base import Base, pg_enum, pk, ts_now, uuid_col


class Clinic(Base):
    __tablename__ = "clinic"
    id: Mapped[uuid.UUID] = pk()
    name: Mapped[str]
    created_at: Mapped[datetime] = ts_now()


class AppUser(Base):
    __tablename__ = "app_user"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    email: Mapped[str]
    password_hash: Mapped[str]
    role: Mapped[str] = mapped_column(pg_enum("user_role"))
    display_name: Mapped[str]
    external_subject: Mapped[str | None]  # OIDC oid/sub; NULL = cannot log in
    is_active: Mapped[bool] = mapped_column(server_default=sa.true())
    created_at: Mapped[datetime] = ts_now()


class RetentionPolicy(Base):
    __tablename__ = "retention_policy"
    clinic_id: Mapped[uuid.UUID] = mapped_column(sa.UUID(as_uuid=True), primary_key=True)
    audio_after_approval_hours: Mapped[int] = mapped_column(server_default="24")
    draft_audio_days: Mapped[int] = mapped_column(server_default="7")
    record_retention_years: Mapped[int | None]


class ConsultationSession(Base):
    __tablename__ = "consultation_session"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    doctor_id: Mapped[uuid.UUID] = uuid_col()
    patient_ref: Mapped[str]
    specialty: Mapped[str | None]
    scheduled_date: Mapped[date]
    state: Mapped[str] = mapped_column(pg_enum("session_state"), server_default="created")
    recording_allowed: Mapped[bool] = mapped_column(server_default=sa.false())
    dek_wrapped: Mapped[bytes]
    kek_version: Mapped[str]
    finished_at: Mapped[datetime | None]
    approved_at: Mapped[datetime | None]
    # Bumped on every withdrawal; a job carrying an older epoch is cancelled, never run.
    consent_epoch: Mapped[int] = mapped_column(server_default="0")
    revoked_at: Mapped[datetime | None]
    mode: Mapped[str] = mapped_column(pg_enum("session_mode"), server_default="recorded")
    created_at: Mapped[datetime] = ts_now()


class Participant(Base):
    __tablename__ = "participant"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    session_id: Mapped[uuid.UUID] = uuid_col()
    role: Mapped[str] = mapped_column(pg_enum("participant_role"))
    display_name: Mapped[str]
    speaker_label: Mapped[str | None]
    created_at: Mapped[datetime] = ts_now()


class ConsentRecord(Base):
    """Append-only (DB trigger). Current state = latest row per participant (v_current_consent)."""

    __tablename__ = "consent_record"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    session_id: Mapped[uuid.UUID] = uuid_col()
    participant_id: Mapped[uuid.UUID] = uuid_col()
    status: Mapped[str] = mapped_column(pg_enum("consent_status"))
    method: Mapped[str] = mapped_column(pg_enum("consent_method"))
    on_behalf_of: Mapped[str] = mapped_column(pg_enum("consent_on_behalf_of"))
    attested_by_user_id: Mapped[uuid.UUID] = uuid_col()
    notice_version: Mapped[str]
    notice_locale: Mapped[str]
    representative_name: Mapped[str | None]
    representative_relationship: Mapped[str | None]
    created_at: Mapped[datetime] = ts_now()


class RecordingSegment(Base):
    __tablename__ = "recording_segment"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    session_id: Mapped[uuid.UUID] = uuid_col()
    segment_no: Mapped[int]
    start_offset_ms: Mapped[int] = mapped_column(sa.BigInteger)
    duration_ms: Mapped[int | None] = mapped_column(sa.BigInteger)
    chunk_count: Mapped[int | None]
    mime_type: Mapped[str]
    state: Mapped[str] = mapped_column(pg_enum("segment_state"), server_default="open")
    client_events: Mapped[list[dict[str, Any]]] = mapped_column(
        JSONB, server_default=sa.text("'[]'::jsonb")
    )
    created_at: Mapped[datetime] = ts_now()
    completed_at: Mapped[datetime | None]


class AudioChunk(Base):
    __tablename__ = "audio_chunk"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    session_id: Mapped[uuid.UUID] = uuid_col()
    segment_id: Mapped[uuid.UUID] = uuid_col()
    seq: Mapped[int]
    size_bytes: Mapped[int]
    sha256: Mapped[str] = mapped_column(sa.CHAR(64))
    blob_path: Mapped[str]
    nonce: Mapped[bytes]
    deleted_at: Mapped[datetime | None]
    created_at: Mapped[datetime] = ts_now()


class AuditEvent(Base):
    """Append-only (trigger + REVOKE). metadata holds ids and counts only, never PHI."""

    __tablename__ = "audit_event"
    id: Mapped[int] = mapped_column(sa.BigInteger, primary_key=True)
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    actor_user_id: Mapped[uuid.UUID | None] = uuid_col(nullable=True)
    action: Mapped[str]
    entity_type: Mapped[str]
    entity_id: Mapped[uuid.UUID | None] = uuid_col(nullable=True)
    meta: Mapped[dict[str, Any]] = mapped_column(
        "metadata", JSONB, server_default=sa.text("'{}'::jsonb")
    )
    ip: Mapped[str | None]
    created_at: Mapped[datetime] = ts_now()


class PrivacyRequest(Base):
    """Patient access / erasure request. decision holds ids and counts only."""

    __tablename__ = "privacy_request"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    patient_ref: Mapped[str]
    kind: Mapped[str] = mapped_column(pg_enum("privacy_request_kind"))
    status: Mapped[str] = mapped_column(
        pg_enum("privacy_request_status"), server_default="received"
    )
    identity_verified_by: Mapped[uuid.UUID] = uuid_col()
    verification_note: Mapped[str | None]
    decision: Mapped[dict[str, Any]] = mapped_column(JSONB, server_default=sa.text("'{}'::jsonb"))
    created_at: Mapped[datetime] = ts_now()
    resolved_at: Mapped[datetime | None]


class ReceptionistConversation(Base):
    """Intake chat (POC). messages: [{role: patient|receptionist, text}], intake: Intake dump."""

    __tablename__ = "receptionist_conversation"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    status: Mapped[str] = mapped_column(pg_enum("receptionist_status"), server_default="open")
    language: Mapped[str | None]
    intake: Mapped[dict[str, Any]] = mapped_column(JSONB, server_default=sa.text("'{}'::jsonb"))
    messages: Mapped[list[dict[str, Any]]] = mapped_column(
        JSONB, server_default=sa.text("'[]'::jsonb")
    )
    session_id: Mapped[uuid.UUID | None] = uuid_col(nullable=True)
    created_at: Mapped[datetime] = ts_now()
    updated_at: Mapped[datetime] = ts_now()


class ProcessingJob(Base):
    """Postgres-backed queue. No PHI, no RLS: the worker claims across tenants."""

    __tablename__ = "processing_job"
    id: Mapped[uuid.UUID] = pk()
    clinic_id: Mapped[uuid.UUID] = uuid_col()
    session_id: Mapped[uuid.UUID | None] = uuid_col(nullable=True)
    kind: Mapped[str] = mapped_column(pg_enum("job_kind"))
    status: Mapped[str] = mapped_column(pg_enum("job_status"), server_default="queued")
    attempts: Mapped[int] = mapped_column(server_default="0")
    max_attempts: Mapped[int] = mapped_column(server_default="5")
    consent_epoch: Mapped[int] = mapped_column(server_default="0")
    usage: Mapped[dict[str, Any]] = mapped_column(JSONB, server_default=sa.text("'{}'::jsonb"))
    run_after: Mapped[datetime] = mapped_column(
        sa.DateTime(timezone=True), server_default=sa.func.now()
    )
    locked_at: Mapped[datetime | None]
    locked_by: Mapped[str | None]
    last_error: Mapped[str | None]
    created_at: Mapped[datetime] = ts_now()
    updated_at: Mapped[datetime] = ts_now()

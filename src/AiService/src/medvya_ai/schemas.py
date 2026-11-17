"""Pydantic v2 request/response models (the shared API contract)."""

import uuid
from datetime import date, datetime
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, StringConstraints, model_validator

ParticipantRole = Literal["doctor", "patient", "companion", "interpreter"]
ConsentStatus = Literal["granted", "refused", "withdrawn"]


class ParticipantIn(BaseModel):
    role: ParticipantRole
    display_name: str = Field(min_length=1, max_length=120)


class ParticipantOut(BaseModel):
    model_config = ConfigDict(from_attributes=True)
    id: uuid.UUID
    role: str
    display_name: str
    speaker_label: str | None = None


class SessionCreate(BaseModel):
    patient_ref: uuid.UUID  # the Medvya encounter id (ADR-021 section 4), never a name
    specialty: str | None = Field(default=None, max_length=120)
    scheduled_date: date
    participants: list[ParticipantIn] = Field(min_length=1, max_length=3)


class ConsentState(BaseModel):
    participant_id: uuid.UUID
    status: str


class SegmentState(BaseModel):
    segment_no: int
    state: str
    chunk_count: int | None


class JobState(BaseModel):
    kind: str
    status: str


class SessionOut(BaseModel):
    id: uuid.UUID
    state: str
    mode: Literal["recorded", "manual"] | str
    recording_allowed: bool
    consent_epoch: int
    revoked_at: datetime | None
    patient_ref: uuid.UUID
    participants: list[ParticipantOut]
    consent: list[ConsentState]
    segments: list[SegmentState]
    jobs: list[JobState]
    created_at: datetime


class ConsentIn(BaseModel):
    participant_id: uuid.UUID
    status: ConsentStatus
    method: Literal["verbal_attested", "digital_signature"]
    on_behalf_of: Literal["self", "guardian", "representative"]
    notice_version: str = Field(max_length=40)
    # Who consented for the participant (guardian of a minor, legal representative). Required
    # unless on_behalf_of is self. No identity verification is claimed; the clinician attests.
    representative_name: str | None = Field(default=None, min_length=1, max_length=120)
    representative_relationship: str | None = Field(default=None, min_length=1, max_length=60)

    @model_validator(mode="after")
    def _representative_details(self) -> "ConsentIn":
        if self.on_behalf_of != "self" and not (
            self.representative_name and self.representative_relationship
        ):
            raise ValueError("representative_name and representative_relationship are required")
        return self


class ConsentOut(BaseModel):
    id: uuid.UUID
    participant_id: uuid.UUID
    status: str
    method: str
    on_behalf_of: str
    notice_version: str
    notice_locale: str
    representative_name: str | None
    representative_relationship: str | None
    created_at: datetime
    recording_allowed: bool
    session_state: str


class NoticeOut(BaseModel):
    version: str
    locale: str
    text: str


MAX_INDEX = 100_000  # segment_no and chunk seq upper bound


class SegmentIn(BaseModel):
    segment_no: int = Field(ge=0, le=MAX_INDEX)
    mime_type: str = Field(pattern=r"^audio/[\w.+-]+(;\s*codecs=[\w.,\"' -]+)?$", max_length=100)
    start_offset_ms: int = Field(ge=0)


class SegmentOut(BaseModel):
    id: uuid.UUID
    segment_no: int
    mime_type: str
    start_offset_ms: int
    state: str
    chunk_count: int | None
    duration_ms: int | None


class ClientEvent(BaseModel):
    t_ms: int = Field(ge=0)
    type: str = Field(max_length=40)
    detail: str | None = Field(default=None, max_length=200)


class SegmentComplete(BaseModel):
    chunk_count: int = Field(ge=0)
    duration_ms: int = Field(ge=0)
    client_events: list[ClientEvent] = Field(default_factory=list, max_length=1000)


class ChunkOut(BaseModel):
    status: Literal["stored", "duplicate"]


class FinishOut(BaseModel):
    state: str


# ---- review / result (B6) ----------------------------------------------------------


class NoteContent(BaseModel):
    """Note revision content (jsonb). Unknown keys are rejected so a PATCH cannot smuggle fields."""

    model_config = ConfigDict(extra="forbid")
    chief_complaint: str = Field(default="", max_length=10_000)
    hpi: str = Field(default="", max_length=10_000)
    subjective: str = Field(default="", max_length=10_000)
    objective: str = Field(default="", max_length=10_000)
    assessment: str = Field(default="", max_length=10_000)
    plan: str = Field(default="", max_length=10_000)
    summary: str = Field(default="", max_length=10_000)
    missing_or_uncertain: list[str] = Field(default_factory=list, max_length=100)


class NoteOut(BaseModel):
    id: uuid.UUID
    status: str
    rev_no: int
    content: NoteContent
    unverified: bool = True  # AI-generated text is not checked against the transcript


class NoteEditIn(BaseModel):
    content: NoteContent


class ManualNoteIn(BaseModel):
    """Clinician-written note when recording was refused or not started (session.mode=manual)."""

    content: NoteContent = Field(default_factory=NoteContent)
    template: Literal["soap", "concise"] = "soap"


class RevisionOut(BaseModel):
    rev_no: int
    author_type: str
    author_id: uuid.UUID | None
    created_at: datetime
    content: NoteContent


class RevisionsOut(BaseModel):
    note_id: uuid.UUID
    status: str
    template: str
    revisions: list[RevisionOut]  # oldest first; append-only in the database


class EvidenceOut(BaseModel):
    segment_id: uuid.UUID
    quote: str
    speaker_label: str | None = None  # who said it (clinician-mapped label when available)
    start_ms: int | None = None
    low_confidence: bool = False


class MedicationOut(BaseModel):
    name_as_spoken: str
    generic_name: str | None
    strength_value: float | None
    strength_unit: str | None
    dose_text: str | None
    frequency_code: str | None
    frequency_text: str | None
    duration_value: int | None
    duration_unit: str | None
    route: str | None
    instructions: str | None


class FactOut(BaseModel):
    id: uuid.UUID
    category: str
    label: str
    polarity: str
    body_site: str | None
    value_numeric: float | None
    value_unit: str | None
    detail: str | None
    review_state: str
    evidence: list[EvidenceOut]
    medication: MedicationOut | None = None
    # Provenance, derived from the cited turn: participant role when the speaker label is mapped
    # to a participant, else the raw label ("Speaker 2", "unknown"). Never inferred from content.
    reported_by: str | None = None


class TranscriptOut(BaseModel):
    id: uuid.UUID
    idx: int
    speaker_label: str
    start_ms: int
    end_ms: int
    language: str
    original_text: str  # immutable ASR output; fact evidence quotes point here
    text: str  # latest clinician correction, else original_text
    english_text: str | None
    edited: bool
    confidence: float | None = None
    low_confidence: bool = False  # provider confidence under LOW_CONFIDENCE
    provider_speaker: str  # diarization cluster as received; speaker_label may be an override
    suggested_role: str | None = None  # content-based suggestion, pending clinician confirmation
    role_confidence: float | None = None


class TranscriptCorrectionIn(BaseModel):
    """A new text, a speaker override for this one phrase, or both."""

    corrected_text: (
        Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=5000)]
        | None
    ) = None
    speaker_label: (
        Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=60)] | None
    ) = None
    reason: str | None = Field(default=None, max_length=200)

    @model_validator(mode="after")
    def _something_to_change(self) -> "TranscriptCorrectionIn":
        if self.corrected_text is None and self.speaker_label is None:
            raise ValueError("corrected_text or speaker_label is required")
        return self


class ApplyRolesOut(BaseModel):
    applied: int  # phrases whose speaker now carries the suggested role
    skipped_low_confidence: int


class ReprocessOut(BaseModel):
    state: str


class TranscriptCorrectionOut(BaseModel):
    turn: TranscriptOut
    affected_fact_ids: list[uuid.UUID]  # facts whose evidence cites this turn: re-review them


class QualityOut(BaseModel):
    """Conversation-processing quality signals for the reviewing clinician."""

    phrases: int
    unknown_speaker_phrases: int
    low_confidence_phrases: int
    edited_phrases: int
    languages: dict[str, int]
    duration_ms: int
    facts_uncertain: int
    facts_rejected_by_gate: int


class ResultOut(BaseModel):
    state: str
    label: Literal["DRAFT", "APPROVED"]
    summary: str
    summary_unverified: bool = True
    note: NoteOut
    facts: list[FactOut]
    transcript: list[TranscriptOut]
    participants: list[ParticipantOut]
    quality: QualityOut


class SpeakerRelabelIn(BaseModel):
    speaker_label: str = Field(min_length=1, max_length=60)
    participant_id: uuid.UUID | None = None
    new_label: str = Field(min_length=1, max_length=60)


class FactReviewIn(BaseModel):
    review_state: Literal["confirmed", "rejected"]

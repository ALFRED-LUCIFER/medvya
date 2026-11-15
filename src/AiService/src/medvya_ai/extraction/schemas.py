"""Pydantic models used both as strict `response_format` and as the parsed result.

Foundry strict structured outputs (provider-notes.md section 2): every field required (optional =
union with null), additionalProperties false, no minLength/minimum/pattern etc. Bounds are
validated in code (persist.py), not in the schema. tests/unit/test_extraction_schema.py enforces it.
"""

from typing import Literal

from pydantic import BaseModel, ConfigDict

Category = Literal[
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
]
Polarity = Literal["present", "absent", "uncertain"]
FrequencyCode = Literal["OD", "BD", "TDS", "QID", "HS", "SOS", "STAT", "PRN", "OTHER"]
DurationUnit = Literal["day", "week", "month"]
Route = Literal["oral", "topical", "iv", "im", "inhalation", "other"]


class Strict(BaseModel):
    model_config = ConfigDict(extra="forbid")


SpeakerRole = Literal["doctor", "patient", "companion", "interpreter", "unknown"]


class NormItem(Strict):
    idx: int
    english_text: str
    role: SpeakerRole  # suggested from content; the clinician confirms
    role_confidence: float


class NormaliseOut(Strict):
    items: list[NormItem]


class EvidenceIn(Strict):
    segment_idx: int
    quote: str


class MedicationIn(Strict):
    name_as_spoken: str
    generic_name: str | None
    strength_value: float | None
    strength_unit: str | None
    dose_text: str | None
    frequency_code: FrequencyCode | None
    frequency_text: str | None
    duration_value: int | None
    duration_unit: DurationUnit | None
    route: Route | None
    instructions: str | None


class FactIn(Strict):
    category: Category
    label: str
    polarity: Polarity
    body_site: str | None
    value_numeric: float | None
    value_unit: str | None
    detail: str | None
    confidence: float | None
    evidence: list[EvidenceIn]
    medication: MedicationIn | None


class SoapIn(Strict):
    chief_complaint: str
    hpi: str
    subjective: str
    objective: str
    assessment: str
    plan: str
    summary: str


class ExtractionOut(Strict):
    note: SoapIn
    facts: list[FactIn]
    missing_or_uncertain: list[str]

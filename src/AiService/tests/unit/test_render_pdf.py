import uuid
from datetime import UTC, date, datetime

from medvya_ai.schemas import FactOut, MedicationOut, NoteContent
from medvya_ai.services.pdf import Document, build_pdf
from medvya_ai.services.render import concise_text, fact_line, soap_sections


def _fact(label: str, cat: str = "symptom", **kw: object) -> FactOut:
    base: dict[str, object] = {
        "id": uuid.uuid4(),
        "category": cat,
        "label": label,
        "polarity": "present",
        "body_site": None,
        "value_numeric": None,
        "value_unit": None,
        "detail": None,
        "review_state": "ai",
        "evidence": [],
    }
    return FactOut(**{**base, **kw})  # type: ignore[arg-type]


MED = MedicationOut(
    name_as_spoken="Dolo",
    generic_name="paracetamol",
    strength_value=650,
    strength_unit="mg",
    dose_text="1 tablet",
    frequency_code="TDS",
    frequency_text=None,
    duration_value=3,
    duration_unit="day",
    route="oral",
    instructions="after food",
)
FACTS = [
    _fact("fever"),
    _fact("chest pain", polarity="absent"),
    _fact("penicillin", "allergy", polarity="uncertain"),
    _fact("temperature", "vital", value_numeric=101.5, value_unit="F"),
    _fact("Dolo", "medication", medication=MED),
    _fact("hallucinated", "diagnosis", review_state="rejected"),
]
NOTE = NoteContent(
    subjective="बुखार दो दिन से",
    plan="Paracetamol",
    summary="Fever two days.",
    missing_or_uncertain=["penicillin: stated as present but negated in the transcript"],
)


def test_fact_lines_mark_polarity_values_and_medication_details() -> None:
    lines = [fact_line(f) for f in FACTS[:5]]
    assert lines[0] == "fever"
    assert lines[1] == "No chest pain"
    assert lines[2] == "penicillin (uncertain)"
    assert lines[3] == "temperature 101.5 F"
    assert lines[4] == "Dolo 650 mg, 1 tablet, TDS, 3 days, oral, after food"


def test_concise_text_groups_by_category_and_drops_rejected() -> None:
    text = concise_text(NOTE, FACTS)
    assert text.startswith("Fever two days.")
    assert "Medications\n- Dolo 650 mg" in text and "Allergies\n- penicillin (uncertain)" in text
    assert "hallucinated" not in text and "To confirm\n- penicillin:" in text
    assert soap_sections(NOTE) == [("Subjective", "बुखार दो दिन से"), ("Plan", "Paracetamol")]


def test_pdf_renders_devanagari_and_carries_hash() -> None:
    doc = Document(
        clinic_name="Clinic",
        clinician_name="Dr A",
        patient_ref="P-1",
        specialty=None,
        scheduled_date=date(2026, 11, 9),
        approved_at=datetime(2026, 11, 9, 10, tzinfo=UTC),
        rev_no=3,
        template="soap",
        content=NOTE,
        facts=FACTS,
        content_hash="a" * 64,
    )
    pdf = build_pdf(doc)
    assert pdf.startswith(b"%PDF") and b"/Count 1" in pdf
    # shaped Devanagari is written as glyph ids, so the raw conjunct string must not appear
    assert "बुखार".encode() not in pdf
    concise = build_pdf(Document(**{**doc.__dict__, "template": "concise"}))
    assert concise.startswith(b"%PDF") and len(concise) > 1000

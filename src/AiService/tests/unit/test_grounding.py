"""F2: values beside the verified-evidence badge must be supported by the cited text."""

import uuid
from types import SimpleNamespace
from typing import Any

import pytest

from medvya_ai.extraction.grounding import has_number, has_term
from medvya_ai.extraction.persist import _medication, persist_extraction
from medvya_ai.extraction.schemas import (
    EvidenceIn,
    ExtractionOut,
    FactIn,
    MedicationIn,
    SoapIn,
)
from medvya_ai.models import ClinicalFact, ExtractionRejection, Medication

CITED = "take paracetamol 500 mg twice a day for 3 days"


def test_has_term_word_boundary() -> None:
    assert has_term(CITED, "Paracetamol")
    assert not has_term(CITED, "ibuprofen")
    assert not has_term(CITED, "para")
    assert not has_term(CITED, None) and not has_term(CITED, "  ")


@pytest.mark.parametrize(
    ("value", "ok"),
    [(500, True), (500.0, True), (50, False), (5, False), (3, True), (2.5, False), (None, False)],
)
def test_has_number(value: float | None, ok: bool) -> None:
    assert has_number(CITED, value) is ok


def test_has_number_decimal() -> None:
    assert has_number("temperature is 101.5 f", 101.5)
    assert not has_number("temperature is 101.5 f", 101)
    assert not has_number("temperature is 101.5 f", 1.5)


def _med(**kw: Any) -> MedicationIn:
    base: dict[str, Any] = {
        "name_as_spoken": "paracetamol",
        "generic_name": "paracetamol",
        "strength_value": 500.0,
        "strength_unit": "mg",
        "dose_text": "one tablet",
        "frequency_code": "TDS",
        "frequency_text": "thrice a day",
        "duration_value": 7,
        "duration_unit": "day",
        "route": "iv",
        "instructions": "after food",
    }
    return MedicationIn(**{**base, **kw})


def test_medication_unsupported_fields_are_nulled() -> None:
    row = _medication(uuid.uuid4(), uuid.uuid4(), "paracetamol", _med(), CITED)
    assert row.strength_value == 500 and row.strength_unit == "mg"  # in the text
    assert row.dose_text is None  # "one tablet" not said
    assert row.frequency_text is None and row.frequency_code == "BD"  # derived from "twice"
    assert row.duration_value is None and row.duration_unit is None  # 7 not said
    assert row.route is None  # model said iv; nothing in the text
    assert row.instructions is None


def test_medication_wrong_strength_is_nulled() -> None:
    row = _medication(uuid.uuid4(), uuid.uuid4(), "x", _med(strength_value=650.0), CITED)
    assert row.strength_value is None and row.strength_unit is None


class _Scalars:
    def __init__(self, rows: list[Any]) -> None:
        self.rows = rows

    def all(self) -> list[Any]:
        return self.rows


class _FakeDB:
    def __init__(self, rows: list[Any]) -> None:
        self.rows, self.added = rows, []

    async def scalars(self, *a: object) -> _Scalars:
        return _Scalars(self.rows)

    def add(self, o: object) -> None:
        self.added.append(o)

    async def flush(self) -> None:
        return None

    async def execute(self, *a: object) -> None:
        return None

    async def scalar(self, *a: object) -> None:
        return None  # no existing note: first extraction


def _fact(label: str, quote: str, cat: str = "vital", **kw: Any) -> FactIn:
    base: dict[str, Any] = {
        "category": cat,
        "label": label,
        "polarity": "present",
        "body_site": None,
        "value_numeric": None,
        "value_unit": None,
        "detail": None,
        "confidence": 0.9,
        "evidence": [EvidenceIn(segment_idx=0, quote=quote)],
        "medication": None,
    }
    return FactIn(**{**base, **kw})


async def test_persist_grounds_values_and_rejects_unsupported_medication() -> None:
    seg = SimpleNamespace(
        idx=0, id=uuid.uuid4(), english_text=CITED + ". temperature is 101", original_text="o"
    )
    soap = SoapIn(
        chief_complaint="c",
        hpi="h",
        subjective="s",
        objective="o",
        assessment="a",
        plan="p",
        summary="x",
    )
    out = ExtractionOut(
        note=soap,
        facts=[
            _fact("temperature", "temperature is 101", value_numeric=101.0, value_unit="f"),
            _fact("pulse", "temperature is 101", value_numeric=88.0, value_unit="bpm"),
            _fact(
                "ibuprofen",
                "take paracetamol",
                cat="medication",
                medication=_med(name_as_spoken="ibuprofen"),
            ),
            _fact("paracetamol", "paracetamol 500 mg", cat="medication", medication=_med()),
        ],
        missing_or_uncertain=[],
    )
    db = _FakeDB([seg])
    counts = await persist_extraction(db, clinic_id=uuid.uuid4(), session_id=uuid.uuid4(), out=out)  # type: ignore[arg-type]
    facts = {f.label: f for f in db.added if isinstance(f, ClinicalFact)}
    assert set(facts) == {"temperature", "pulse", "paracetamol"}
    assert facts["temperature"].value_numeric == 101 and facts["temperature"].value_unit == "°F"
    assert facts["pulse"].value_numeric is None and facts["pulse"].value_unit is None
    rej = [r for r in db.added if isinstance(r, ExtractionRejection)]
    assert [r.reason for r in rej] == ["QUOTE_NOT_FOUND"] and counts.rejected == 1
    meds = [m for m in db.added if isinstance(m, Medication)]
    assert len(meds) == 1 and meds[0].dose_text is None


async def test_assertion_label_must_be_named_in_the_cited_text() -> None:
    """ "Any allergies?" is a real quote, yet it does not back "allergy: penicillin"."""
    # SAFETY: mutation check: removed the ASSERTION_CATEGORIES has_term check in persist.py ->
    # this test failed
    seg = SimpleNamespace(
        idx=0,
        id=uuid.uuid4(),
        english_text="Any allergies? Yes, a penicillin allergy. Take crocin twice a day.",
        original_text="o",
    )
    soap = SoapIn(
        chief_complaint="", hpi="", subjective="", objective="", assessment="", plan="", summary=""
    )
    out = ExtractionOut(
        note=soap,
        facts=[
            _fact("penicillin", "Any allergies?", cat="allergy"),
            _fact("sulfa", "Any allergies?", cat="allergy", polarity="absent"),
            _fact("penicillin allergy", "a penicillin allergy", cat="allergy"),
            _fact("diabetes", "Any allergies?", cat="history"),
            _fact(
                "crocin",
                "Take crocin twice a day",
                cat="medication",
                medication=_med(name_as_spoken="crocin", generic_name="Paracetamol"),
            ),
        ],
        missing_or_uncertain=[],
    )
    db = _FakeDB([seg])
    counts = await persist_extraction(db, clinic_id=uuid.uuid4(), session_id=uuid.uuid4(), out=out)  # type: ignore[arg-type]
    pol = {f.label: f.polarity for f in db.added if isinstance(f, ClinicalFact)}
    assert pol == {
        "penicillin": "uncertain",
        "sulfa": "uncertain",
        "penicillin allergy": "present",
        "diabetes": "uncertain",
        "crocin": "present",
    }
    assert counts.grounding_flagged == 3
    (rev,) = [r for r in db.added if type(r).__name__ == "NoteRevision"]
    assert (
        "penicillin: not named in the quoted transcript text" in rev.content["missing_or_uncertain"]
    )
    (med,) = [m for m in db.added if isinstance(m, Medication)]
    assert med.generic_name is None  # "paracetamol" was never said: the model's guess is not stored


async def test_note_is_flagged_unverified_until_a_clinician_revises_it(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    from medvya_ai.services import note as note_svc

    rev = SimpleNamespace(rev_no=1, author_type="ai", content={"summary": "s"})

    async def current(*a: object) -> Any:
        return rev

    monkeypatch.setattr(note_svc, "_current_revision", current)
    note = SimpleNamespace(id=uuid.uuid4(), status="draft")
    assert (await note_svc.note_out(None, note)).unverified is True  # type: ignore[arg-type]
    rev.author_type = "clinician"
    assert (await note_svc.note_out(None, note)).unverified is False  # type: ignore[arg-type]

"""Blueprint section 9 negative examples: a negated statement never becomes a present fact."""

import pytest

from medvya_ai.extraction.grounding import negated


@pytest.mark.parametrize(
    ("cited", "term", "expected"),
    [
        ("no penicillin allergy", "penicillin", True),
        ("patient has a penicillin allergy", "penicillin", False),
        ("she is not taking metformin any more", "metformin", True),
        ("she is taking metformin", "metformin", False),
        ("temperature not taken today", "temperature", True),
        ("temperature is 101", "temperature", False),
        ("Mujhe bukhar nahi hai", "bukhar", True),
        ("Mujhe bukhar hai", "bukhar", False),
        ("मुझे बुखार नहीं है", "बुखार", True),
        ("denies chest pain", "chest pain", True),
        ("five milligrams, not fifty", "fifty", True),
        ("paracetamol 5 mg, not 50", "50", True),
        ("paracetamol 50 mg", "50", False),
        # the cue must be close: a "no" four or more words away is not about the term
        ("no I did not come yesterday because of a headache", "headache", False),
        ("", "fever", False),
        ("fever", None, False),
    ],
)
def test_negated(cited: str, term: str | None, expected: bool) -> None:
    assert negated(cited, term) is expected

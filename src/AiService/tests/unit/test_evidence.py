import uuid

import pytest

from medvya_ai.extraction.evidence import SegText, norm, verify_evidence


def _seg(english: str | None, original: str = "orig text") -> SegText:
    return SegText(id=uuid.uuid4(), english_text=english, original_text=original)


SEGS = {
    0: _seg("The patient has had fever for two days.", "Mujhe do din se bukhar hai"),
    1: _seg("Take Paracetamol  500 mg\ntwice a day."),
}


def test_norm_collapses_whitespace_and_case() -> None:
    assert norm("  Fever\n for   TWO days ") == "fever for two days"


@pytest.mark.parametrize(
    ("idx", "quote"),
    [
        (0, "fever for two days"),  # exact substring
        (0, "FEVER   FOR two DAYS"),  # case + whitespace normalised
        (1, "paracetamol 500 mg twice a day"),  # newline in source
        (0, "do din se bukhar"),  # original_text also accepted
    ],
)
def test_valid_quotes(idx: int, quote: str) -> None:
    assert verify_evidence(SEGS, [(idx, quote)]).reason is None


@pytest.mark.parametrize(
    ("evidence", "reason"),
    [
        ([(0, "severe chest pain")], "QUOTE_NOT_FOUND"),  # fabricated
        ([(1, "fever for two days")], "QUOTE_NOT_FOUND"),  # real text, wrong segment
        ([(99, "fever")], "SEGMENT_UNKNOWN"),
        ([(0, "   ")], "QUOTE_NOT_FOUND"),  # empty quote is never evidence
        ([], "QUOTE_NOT_FOUND"),  # a fact without evidence is not a fact
    ],
)
def test_invalid_evidence(evidence: list[tuple[int, str]], reason: str) -> None:
    r = verify_evidence(SEGS, evidence)
    assert r.reason == reason and r.accepted == []


def test_partial_evidence_keeps_only_verified_quotes() -> None:
    r = verify_evidence(SEGS, [(0, "fever for two days"), (0, "made up"), (99, "x")])
    assert r.reason is None
    assert [(s.id, q) for s, q in r.accepted] == [(SEGS[0].id, "fever for two days")]
    assert r.dropped == 2


def test_none_english_text_falls_back_to_original() -> None:
    segs = {0: _seg(None, "Sar mein dard hai")}
    assert verify_evidence(segs, [(0, "sar mein dard")]).reason is None


@pytest.mark.parametrize("quote", ["a", "e", "ab", "do"])
def test_tiny_quote_is_not_evidence(quote: str) -> None:
    assert verify_evidence(SEGS, [(0, quote)]).reason == "QUOTE_NOT_FOUND"


@pytest.mark.parametrize("quote", ["ever", "fever for two day", "atient has"])
def test_quote_must_match_on_word_boundaries(quote: str) -> None:
    assert verify_evidence(SEGS, [(0, quote)]).reason == "QUOTE_NOT_FOUND"

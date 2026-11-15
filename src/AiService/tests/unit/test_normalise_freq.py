import pytest

from medvya_ai.extraction.normalise import (
    frequency_code,
    generic_name,
    route_code,
)


@pytest.mark.parametrize(
    ("text", "code"),
    [
        ("OD", "OD"),
        ("once daily", "OD"),
        ("once a day", "OD"),
        ("1-0-0", "OD"),
        ("0-1-0", "OD"),
        ("BD", "BD"),
        ("BID", "BD"),
        ("twice a day", "BD"),
        ("two times a day", "BD"),
        ("1-0-1", "BD"),
        ("TDS", "TDS"),
        ("TID", "TDS"),
        ("thrice daily", "TDS"),
        ("three times a day", "TDS"),
        ("1-1-1", "TDS"),
        ("QID", "QID"),
        ("four times a day", "QID"),
        ("HS", "HS"),
        ("at bedtime", "HS"),
        ("at night", "HS"),
        ("0-0-1", "HS"),
        ("SOS", "SOS"),
        ("STAT", "STAT"),
        ("PRN", "PRN"),
        ("as needed", "PRN"),
        ("every other day", "OTHER"),
        ("weekly", "OTHER"),
    ],
)
def test_frequency_code(text: str, code: str) -> None:
    assert frequency_code(text) == code


def test_frequency_none_for_empty() -> None:
    assert frequency_code(None) is None and frequency_code("  ") is None


@pytest.mark.parametrize(
    ("text", "route"),
    [
        ("oral", "oral"),
        ("by mouth", "oral"),
        ("PO", "oral"),
        ("topical", "topical"),
        ("intravenous", "iv"),
        ("IM", "im"),
        ("inhaler", "inhalation"),
        ("rectal", "other"),
    ],
)
def test_route_code(text: str, route: str) -> None:
    assert route_code(text) == route


def test_generic_name_is_lowercase_trimmed() -> None:
    assert generic_name("  Paracetamol ") == "paracetamol"
    assert generic_name("") is None and generic_name(None) is None


@pytest.mark.parametrize(
    ("spoken", "code"),
    [
        ("milligrams", "mg"),
        ("Milligram", "mg"),
        ("degree Fahrenheit", "°F"),
        ("degrees celsius", "°C"),
        ("kilos", "kg"),
        ("mmHg", "mmHg"),
        ("tablets", "tablets"),  # unknown units are kept as said
        (None, None),
        ("  ", None),
    ],
)
def test_unit_code(spoken: str | None, code: str | None) -> None:
    from medvya_ai.extraction.normalise import unit_code

    assert unit_code(spoken) == code

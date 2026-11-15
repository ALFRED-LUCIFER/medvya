"""Support checks: a model-supplied value is kept only if the cited transcript text backs it."""

import math
import re
from decimal import Decimal

from medvya_ai.extraction.evidence import contains, norm

# Negation cues in English, romanised Hindi/Marathi and Devanagari. A cue within WINDOW words
# before the term ("no penicillin allergy", "not taking metformin", "bukhar nahi hai" has it after)
# or right after it ("temperature not taken") makes a stated fact uncertain, never "absent".
_NEG = (
    r"no|not|never|none|without|denies|denied|deny|nor|n't|"
    r"nahi|nahin|nhi|nahee|mat|na|"
    r"नहीं|नही|ना|मत"
)
_WORD = r"[^\s,.;:!?]+"
WINDOW = 4


def negated(cited: str, term: str | None) -> bool:
    """True when the term is explicitly negated in the cited text."""
    needle = norm(term or "")
    if not needle:
        return False
    hay = norm(cited)
    before = rf"(?:(?:{_NEG})\s+(?:{_WORD}\s+){{0,{WINDOW - 1}}})"
    after = rf"(?:\s+(?:{_WORD}\s+){{0,{WINDOW - 1}}}(?:{_NEG})(?!\w))"
    pattern = rf"(?<!\w)(?:{before}{re.escape(needle)}(?!\w)|{re.escape(needle)}(?!\w){after})"
    return re.search(pattern, hay) is not None


def has_term(cited: str, term: str | None) -> bool:
    needle = norm(term or "")
    return bool(needle) and contains(norm(cited), needle)


def has_number(cited: str, value: float | None) -> bool:
    """True if the number appears as a whole numeral (50 is not backed by 500 or 101.5)."""
    if value is None or not math.isfinite(value):
        return False
    s = format(Decimal(str(value)).normalize(), "f")
    return re.search(rf"(?<![\d.]){re.escape(s)}(?!\d|\.\d)", cited) is not None

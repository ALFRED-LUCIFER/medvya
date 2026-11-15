"""Evidence verification: a fact exists only if its quote is found in the cited segment."""

import re
import uuid
from collections.abc import Mapping, Sequence
from dataclasses import dataclass, field


@dataclass(frozen=True)
class SegText:
    id: uuid.UUID
    english_text: str | None
    original_text: str


@dataclass
class Verified:
    accepted: list[tuple[SegText, str]] = field(default_factory=list)
    dropped: int = 0
    reason: str | None = None  # None = at least one quote verified


MIN_QUOTE_CHARS = 4  # a one- or two-letter "quote" matches almost any segment


def norm(s: str) -> str:
    return " ".join(s.split()).casefold()


def contains(haystack: str, needle: str) -> bool:
    """Word-boundary substring test on already-normalised text."""
    return re.search(rf"(?<!\w){re.escape(needle)}(?!\w)", haystack) is not None


def verify_evidence(
    segments: Mapping[int, SegText], evidence: Sequence[tuple[int, str]]
) -> Verified:
    """Keeps verified (segment, quote) pairs. reason is set only when none survive:
    SEGMENT_UNKNOWN if the first failure was an unknown segment, else QUOTE_NOT_FOUND."""
    result = Verified()
    first_failure: str | None = None
    for idx, quote in evidence:
        seg = segments.get(idx)
        needle = norm(quote)
        if seg is None:
            failure = "SEGMENT_UNKNOWN"
        elif len(needle) < MIN_QUOTE_CHARS or not any(
            contains(norm(text), needle) for text in (seg.english_text or "", seg.original_text)
        ):
            failure = "QUOTE_NOT_FOUND"
        else:
            result.accepted.append((seg, quote.strip()))
            continue
        result.dropped += 1
        first_failure = first_failure or failure
    if not result.accepted:
        result.reason = first_failure or "QUOTE_NOT_FOUND"
    return result

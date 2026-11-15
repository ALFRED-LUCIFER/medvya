"""Normalisers (frequency, route, generic name) and the normalise step: English rendering plus a
content-based speaker-role suggestion for every phrase (see prompts.NORMALISE_SYSTEM)."""

import logging
import re
import uuid
from collections.abc import Awaitable, Callable, Sequence
from decimal import Decimal

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from medvya_ai.extraction import prompts
from medvya_ai.extraction.schemas import NormaliseOut, NormItem
from medvya_ai.models import Participant, TranscriptSegment
from medvya_ai.providers.errors import PipelineError, ProviderError
from medvya_ai.providers.llm.base import LLMClient

_FREQ: tuple[tuple[str, str], ...] = (
    (r"\bstat\b|\bimmediately\b", "STAT"),
    (r"\bsos\b", "SOS"),
    (r"\bprn\b|\bas needed\b|\bwhen needed\b", "PRN"),
    (r"\bqid\b|\bqds\b|\bfour times\b|\b4 times\b", "QID"),
    (r"\btds\b|\btid\b|\bthrice\b|\bthree times\b|\b3 times\b|\bthree times a day\b", "TDS"),
    (r"\bbd\b|\bbid\b|\btwice\b|\btwo times\b|\b2 times\b", "BD"),
    (r"\bhs\b|\bbedtime\b|\bat night\b|\bnightly\b", "HS"),
    (r"\bod\b|\bonce\b|\bdaily\b|\bevery day\b|\bone time\b", "OD"),
)
_PATTERN = re.compile(r"^\s*([01])\s*-\s*([01])\s*-\s*([01])\s*$")  # morning-noon-night (India)
_PATTERN_CODES = {
    "100": "OD",
    "010": "OD",
    "001": "HS",
    "101": "BD",
    "011": "BD",
    "110": "BD",
    "111": "TDS",
}
_ROUTE: tuple[tuple[str, str], ...] = (
    (r"\bintravenous\b|\biv\b", "iv"),
    (r"\bintramuscular\b|\bim\b", "im"),
    (r"\binhal\w*|\bnebuli[sz]\w*", "inhalation"),
    (r"\btopical\w*|\bapply\b|\bointment\b|\bcream\b", "topical"),
    (r"\boral\w*|\bby mouth\b|\bpo\b|\bswallow\b", "oral"),
)


def frequency_code(text: str | None) -> str | None:
    if text is None or not text.strip():
        return None
    m = _PATTERN.match(text)
    if m:
        return _PATTERN_CODES.get("".join(m.groups()), "OTHER")
    low = text.lower()
    if re.search(r"\bweek|\bmonth|\balternate|\bother day", low):
        return "OTHER"
    for pattern, code in _FREQ:
        if re.search(pattern, low):
            return code
    return "OTHER"


def route_code(text: str | None) -> str | None:
    if text is None or not text.strip():
        return None
    low = text.lower()
    for pattern, code in _ROUTE:
        if re.search(pattern, low):
            return code
    return "other"


_UNITS: tuple[tuple[str, str], ...] = (
    (r"^(milligrams?|mgs?)$", "mg"),
    (r"^(micrograms?|mcg|µg|ug)$", "mcg"),
    (r"^(grams?|gm|gms)$", "g"),
    (r"^(millilitres?|milliliters?|mls?)$", "ml"),
    (r"^(degrees?\s*)?(fahrenheit|f|°f)$", "°F"),
    (r"^(degrees?\s*)?(celsius|centigrade|c|°c)$", "°C"),
    (r"^(kilograms?|kgs?|kilos?)$", "kg"),
    (r"^(mm\s*hg|mmhg)$", "mmHg"),
    (r"^(beats? per minute|bpm)$", "bpm"),
    (r"^(percent|%)$", "%"),
)


def unit_code(text: str | None) -> str | None:
    """Spoken unit -> conventional symbol; anything unrecognised is kept as said."""
    if text is None or not text.strip():
        return None
    low = " ".join(text.lower().split())
    for pattern, code in _UNITS:
        if re.match(pattern, low):
            return code
    return text.strip()


def generic_name(name: str | None) -> str | None:
    cleaned = (name or "").strip().lower()
    return cleaned or None


log = logging.getLogger(__name__)
Item = tuple[int, str, str]  # idx, speaker label, text
CONTEXT_PHRASES = 8  # tail of the previous batch shown read-only so roles stay consistent


def batches(items: Sequence[Item], max_items: int = 40, max_chars: int = 8000) -> list[list[Item]]:
    out: list[list[Item]] = []
    cur: list[Item] = []
    size = 0
    for it in items:
        if cur and (len(cur) >= max_items or size + len(it[2]) > max_chars):
            out.append(cur)
            cur, size = [], 0
        cur.append(it)
        size += len(it[2])
    if cur:
        out.append(cur)
    return out


async def _noop() -> None:
    return None


async def _normalise_batch(
    llm: LLMClient,
    batch: list[Item],
    roster: list[tuple[str, str]],
    previous: list[Item],
) -> dict[int, NormItem | None]:
    """One model call; when the provider's content filter refuses the batch, split it so a single
    phrase cannot block the whole session. A refused single phrase maps to None."""
    try:
        out = await llm.parse(
            "normalise",
            system=prompts.NORMALISE_SYSTEM,
            user=prompts.normalise_user(batch, roster, previous[-CONTEXT_PHRASES:]),
            schema=NormaliseOut,
        )
    except ProviderError as exc:
        if exc.code != "LLM_CONTENT_FILTER":
            raise
        if len(batch) == 1:
            return {batch[0][0]: None}
        half = len(batch) // 2
        first = await _normalise_batch(llm, batch[:half], roster, previous)
        second = await _normalise_batch(llm, batch[half:], roster, batch[:half])
        return {**first, **second}
    got: dict[int, NormItem | None] = {i.idx: i for i in out.items}
    if (
        len(out.items) != len(batch)
        or set(got) != {i for i, _, _ in batch}
        or not all(i.english_text.strip() for i in out.items)
    ):
        raise PipelineError("NORMALISE_IDS_MISMATCH")  # ids must be preserved, text non-empty
    return got


async def run_normalise(
    db: AsyncSession,
    llm: LLMClient,
    session_id: uuid.UUID,
    before_call: Callable[[], Awaitable[None]] = _noop,
) -> int:
    """Every phrase goes through the model: English text (ASR term repair allowed only when
    unambiguous) and a suggested speaker role with confidence. Returns the number of phrases."""
    rows = (
        await db.scalars(
            select(TranscriptSegment)
            .where(TranscriptSegment.session_id == session_id)
            .order_by(TranscriptSegment.idx)
        )
    ).all()
    if not rows:
        raise PipelineError("NO_TRANSCRIPT")
    roster = [
        (p.role, p.display_name)
        for p in (
            await db.scalars(
                select(Participant)
                .where(Participant.session_id == session_id)
                .order_by(Participant.id)
            )
        ).all()
    ]
    items: list[Item] = [(r.idx, r.speaker_label, r.original_text) for r in rows]
    by_idx = {r.idx: r for r in rows}
    filtered = 0
    previous: list[Item] = []
    for batch in batches(items):
        await before_call()
        for idx, item in (await _normalise_batch(llm, batch, roster, previous)).items():
            row = by_idx[idx]
            if item is None:  # content filter refused this single phrase: keep it as heard
                row.english_text, row.suggested_role, row.role_confidence = (
                    row.original_text,
                    "unknown",
                    Decimal(0),
                )
                filtered += 1
                continue
            row.english_text = item.english_text.strip()
            row.suggested_role = item.role
            row.role_confidence = Decimal(str(min(max(item.role_confidence, 0.0), 1.0)))
        previous = batch
    if filtered:
        log.warning(
            "normalise: %d phrase(s) refused by the content filter, kept as heard", filtered
        )
    await db.flush()
    return len(rows)

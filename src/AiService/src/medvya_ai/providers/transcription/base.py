"""Transcription provider Protocol and its domain object."""

from collections.abc import Sequence
from dataclasses import dataclass
from typing import Protocol


@dataclass(frozen=True)
class Phrase:
    """One diarized phrase, offsets relative to the start of the audio file sent."""

    offset_ms: int
    duration_ms: int
    text: str
    speaker: int | None
    locale: str | None
    confidence: float | None


UNKNOWN_SPEAKER = "unknown"


def speaker_label(speaker: int | None, *, part: int | None = None) -> str:
    """Provider speaker numbers are anonymous and per request. No number -> "unknown" (never a
    guess). With several recording segments the label carries the part, because "Speaker 1" of
    part 2 is not known to be "Speaker 1" of part 1 until the clinician maps both."""
    if speaker is None:
        return UNKNOWN_SPEAKER
    return f"Speaker {speaker}" if part is None else f"Speaker {speaker} (part {part})"


class Transcriber(Protocol):
    async def transcribe(
        self, audio: bytes, *, mime_type: str, locales: Sequence[str], max_speakers: int
    ) -> list[Phrase]: ...

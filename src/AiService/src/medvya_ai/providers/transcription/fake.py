"""Deterministic provider for tests and local runs (TRANSCRIPTION_PROVIDER=fake)."""

from collections.abc import Sequence

from medvya_ai.providers.transcription.base import Phrase

DEFAULT_PHRASES = (
    Phrase(0, 2500, "Hello, tell me what is troubling you.", 1, "en-IN", 0.95),
    Phrase(3000, 3000, "Mujhe do din se bukhar hai aur sar mein dard hai.", 2, "hi-IN", 0.85),
    Phrase(
        7000, 3500, "Take tablet paracetamol 500 mg twice a day for three days.", 1, "en-IN", 0.9
    ),
)


class FakeTranscriber:
    def __init__(self, phrases: Sequence[Phrase] = DEFAULT_PHRASES) -> None:
        self._phrases = list(phrases)

    async def transcribe(
        self, audio: bytes, *, mime_type: str, locales: Sequence[str], max_speakers: int
    ) -> list[Phrase]:
        return list(self._phrases)

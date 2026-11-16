"""Azure Speech fast transcription adapter. Facts and sources: docs/provider-notes.md."""

import asyncio
import json
from collections.abc import Awaitable, Callable, Sequence
from typing import Any
from urllib.parse import urlsplit

import httpx

from medvya_ai.providers.errors import BACKOFF_SECONDS, TIMEOUT_SECONDS, ProviderError, retry_delay
from medvya_ai.providers.transcription.base import Phrase

RETRY_STATUS = {429, 500, 502, 503, 504}


def build_definition(locales: Sequence[str], max_speakers: int) -> str:
    return json.dumps(
        {
            "locales": list(locales),
            "diarization": {"enabled": True, "maxSpeakers": max_speakers},
            "profanityFilterMode": "None",  # default "Masked" would corrupt clinical text
        }
    )


def map_response(body: dict[str, Any]) -> list[Phrase]:
    out: list[Phrase] = []
    for p in body.get("phrases", []):
        text = str(p.get("text", "")).strip()
        if not text:
            continue
        speaker = p.get("speaker")
        conf = p.get("confidence")
        out.append(
            Phrase(
                offset_ms=int(p["offsetMilliseconds"]),
                duration_ms=int(p.get("durationMilliseconds", 0)),
                text=text,
                speaker=int(speaker) if speaker is not None else None,
                locale=p.get("locale"),
                confidence=float(conf) if conf is not None else None,
            )
        )
    return out


class AzureFastTranscriber:
    def __init__(
        self,
        *,
        endpoint: str,
        api_key: str,
        api_version: str,
        client: httpx.AsyncClient | None = None,
        sleep: Callable[[float], Awaitable[None]] = asyncio.sleep,
    ) -> None:
        self._url = f"{endpoint.rstrip('/')}/speechtotext/transcriptions:transcribe"
        self.endpoint_host = urlsplit(endpoint).hostname  # recorded on each job
        self._key, self._version, self._sleep = api_key, api_version, sleep
        self._client = client or httpx.AsyncClient(timeout=httpx.Timeout(TIMEOUT_SECONDS))

    async def transcribe(
        self, audio: bytes, *, mime_type: str, locales: Sequence[str], max_speakers: int
    ) -> list[Phrase]:
        definition = build_definition(locales, max_speakers)
        last, delay = "SPEECH_NETWORK", 0.0
        for attempt in range(len(BACKOFF_SECONDS) + 1):
            if attempt:
                await self._sleep(delay)
            default = BACKOFF_SECONDS[min(attempt, len(BACKOFF_SECONDS) - 1)]
            delay = default
            try:
                resp = await self._client.post(
                    self._url,
                    params={"api-version": self._version},
                    headers={"Ocp-Apim-Subscription-Key": self._key},
                    files={"audio": ("audio.webm", audio, mime_type.split(";")[0])},
                    data={"definition": definition},
                )
            except httpx.TransportError:
                last = "SPEECH_NETWORK"
                continue
            if resp.status_code == 200:
                try:
                    return map_response(resp.json())
                except (ValueError, KeyError, TypeError) as exc:
                    raise ProviderError("SPEECH_BAD_RESPONSE") from exc
            last = f"SPEECH_HTTP_{resp.status_code}"
            if resp.status_code not in RETRY_STATUS:
                break
            delay = retry_delay(resp.headers, default)
        raise ProviderError(last)

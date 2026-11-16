import json
from pathlib import Path

import httpx
import pytest

from medvya_ai.providers.transcription.azure_fast import (
    AzureFastTranscriber,
    ProviderError,
    build_definition,
    map_response,
)
from medvya_ai.providers.transcription.base import Phrase, speaker_label
from medvya_ai.providers.transcription.fake import FakeTranscriber

SAMPLE = json.loads(
    (Path(__file__).parents[1] / "fixtures/fast_transcription_sample.json").read_text()
)


def test_map_response_maps_fields_and_skips_blank_phrases() -> None:
    phrases = map_response(SAMPLE)
    assert [p.text for p in phrases] == [
        "Hello, tell me what is troubling you.",
        "Mujhe do din se bukhar hai.",
        "Take tablet paracetamol twice a day.",
        "No speaker field here.",
    ]
    first = phrases[0]
    assert (first.offset_ms, first.duration_ms, first.speaker, first.locale) == (
        960,
        2400,
        1,
        "en-IN",
    )
    assert first.confidence == pytest.approx(0.93616915)
    assert phrases[3].speaker is None and phrases[3].confidence is None


def test_speaker_label_default_and_numbered() -> None:
    assert speaker_label(2) == "Speaker 2"
    assert speaker_label(None) == "unknown"  # never guess an identity
    assert speaker_label(2, part=3) == "Speaker 2 (part 3)"
    assert speaker_label(None, part=3) == "unknown"


def test_definition_has_locales_diarization_and_no_profanity_masking() -> None:
    d = json.loads(build_definition(["hi-IN", "en-IN"], 3))
    assert d["locales"] == ["hi-IN", "en-IN"]
    assert d["diarization"] == {"enabled": True, "maxSpeakers": 3}
    assert d["profanityFilterMode"] == "None"


def _transcriber(handler: httpx.MockTransport, sleeps: list[float]) -> AzureFastTranscriber:
    async def nap(s: float) -> None:
        sleeps.append(s)

    return AzureFastTranscriber(
        endpoint="https://res.cognitiveservices.azure.com",
        api_key="k-test",
        api_version="2025-10-15",
        client=httpx.AsyncClient(transport=handler),
        sleep=nap,
    )


async def test_transcribe_sends_multipart_and_maps() -> None:
    seen: dict[str, object] = {}

    def handler(req: httpx.Request) -> httpx.Response:
        seen["url"] = str(req.url)
        seen["key"] = req.headers["ocp-apim-subscription-key"]
        seen["ctype"] = req.headers["content-type"]
        seen["body"] = req.content
        return httpx.Response(200, json=SAMPLE)

    t = _transcriber(httpx.MockTransport(handler), [])
    out = await t.transcribe(
        b"WEBM-BYTES",
        mime_type="audio/webm;codecs=opus",
        locales=["hi-IN", "en-IN"],
        max_speakers=3,
    )
    assert len(out) == 4
    assert seen["url"] == (
        "https://res.cognitiveservices.azure.com/speechtotext/transcriptions:transcribe"
        "?api-version=2025-10-15"
    )
    assert seen["key"] == "k-test"
    assert str(seen["ctype"]).startswith("multipart/form-data")
    body = seen["body"]
    assert isinstance(body, bytes) and b'name="audio"' in body and b'name="definition"' in body
    assert b"WEBM-BYTES" in body


async def test_retries_429_then_succeeds_honouring_retry_after() -> None:
    calls = {"n": 0}

    def handler(_: httpx.Request) -> httpx.Response:
        calls["n"] += 1
        if calls["n"] == 1:
            return httpx.Response(429, headers={"Retry-After": "5"}, text="busy PHI-LIKE-TEXT")
        if calls["n"] == 2:
            return httpx.Response(503, text="busy")
        return httpx.Response(200, json=SAMPLE)

    sleeps: list[float] = []
    t = _transcriber(httpx.MockTransport(handler), sleeps)
    assert await t.transcribe(b"x", mime_type="audio/webm", locales=["en-IN"], max_speakers=2)
    assert sleeps == [5, 8]  # Retry-After first, then our backoff


async def test_gives_up_after_two_retries_without_leaking_body() -> None:
    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(503, text="secret clinical words")

    sleeps: list[float] = []
    t = _transcriber(httpx.MockTransport(handler), sleeps)
    with pytest.raises(ProviderError) as exc:
        await t.transcribe(b"x", mime_type="audio/webm", locales=["en-IN"], max_speakers=2)
    assert sleeps == [2, 8]
    assert "secret" not in str(exc.value) and exc.value.code == "SPEECH_HTTP_503"


async def test_client_error_is_not_retried() -> None:
    n = {"c": 0}

    def handler(_: httpx.Request) -> httpx.Response:
        n["c"] += 1
        return httpx.Response(400, json={"error": "bad audio"})

    t = _transcriber(httpx.MockTransport(handler), [])
    with pytest.raises(ProviderError) as exc:
        await t.transcribe(b"x", mime_type="audio/webm", locales=["en-IN"], max_speakers=2)
    assert n["c"] == 1 and exc.value.code == "SPEECH_HTTP_400"


async def test_fake_transcriber_is_deterministic_and_diarized() -> None:
    out = await FakeTranscriber().transcribe(
        b"a", mime_type="audio/webm", locales=["en-IN"], max_speakers=3
    )
    assert out == await FakeTranscriber().transcribe(
        b"b", mime_type="audio/webm", locales=["en-IN"], max_speakers=3
    )
    assert {p.speaker for p in out} == {1, 2}
    assert any(p.locale == "hi-IN" for p in out)
    assert all(isinstance(p, Phrase) for p in out)

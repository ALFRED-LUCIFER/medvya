"""FoundryLLM against the real openai SDK on a mocked transport (no network, no credentials)."""

import json

import httpx2
import pytest
from openai import AsyncOpenAI

from medvya_ai.config import Settings
from medvya_ai.extraction.schemas import NormaliseOut
from medvya_ai.providers.errors import ProviderError
from medvya_ai.providers.llm.foundry import BACKOFF_SECONDS, FoundryLLM, base_url

PARSED = {
    "items": [
        {"idx": 1, "english_text": "I have fever.", "role": "patient", "role_confidence": 0.9}
    ]
}


def _completion() -> dict[str, object]:
    return {
        "id": "c1",
        "object": "chat.completion",
        "created": 1,
        "model": "dep",
        "choices": [
            {
                "index": 0,
                "finish_reason": "stop",
                "message": {"role": "assistant", "content": json.dumps(PARSED)},
            }
        ],
        "usage": {"prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15},
    }


def _llm(handler, sleeps: list[float]) -> FoundryLLM:  # type: ignore[no-untyped-def]
    async def nap(s: float) -> None:
        sleeps.append(s)

    client = AsyncOpenAI(
        base_url=base_url("https://res.openai.azure.com"),
        api_key="k-test",
        max_retries=0,
        http_client=httpx2.AsyncClient(transport=httpx2.MockTransport(handler)),
    )
    return FoundryLLM(
        endpoint="x",
        api_key="k",
        deployments={"normalise": "dep-n", "extract": "dep-e"},
        client=client,
        sleep=nap,
    )


def test_base_url_normalisation() -> None:
    assert base_url("https://r.openai.azure.com") == "https://r.openai.azure.com/openai/v1/"
    assert base_url("https://r.openai.azure.com/") == "https://r.openai.azure.com/openai/v1/"
    assert (
        base_url("https://r.openai.azure.com/openai/v1") == "https://r.openai.azure.com/openai/v1/"
    )


async def test_sends_strict_json_schema_no_temperature_and_deployment() -> None:
    seen: dict[str, object] = {}

    def handler(req: httpx2.Request) -> httpx2.Response:
        seen["url"] = str(req.url)
        seen["body"] = json.loads(req.content)
        return httpx2.Response(200, json=_completion())

    out = await _llm(handler, []).parse("normalise", system="s", user="u", schema=NormaliseOut)
    assert out.items[0].english_text == "I have fever."
    body: dict[str, object] = seen["body"]  # type: ignore[assignment]
    assert str(seen["url"]).endswith("/openai/v1/chat/completions")
    assert body["model"] == "dep-n" and "temperature" not in body
    fmt = body["response_format"]
    assert fmt["type"] == "json_schema" and fmt["json_schema"]["strict"] is True  # type: ignore[index]


async def test_retries_429_and_5xx_then_succeeds() -> None:
    codes = iter([429, 503, 200])

    def handler(_: httpx2.Request) -> httpx2.Response:
        c = next(codes)
        return httpx2.Response(c, json=_completion() if c == 200 else {"error": {"message": "PHI"}})

    sleeps: list[float] = []
    out = await _llm(handler, sleeps).parse("extract", system="s", user="u", schema=NormaliseOut)
    assert out.items and sleeps == [2, 8]


async def test_honours_retry_after_up_to_a_cap() -> None:
    waits = iter(["3", "9999", None])

    def handler(_: httpx2.Request) -> httpx2.Response:
        w = next(waits)
        if w is None:
            return httpx2.Response(200, json=_completion())
        return httpx2.Response(429, headers={"retry-after": w}, json={"error": {}})

    sleeps: list[float] = []
    await _llm(handler, sleeps).parse("extract", system="s", user="u", schema=NormaliseOut)
    assert sleeps == [3, 60]


async def test_gives_up_with_safe_code() -> None:
    def handler(_: httpx2.Request) -> httpx2.Response:
        return httpx2.Response(429, json={"error": {"message": "patient text"}})

    sleeps: list[float] = []
    with pytest.raises(ProviderError) as exc:
        await _llm(handler, sleeps).parse("normalise", system="s", user="u", schema=NormaliseOut)
    assert list(sleeps) == list(BACKOFF_SECONDS) and exc.value.code == "LLM_429"
    assert "patient" not in str(exc.value)


async def test_client_error_not_retried() -> None:
    n = {"c": 0}

    def handler(_: httpx2.Request) -> httpx2.Response:
        n["c"] += 1
        return httpx2.Response(400, json={"error": {"message": "temperature unsupported"}})

    with pytest.raises(ProviderError) as exc:
        await _llm(handler, []).parse("normalise", system="s", user="u", schema=NormaliseOut)
    assert n["c"] == 1 and exc.value.code == "LLM_HTTP_400"


async def test_extraction_schema_is_accepted_by_the_sdk_strict_converter() -> None:
    from medvya_ai.extraction.schemas import ExtractionOut
    from medvya_ai.providers.llm.fake import FakeLLM

    sample = await FakeLLM().parse(
        "extract", system="", user='{"segments":[]}', schema=ExtractionOut
    )
    comp = _completion()
    comp["choices"][0]["message"]["content"] = sample.model_dump_json()  # type: ignore[index]
    seen: dict[str, object] = {}

    def handler(req: httpx2.Request) -> httpx2.Response:
        seen["body"] = json.loads(req.content)
        return httpx2.Response(200, json=comp)

    out = await _llm(handler, []).parse("extract", system="s", user="u", schema=ExtractionOut)
    assert out == sample
    schema = seen["body"]["response_format"]["json_schema"]["schema"]  # type: ignore[index]
    assert schema["additionalProperties"] is False


async def test_azure_input_content_filter_400_maps_to_content_filter_code() -> None:
    body = {
        "error": {
            "code": "content_filter",
            "message": "The response was filtered",
            "innererror": {
                "code": "ResponsibleAIPolicyViolation",
                "content_filter_result": {"jailbreak": {"filtered": True, "detected": True}},
            },
        }
    }

    def handler(_: httpx2.Request) -> httpx2.Response:
        return httpx2.Response(400, json=body)

    sleeps: list[float] = []
    with pytest.raises(ProviderError) as exc:
        await _llm(handler, sleeps).parse("normalise", system="s", user="u", schema=NormaliseOut)
    assert exc.value.code == "LLM_CONTENT_FILTER" and sleeps == []  # not retried


def test_real_providers_refuse_endpoints_outside_the_country_allow_list(settings: Settings) -> None:
    # SAFETY: mutation check: removed settings.allowed_host() from build_llm and
    # build_transcriber -> this test failed
    from pydantic import SecretStr

    from medvya_ai.providers.llm import build_llm
    from medvya_ai.providers.transcription import build_transcriber

    real = settings.model_copy(
        update={
            "llm_provider": "foundry",
            "foundry_endpoint": "https://ai-uaenorth.openai.azure.com/openai/v1/",
            "foundry_api_key": SecretStr("k"),
            "foundry_deployment_extract": "e",
            "foundry_deployment_normalise": "n",
            "transcription_provider": "azure_fast",
            "speech_key": SecretStr("k"),
            "speech_region": "uaenorth",
            "processing_endpoint_hosts": (
                "ai-india.openai.azure.com, centralindia.api.cognitive.microsoft.com"
            ),
        }
    )
    with pytest.raises(RuntimeError, match=r"ai-uaenorth\.openai\.azure\.com"):
        build_llm(real)
    with pytest.raises(RuntimeError, match=r"uaenorth\.api\.cognitive\.microsoft\.com"):
        build_transcriber(real)
    with pytest.raises(RuntimeError, match="PROCESSING_ENDPOINT_HOSTS"):
        build_llm(real.model_copy(update={"processing_endpoint_hosts": ""}))
    india = real.model_copy(
        update={
            "foundry_endpoint": "https://AI-India.openai.azure.com/",
            "speech_region": "centralindia",
        }
    )
    assert getattr(build_llm(india), "endpoint_host", None) == "ai-india.openai.azure.com"
    speech = build_transcriber(india)
    assert getattr(speech, "endpoint_host", None) == "centralindia.api.cognitive.microsoft.com"

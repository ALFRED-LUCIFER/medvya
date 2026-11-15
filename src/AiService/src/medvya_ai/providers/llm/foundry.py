"""Azure AI Foundry (Azure OpenAI v1) adapter via the openai package. See provider-notes.md."""

import asyncio
import logging
from collections.abc import Awaitable, Callable
from typing import Any

import openai
from openai import AsyncOpenAI

from medvya_ai.providers.errors import ProviderError
from medvya_ai.providers.llm.base import T, Task, Usage

log = logging.getLogger(__name__)
BACKOFF_SECONDS = (2, 4, 8, 16, 32)
CONTENT_FILTER = "LLM_CONTENT_FILTER"
_RETRYABLE = (openai.RateLimitError, openai.InternalServerError, openai.APIConnectionError)


def base_url(endpoint: str) -> str:
    e = endpoint.rstrip("/")
    return e + "/" if e.endswith("/openai/v1") else e + "/openai/v1/"


def _error_code(exc: openai.APIStatusError) -> str | None:
    """Azure error code (no message text): {"error": {"code", "innererror": {"code"}}}."""
    body = exc.body if isinstance(exc.body, dict) else {}
    err = body.get("error", body)
    if not isinstance(err, dict):
        return None
    inner = err.get("innererror")
    inner_code = inner.get("code") if isinstance(inner, dict) else None
    return str(inner_code or err.get("code") or exc.code or "") or None


class FoundryLLM:
    def __init__(
        self,
        *,
        endpoint: str,
        api_key: str,
        deployments: dict[Task, str],
        client: AsyncOpenAI | None = None,
        sleep: Callable[[float], Awaitable[None]] = asyncio.sleep,
    ) -> None:
        # SDK retries off: the 429/5xx policy below is ours (testable, shared shape with Speech).
        self._client = client or AsyncOpenAI(
            base_url=base_url(endpoint), api_key=api_key, max_retries=0, timeout=300.0
        )
        self._deployments, self._sleep = deployments, sleep
        self.usage = Usage()
        self.model_names = dict(deployments)

    async def parse(self, task: Task, *, system: str, user: str, schema: type[T]) -> T:
        model = self._deployments[task]
        last = "LLM_ERROR"
        for attempt in range(len(BACKOFF_SECONDS) + 1):
            if attempt:
                await self._sleep(BACKOFF_SECONDS[attempt - 1])
            try:
                completion: Any = await self._client.chat.completions.parse(
                    model=model,
                    messages=[
                        {"role": "system", "content": system},
                        {"role": "user", "content": user},
                    ],
                    response_format=schema,
                    # no temperature: current (reasoning) models reject it; the strict schema and
                    # evidence verification are what prevent invented facts
                )
            except _RETRYABLE as exc:
                last = f"LLM_{getattr(exc, 'status_code', 'NETWORK')}"
                continue
            except openai.APIStatusError as exc:
                code = _error_code(exc)
                log.warning("llm_http status=%s code=%s task=%s", exc.status_code, code, task)
                if code in ("content_filter", "ResponsibleAIPolicyViolation"):
                    raise ProviderError(CONTENT_FILTER) from None  # input filtered (Azure 400)
                raise ProviderError(f"LLM_HTTP_{exc.status_code}") from None
            except openai.ContentFilterFinishReasonError:
                # Azure content filter: not retryable; callers may split the input and retry
                raise ProviderError(CONTENT_FILTER) from None
            except openai.LengthFinishReasonError:
                raise ProviderError("LLM_LENGTH") from None
            usage = completion.usage
            if usage is not None:  # counts only: PRD cost gate, never content
                self.usage.add(usage.prompt_tokens or 0, usage.completion_tokens or 0)
                log.info(
                    "llm_usage task=%s prompt_tokens=%s completion_tokens=%s",
                    task,
                    usage.prompt_tokens,
                    usage.completion_tokens,
                )
            message = completion.choices[0].message
            if getattr(message, "refusal", None) or message.parsed is None:
                raise ProviderError("LLM_REFUSED_OR_UNPARSED")
            parsed: T = message.parsed
            return parsed
        raise ProviderError(last)

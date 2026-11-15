"""LLM provider Protocol: one schema-constrained call parsed into `schema`."""

from dataclasses import dataclass
from typing import Literal, Protocol, TypeVar

from pydantic import BaseModel

T = TypeVar("T", bound=BaseModel)
Task = Literal["normalise", "extract", "receptionist"]  # selects the deployment


@dataclass
class Usage:
    """Running token counters (no content). Handlers diff before/after a job for cost accounting."""

    calls: int = 0
    prompt_tokens: int = 0
    completion_tokens: int = 0

    def add(self, prompt: int, completion: int) -> None:
        self.calls += 1
        self.prompt_tokens += prompt
        self.completion_tokens += completion

    def since(self, start: "Usage") -> dict[str, int]:
        return {
            "llm_calls": self.calls - start.calls,
            "prompt_tokens": self.prompt_tokens - start.prompt_tokens,
            "completion_tokens": self.completion_tokens - start.completion_tokens,
        }

    def snapshot(self) -> "Usage":
        return Usage(self.calls, self.prompt_tokens, self.completion_tokens)


class LLMClient(Protocol):
    usage: Usage
    model_names: dict[Task, str]

    async def parse(self, task: Task, *, system: str, user: str, schema: type[T]) -> T: ...

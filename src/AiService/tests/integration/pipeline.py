import uuid

from medvya_ai.jobs.context import Deps
from medvya_ai.jobs.worker import run_once
from medvya_ai.providers.llm.fake import FakeLLM
from medvya_ai.providers.transcription.fake import FakeTranscriber


def deps_for(app, llm: FakeLLM | None = None) -> Deps:  # type: ignore[no-untyped-def]
    return Deps(
        settings=app.state.settings,
        blobs=app.state.blobs,
        wrapper=app.state.key_wrapper,
        transcriber=FakeTranscriber(),
        llm=llm or FakeLLM(),
    )


async def drain(app, deps: Deps, max_jobs: int = 5) -> int:  # type: ignore[no-untyped-def]
    n = 0
    while n < max_jobs and await run_once(app.state.sessionmaker, deps, "test-worker"):
        n += 1
    return n


def uid(s: str) -> uuid.UUID:
    return uuid.UUID(s)

import asyncio
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
    retried = False
    while n < max_jobs:
        if await run_once(app.state.sessionmaker, deps, "test-worker"):
            n, retried = n + 1, False
        elif retried:
            break
        else:  # run_after is the enqueuing transaction's now(): under a loaded VM clock a just-
            await asyncio.sleep(0.2)  # enqueued successor can be a few ms "in the future"
            retried = True
    return n


def uid(s: str) -> uuid.UUID:
    return uuid.UUID(s)

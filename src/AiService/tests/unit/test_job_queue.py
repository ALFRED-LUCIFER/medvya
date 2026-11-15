import uuid

import pytest

from medvya_ai.extraction.normalise import batches
from medvya_ai.jobs.queue import backoff_seconds
from medvya_ai.jobs.worker import safe_error
from medvya_ai.providers.errors import PipelineError, ProviderError
from medvya_ai.providers.transcription.base import Phrase
from medvya_ai.services.transcribe import to_timeline


@pytest.mark.parametrize(("attempts", "delay"), [(1, 30), (2, 60), (3, 120), (4, 240), (5, 480)])
def test_backoff_is_exponential(attempts: int, delay: int) -> None:
    assert backoff_seconds(attempts) == delay


def test_backoff_is_capped() -> None:
    assert backoff_seconds(30) == 3600


def test_safe_error_never_carries_message_text() -> None:
    assert safe_error(ProviderError("SPEECH_HTTP_503")) == "SPEECH_HTTP_503"
    assert safe_error(PipelineError("AUDIO_INCOMPLETE")) == "AUDIO_INCOMPLETE"
    assert safe_error(ValueError("patient has diabetes")) == "ValueError"


def test_segment_offset_is_applied_to_session_timeline() -> None:
    sid = uuid.uuid4()
    out = to_timeline(sid, 3_600_000, [Phrase(1_000, 2_500, "x", 1, "en-IN", None)])
    assert (out[0].start_ms, out[0].end_ms, out[0].recording_segment_id) == (
        3_601_000,
        3_603_500,
        sid,
    )


def test_batches_respect_item_and_char_limits_and_keep_order() -> None:
    items = [(i, "Speaker 1", "x" * 10) for i in range(7)]
    out = batches(items, max_items=3, max_chars=1000)
    assert [len(b) for b in out] == [3, 3, 1]
    assert [i for b in out for i, _, _ in b] == list(range(7))
    assert [len(b) for b in batches(items, max_items=100, max_chars=25)] == [2, 2, 2, 1]
    assert batches([]) == []


async def test_heartbeat_refreshes_lock_and_outlives_stale_window(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    import asyncio

    from medvya_ai.jobs import queue, worker

    touched: list[uuid.UUID] = []

    class _Tx:
        async def __aenter__(self) -> "_Tx":
            return self

        async def __aexit__(self, *a: object) -> None:
            return None

        def begin(self) -> "_Tx":
            return self

    async def fake_touch(_: object, job_id: uuid.UUID) -> None:
        touched.append(job_id)

    monkeypatch.setattr(queue, "touch", fake_touch)
    jid = uuid.uuid4()
    task = asyncio.create_task(worker.heartbeat(lambda: _Tx(), jid, 0.01))  # type: ignore[arg-type]
    await asyncio.sleep(0.08)
    task.cancel()
    assert len(touched) >= 2 and set(touched) == {jid}
    assert queue.HEARTBEAT_SECONDS * 3 <= queue.STALE_LOCK_SECONDS


@pytest.mark.parametrize(
    ("state", "session_epoch", "job_epoch", "ok"),
    [
        ("processing", 0, 0, True),
        ("revoked", 1, 1, False),  # revoked wins even when the epoch matches
        ("processing", 1, 0, False),  # job enqueued before a withdrawal: stale epoch
        ("approved", 1, 1, True),  # approved record with a later audio-only revocation
    ],
)
def test_processable_gate(state: str, session_epoch: int, job_epoch: int, ok: bool) -> None:
    from medvya_ai.jobs.queue import processable

    assert processable(state, session_epoch, job_epoch) is ok

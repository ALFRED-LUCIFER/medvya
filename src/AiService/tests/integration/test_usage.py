import pytest
from sqlalchemy import text

from tests.integration.helpers import finished_session
from tests.integration.pipeline import deps_for, drain

pytestmark = pytest.mark.integration


async def test_jobs_record_usage_counters(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    _, _, h = await make_clinic()
    sid = await finished_session(c, h)
    assert await drain(app, deps_for(app)) == 3
    async with app.state.sessionmaker() as db, db.begin():
        rows = (
            await db.execute(
                text("SELECT kind::text, usage FROM processing_job WHERE session_id = :s"),
                {"s": sid},
            )
        ).all()
    usage = {r[0]: r[1] for r in rows}
    assert (
        usage["transcribe"]["audio_ms"] == 60_000
        and usage["transcribe"]["provider"] == "FakeTranscriber"
    )
    assert usage["normalise"]["llm_calls"] == 1 and usage["normalise"]["prompt_tokens"] > 0
    assert usage["extract"]["completion_tokens"] > 0 and usage["extract"]["facts"] == 2
    assert not any("text" in k or "quote" in k for u in usage.values() for k in u)  # counters only

import pytest
from sqlalchemy import text

from tests.integration.helpers import API, finished_session
from tests.integration.pipeline import deps_for

pytestmark = pytest.mark.integration


async def test_transcribe_then_normalise_fills_english_and_offsets(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, _, h = await make_clinic()
    sid = await finished_session(c, h)
    deps = deps_for(app)
    # transcribe + normalise only: stop before extract
    from medvya_ai.jobs.handlers import HANDLERS

    handlers = {k: v for k, v in HANDLERS.items() if k != "extract"}
    from medvya_ai.jobs.worker import run_once

    assert await run_once(app.state.sessionmaker, deps, "w", handlers)
    assert await run_once(app.state.sessionmaker, deps, "w", handlers)
    async with app.state.sessionmaker() as db, db.begin():
        await db.execute(text("SELECT set_config('app.clinic_id', :c, true)"), {"c": str(cid)})
        rows = (
            await db.execute(
                text(
                    "SELECT idx, speaker_label, original_text, language, english_text, "
                    "start_ms, end_ms, suggested_role, role_confidence "
                    "FROM transcript_segment WHERE session_id = :s ORDER BY idx"
                ),
                {"s": sid},
            )
        ).all()
    assert len(rows) == 3
    assert all(r.english_text for r in rows)
    assert rows[0].english_text == rows[0].original_text  # en: copy
    hi = rows[1]
    assert hi.language == "hi-IN" and hi.original_text.startswith("Mujhe")  # original untouched
    assert "fever" in hi.english_text and hi.english_text != hi.original_text
    assert rows[0].start_ms == 0 and rows[1].start_ms == 3000  # segment offset 0 applied
    assert [r.suggested_role for r in rows] == ["doctor", "patient", "doctor"]
    assert all(float(r.role_confidence) == 0.9 for r in rows)
    r = await c.get(f"{API}/sessions/{sid}", headers=h)
    assert {j["kind"]: j["status"] for j in r.json()["jobs"]} == {
        "transcribe": "done",
        "normalise": "done",
        "extract": "queued",
    }


async def test_failed_job_is_retried_with_backoff_and_error_has_no_phi(
    app_ctx, make_clinic
) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    _, _, h = await make_clinic()
    sid = await finished_session(c, h)
    deps = deps_for(app)
    app.state.blobs.blobs.clear()  # audio vanished: transcribe cannot run
    from medvya_ai.jobs.worker import run_once

    assert await run_once(app.state.sessionmaker, deps, "w")
    async with app.state.sessionmaker() as db, db.begin():
        row = (
            await db.execute(
                text(
                    "SELECT status, attempts, last_error, run_after > now() AS later "
                    "FROM processing_job WHERE session_id = :s"
                ),
                {"s": sid},
            )
        ).one()
    assert row.status == "queued" and row.attempts == 1 and row.later
    assert row.last_error == "KeyError"
    assert not await run_once(app.state.sessionmaker, deps, "w")  # not due yet

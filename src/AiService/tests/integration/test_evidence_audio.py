from datetime import UTC, datetime, timedelta

import pytest

from medvya_ai.db import set_tenant
from medvya_ai.services.retention import sweep
from tests.integration.helpers import API, finished_session
from tests.integration.pipeline import deps_for, drain

pytestmark = pytest.mark.integration


async def test_replay_streams_decrypted_segment_then_410_after_deletion(
    app_ctx, make_clinic, settings, blobs
) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, _, h = await make_clinic()
    sid = await finished_session(c, h)
    await drain(app, deps_for(app))
    facts = (await c.get(f"{API}/sessions/{sid}/result", headers=h)).json()["facts"]
    med = next(f for f in facts if f["category"] == "medication")  # third phrase, 7000-10500 ms
    url = f"{API}/sessions/{sid}/facts/{med['id']}/evidence-audio"

    r = await c.get(url, headers=h)
    assert r.status_code == 200 and r.headers["content-type"] == "audio/webm"
    assert r.content == b"audio-0audio-1"  # decrypted, in order, never a blob link
    assert (r.headers["x-evidence-start-ms"], r.headers["x-evidence-end-ms"]) == ("7000", "10500")
    assert r.headers["cache-control"] == "no-store"
    _, _, other = await make_clinic("B")
    assert (await c.get(url, headers=other)).status_code == 404

    # retention (draft audio after 7 days) deletes the audio: replay says so honestly
    async with app.state.sessionmaker() as db, db.begin():
        await set_tenant(db, cid)
        n = await sweep(
            db,
            blobs,
            clinic_id=cid,
            now=datetime.now(UTC) + timedelta(days=8),
            settings=settings,
        )
    assert n == 2
    r = await c.get(url, headers=h)
    assert r.status_code == 410 and r.json()["error"]["code"] == "AUDIO_NOT_AVAILABLE"
    # the transcript evidence is still shown
    assert (await c.get(f"{API}/sessions/{sid}/result", headers=h)).status_code == 200

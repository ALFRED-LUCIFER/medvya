import asyncpg
import pytest

from tests.conftest import OWNER_URL, _dsn
from tests.integration.helpers import API, consent, grant_all, new_session, put_chunk, segment

pytestmark = pytest.mark.integration


async def test_no_login_uses_demo_clinic_and_unknown_clinic_is_404(app_ctx) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    assert (await c.get(f"{API}/sessions")).status_code == 200
    r = await c.get(
        f"{API}/sessions", headers={"X-Clinic-Id": "00000000-0000-0000-0000-000000000000"}
    )
    assert r.status_code == 404 and r.json()["error"]["code"] == "CLINIC_NOT_FOUND"


async def test_consent_gate_flow(app_ctx, make_clinic, blobs) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    _, _, h = await make_clinic()
    s = await new_session(c, h)
    assert s["state"] == "created" and s["recording_allowed"] is False
    sid, (p1, p2) = s["id"], s["participants"]

    # no consent: segment and chunk refused, nothing in Blob
    assert (await segment(c, h, sid)).status_code == 403
    assert (await put_chunk(c, h, sid, 0, b"abc")).status_code == 403
    assert blobs.blobs == {}

    # one of two granted: still required
    assert (await consent(c, h, sid, p1["id"])).status_code == 201
    r = await put_chunk(c, h, sid, 0, b"abc")
    assert r.status_code == 403 and r.json()["error"]["code"] == "CONSENT_REQUIRED"

    # all granted
    r = await consent(c, h, sid, p2["id"])
    assert r.json()["recording_allowed"] is True
    assert (await segment(c, h, sid)).status_code == 200
    assert (await put_chunk(c, h, sid, 0, b"abc")).json() == {"status": "stored"}

    # participant added later: required again until they consent
    added = await c.post(
        f"{API}/sessions/{sid}/participants",
        headers=h,
        json={"role": "interpreter", "display_name": "I"},
    )
    assert added.status_code == 201
    r = await put_chunk(c, h, sid, 1, b"def")
    assert r.status_code == 403 and r.json()["error"]["code"] == "CONSENT_REQUIRED"
    await consent(c, h, sid, added.json()["id"])
    assert (await put_chunk(c, h, sid, 1, b"def")).status_code == 200

    # withdrawal: very next chunk refused, flag false at once
    r = await consent(c, h, sid, p1["id"], "withdrawn")
    assert r.json()["recording_allowed"] is False
    n = len(blobs.blobs)
    r = await put_chunk(c, h, sid, 2, b"ghi")
    assert r.status_code == 403 and r.json()["error"]["code"] == "CONSENT_WITHDRAWN"
    assert len(blobs.blobs) == n
    got = await c.get(f"{API}/sessions/{sid}", headers=h)
    assert got.json()["recording_allowed"] is False
    assert {x["status"] for x in got.json()["consent"]} >= {"withdrawn"}


async def test_all_refused_closes_session(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    _, _, h = await make_clinic()
    s = await new_session(c, h)
    for p in s["participants"]:
        await consent(c, h, s["id"], p["id"], "refused")
    assert (await c.get(f"{API}/sessions/{s['id']}", headers=h)).json()[
        "state"
    ] == "closed_no_consent"


async def test_participant_limit_and_notice(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    _, _, h = await make_clinic()
    s = await new_session(c, h, 3)
    r = await c.post(
        f"{API}/sessions/{s['id']}/participants",
        headers=h,
        json={"role": "patient", "display_name": "x"},
    )
    assert r.status_code == 409 and r.json()["error"]["code"] == "PARTICIPANT_LIMIT"
    n = (await c.get(f"{API}/consent-notice", headers=h)).json()
    assert n["version"] == "en-v1" and "AI note-taking" in n["text"]


async def test_consent_record_immutable(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    cid, _, h = await make_clinic()
    s = await new_session(c, h, 1)
    await grant_all(c, h, s)
    conn = await asyncpg.connect(_dsn(OWNER_URL))
    try:
        async with conn.transaction():
            await conn.execute("SELECT set_config('app.clinic_id', $1, true)", str(cid))
            for stmt in (
                "UPDATE consent_record SET status = 'refused'",
                "DELETE FROM consent_record",
            ):
                with pytest.raises(asyncpg.RestrictViolationError, match="append-only"):
                    async with conn.transaction():
                        await conn.execute(stmt)
            with pytest.raises(asyncpg.RestrictViolationError, match="append-only"):
                async with conn.transaction():
                    await conn.execute("TRUNCATE consent_record")
    finally:
        await conn.close()

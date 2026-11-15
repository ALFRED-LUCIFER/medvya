"""Declined consent -> manual encounter note, reviewed and approved like any other note."""

import pytest

from tests.integration.helpers import API, consent, grant_all, new_session, segment

pytestmark = pytest.mark.integration


async def test_refused_consent_manual_note_flow(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    _, _, h = await make_clinic()
    s = await new_session(c, h)
    for p in s["participants"]:
        assert (await consent(c, h, s["id"], p["id"], "refused")).status_code == 201
    sid = s["id"]
    assert (await c.get(f"{API}/sessions/{sid}", headers=h)).json()["state"] == "closed_no_consent"

    r = await c.post(
        f"{API}/sessions/{sid}/notes",
        headers=h,
        json={"content": {"subjective": "Fever for two days.", "plan": "Rest"}},
    )
    assert r.status_code == 201, r.text
    assert r.json()["status"] == "draft" and r.json()["rev_no"] == 1 and not r.json()["unverified"]
    got = (await c.get(f"{API}/sessions/{sid}", headers=h)).json()
    assert got["state"] == "review" and got["mode"] == "manual"
    res = (await c.get(f"{API}/sessions/{sid}/result", headers=h)).json()
    assert res["transcript"] == [] and res["facts"] == []
    assert res["note"]["content"]["subjective"] == "Fever for two days."
    # a second manual note is refused; the first can be edited and approved
    assert (await c.post(f"{API}/sessions/{sid}/notes", headers=h, json={})).status_code == 409
    r = await c.patch(
        f"{API}/sessions/{sid}/note", headers=h, json={"content": {"plan": "Rest, fluids"}}
    )
    assert r.status_code == 200 and r.json()["rev_no"] == 2
    assert (await c.post(f"{API}/sessions/{sid}/approve", headers=h)).status_code == 200
    r = await c.get(f"{API}/sessions/{sid}/export", headers=h)
    assert r.status_code == 200 and r.json()["note"]["content"]["plan"] == "Rest, fluids"


async def test_manual_note_refused_once_audio_exists(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    _, _, h = await make_clinic()
    s = await new_session(c, h)
    await grant_all(c, h, s)
    assert (await segment(c, h, s["id"])).status_code == 200
    r = await c.post(f"{API}/sessions/{s['id']}/notes", headers=h, json={})
    assert r.status_code == 409 and r.json()["error"]["code"] == "SESSION_STATE"


async def test_guardian_consent_requires_and_stores_representative(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    _, _, h = await make_clinic()
    s = await new_session(c, h)
    pid = s["participants"][1]["id"]
    body = {
        "participant_id": pid,
        "status": "granted",
        "method": "verbal_attested",
        "on_behalf_of": "guardian",
        "notice_version": "en-v1",
    }
    r = await c.post(f"{API}/sessions/{s['id']}/consents", headers=h, json=body)
    assert r.status_code == 422
    r = await c.post(
        f"{API}/sessions/{s['id']}/consents",
        headers=h,
        json={**body, "representative_name": "R. Sharma", "representative_relationship": "mother"},
    )
    assert r.status_code == 201
    assert (r.json()["representative_name"], r.json()["on_behalf_of"]) == ("R. Sharma", "guardian")

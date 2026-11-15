"""Patient erasure separates deletable drafts from retained approved records; access lists them."""

from typing import Any

import pytest
from sqlalchemy import text

from tests.integration.helpers import API, finished_session
from tests.integration.pipeline import deps_for, drain

pytestmark = pytest.mark.integration


async def _state(c: Any, h: Any, sid: str) -> dict[str, Any]:
    return (await c.get(f"{API}/sessions/{sid}", headers=h)).json()


async def test_erasure_purges_drafts_and_keeps_approved_records(
    app_ctx, make_clinic, blobs
) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, _, h = await make_clinic()
    approved = await finished_session(c, h)  # patient P-1 (helpers default)
    draft = await finished_session(c, h)  # patient P-1
    await drain(app, deps_for(app), max_jobs=6)
    assert (await c.post(f"{API}/sessions/{approved}/approve", headers=h)).status_code == 200
    other = await finished_session(c, h)  # P-1 too but we rename it to another patient
    async with app.state.sessionmaker() as db, db.begin():
        await db.execute(text("SELECT set_config('app.clinic_id', :c, true)"), {"c": str(cid)})
        await db.execute(
            text("UPDATE consultation_session SET patient_ref = 'P-2' WHERE id = :s"), {"s": other}
        )
    blobs_before = len(blobs.blobs)

    r = await c.post(
        f"{API}/privacy-requests",
        headers=h,
        json={"patient_ref": "P-1", "kind": "erasure", "verification_note": "ID card checked"},
    )
    assert r.status_code == 201, r.text
    body = r.json()
    assert body["status"] == "completed"
    assert body["decision"] == {"purged": [draft], "retained_approved_records": [approved]}
    await drain(app, deps_for(app), max_jobs=6)  # purge jobs
    assert (await _state(c, h, draft))["state"] == "revoked"
    s = await _state(c, h, approved)
    assert s["state"] == "approved" and s["revoked_at"]
    assert (await c.get(f"{API}/sessions/{approved}/export", headers=h)).status_code == 200
    assert (await c.get(f"{API}/sessions/{draft}/result", headers=h)).status_code == 409
    assert len(blobs.blobs) == blobs_before - 4  # both P-1 recordings gone, P-2 untouched
    s2 = await _state(c, h, other)
    assert s2["state"] != "revoked" and s2["revoked_at"] is None  # other patient untouched

    got = (await c.get(f"{API}/privacy-requests/{body['id']}", headers=h)).json()
    assert got["decision"] == body["decision"] and got["kind"] == "erasure"
    _, _, hb = await make_clinic("B")
    assert (await c.get(f"{API}/privacy-requests/{body['id']}", headers=hb)).status_code == 404


async def test_access_request_lists_sessions_and_requires_verification(
    app_ctx, make_clinic
) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    _, _, h = await make_clinic()
    sid = await finished_session(c, h)
    await drain(app, deps_for(app))
    await c.post(f"{API}/sessions/{sid}/approve", headers=h)
    r = await c.post(
        f"{API}/privacy-requests",
        headers=h,
        json={"patient_ref": "P-1", "kind": "access", "verification_note": "x"},
    )
    assert r.status_code == 422
    r = await c.post(
        f"{API}/privacy-requests",
        headers=h,
        json={"patient_ref": "P-1", "kind": "access", "verification_note": "phone + DOB"},
    )
    assert r.status_code == 201 and r.json()["decision"] == {"sessions": [sid], "exportable": [sid]}

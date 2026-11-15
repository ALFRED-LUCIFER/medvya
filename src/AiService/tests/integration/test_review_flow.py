import pytest
from sqlalchemy import text

from tests.integration.helpers import API, finished_session
from tests.integration.pipeline import deps_for, drain

pytestmark = pytest.mark.integration


async def _ready(app, c, make_clinic):  # type: ignore[no-untyped-def]
    cid, _, h = await make_clinic()
    sid = await finished_session(c, h)
    await drain(app, deps_for(app))
    return cid, h, sid


async def test_review_approve_export_flow(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, h, sid = await _ready(app, c, make_clinic)
    res = (await c.get(f"{API}/sessions/{sid}/result", headers=h)).json()
    assert res["state"] == "review" and res["label"] == "DRAFT"

    r = await c.get(f"{API}/sessions/{sid}/export", headers=h)  # draft
    assert r.status_code == 409 and r.json()["error"]["code"] == "NOTE_NOT_APPROVED"
    r = await c.get(f"{API}/sessions/{sid}/export", headers=h, params={"format": "pdf"})
    assert r.status_code == 409

    r = await c.patch(
        f"{API}/sessions/{sid}/speakers",
        headers=h,
        json={"speaker_label": "Speaker 1", "new_label": "Doctor"},
    )
    assert r.status_code == 204
    t = (await c.get(f"{API}/sessions/{sid}/result", headers=h)).json()["transcript"]
    assert [x["speaker_label"] for x in t] == ["Doctor", "Speaker 2", "Doctor"]

    # transcript correction: original immutable, displayed text changes, citing facts flagged
    turn = t[1]
    turn_url = f"{API}/sessions/{sid}/transcript/turns/{turn['id']}"
    r = await c.patch(turn_url, headers=h, json={"corrected_text": "Mujhe teen din se bukhar hai"})
    assert r.status_code == 200
    body = r.json()
    assert body["turn"]["text"] == "Mujhe teen din se bukhar hai"
    assert body["turn"]["original_text"] == turn["original_text"] and body["turn"]["edited"]
    cited = {
        f["id"] for f in res["facts"] if any(e["segment_id"] == turn["id"] for e in f["evidence"])
    }
    assert set(body["affected_fact_ids"]) == cited and cited
    t2 = (await c.get(f"{API}/sessions/{sid}/result", headers=h)).json()["transcript"][1]
    assert (t2["text"], t2["original_text"]) == (body["turn"]["text"], turn["original_text"])
    assert (await c.patch(turn_url, headers=h, json={"corrected_text": " "})).status_code == 422

    fact = res["facts"][0]
    r = await c.patch(f"{API}/facts/{fact['id']}", headers=h, json={"review_state": "rejected"})
    assert r.status_code == 200 and r.json()["review_state"] == "rejected"

    content = {**res["note"]["content"], "plan": "edited plan"}
    r = await c.patch(f"{API}/sessions/{sid}/note", headers=h, json={"content": content})
    assert r.status_code == 200 and r.json()["rev_no"] == 2

    r = await c.post(f"{API}/sessions/{sid}/approve", headers=h)
    assert r.status_code == 200 and r.json()["status"] == "approved"

    for method, url, body in (
        ("patch", f"{API}/sessions/{sid}/note", {"content": content}),
        ("patch", f"{API}/facts/{fact['id']}", {"review_state": "confirmed"}),
        ("patch", turn_url, {"corrected_text": "x"}),
        ("post", f"{API}/sessions/{sid}/approve", None),
    ):
        r = await c.request(method, url, headers=h, json=body)
        assert r.status_code == 409 and r.json()["error"]["code"] == "NOTE_APPROVED"

    revs = (await c.get(f"{API}/sessions/{sid}/notes/revisions", headers=h)).json()
    assert [x["rev_no"] for x in revs["revisions"]] == [1, 2, 3] and revs["status"] == "approved"
    assert [x["author_type"] for x in revs["revisions"]] == ["ai", "clinician", "clinician"]
    assert revs["revisions"][1]["content"]["plan"] == "edited plan"

    r = await c.get(f"{API}/sessions/{sid}/export", headers=h)
    body = r.json()
    assert r.status_code == 200 and body["schema_version"] == "1"
    assert len(r.headers["x-document-hash"]) == 64
    pdf = await c.get(f"{API}/sessions/{sid}/export", headers=h, params={"format": "pdf"})
    assert pdf.status_code == 200 and pdf.headers["content-type"] == "application/pdf"
    assert pdf.content.startswith(b"%PDF") and "attachment" in pdf.headers["content-disposition"]
    assert pdf.headers["x-document-hash"] == r.headers["x-document-hash"]
    assert body["note"]["status"] == "approved" and body["note"]["rev_no"] == 3
    assert fact["id"] not in {f["id"] for f in body["facts"]}  # rejected facts left out

    async with app.state.sessionmaker() as db, db.begin():
        await db.execute(text("SELECT set_config('app.clinic_id', :c, true)"), {"c": str(cid)})
        actions = {
            a
            for (a,) in (
                await db.execute(text("SELECT action FROM audit_event WHERE entity_id IS NOT NULL"))
            ).all()
        }
        authors = [
            a
            for (a,) in (
                await db.execute(text("SELECT author_type FROM note_revision ORDER BY rev_no"))
            ).all()
        ]
    assert {
        "speaker_relabel",
        "transcript_correct",
        "fact_review",
        "note_edit",
        "note_approve",
        "export",
    } <= actions
    assert authors == ["ai", "clinician", "clinician"]


async def test_other_clinic_cannot_see_result(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    _, _, sid = await _ready(app, c, make_clinic)
    _, _, other = await make_clinic("B")
    r = await c.get(f"{API}/sessions/{sid}/result", headers=other)
    assert r.status_code == 404

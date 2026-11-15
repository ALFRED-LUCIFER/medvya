import pytest
from sqlalchemy import text

from tests.integration.helpers import API

pytestmark = pytest.mark.integration
R = f"{API}/receptionist/conversations"


async def test_intake_conversation_opens_a_session(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, _, h = await make_clinic()
    conv = (await c.post(R, headers=h)).json()
    assert conv["status"] == "open" and conv["messages"] == []
    url = f"{R}/{conv['id']}/messages"
    r = await c.post(url, headers=h, json={"text": "Asha Patil"})
    assert r.status_code == 200 and r.json()["status"] == "open"
    assert r.json()["messages"][-1]["role"] == "receptionist"
    assert r.json()["intake"]["patient_name"] == "Asha Patil"
    await c.post(url, headers=h, json={"text": "fever since two days"})
    r = await c.post(url, headers=h, json={"text": "2026-11-12"})
    body = r.json()
    assert body["status"] == "completed" and body["session_id"]
    s = (await c.get(f"{API}/sessions/{body['session_id']}", headers=h)).json()
    assert s["patient_ref"] == "Asha Patil" and s["state"] == "created"
    assert {p["role"] for p in s["participants"]} == {"doctor", "patient"}
    # closed: no more messages; other clinic cannot see it
    assert (await c.post(url, headers=h, json={"text": "hi"})).status_code == 409
    _, _, hb = await make_clinic("B")
    assert (await c.get(f"{R}/{conv['id']}", headers=hb)).status_code == 404
    async with app.state.sessionmaker() as db, db.begin():
        await db.execute(text("SELECT set_config('app.clinic_id', :c, true)"), {"c": str(cid)})
        n = await db.scalar(
            text("SELECT count(*) FROM audit_event WHERE action = 'receptionist_message'")
        )
    assert n == 3


@pytest.mark.parametrize("text_", ["I have chest pain right now", "seene mein dard ho raha hai"])
async def test_red_flag_escalates_without_model_or_session(
    app_ctx, make_clinic, text_: str
) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    _, _, h = await make_clinic()
    conv = (await c.post(R, headers=h)).json()
    calls_before = len(app.state.llm.calls)
    r = await c.post(f"{R}/{conv['id']}/messages", headers=h, json={"text": text_})
    body = r.json()
    assert body["status"] == "escalated" and body["session_id"] is None
    assert "emergency" in body["messages"][-1]["text"].lower()
    assert body["intake"]["urgency"] == "emergency"
    assert len(app.state.llm.calls) == calls_before  # no model call for an emergency

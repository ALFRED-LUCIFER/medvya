import uuid
from datetime import UTC, date, datetime
from typing import Any

import pytest
from sqlalchemy import text

from medvya_ai.db import set_tenant, uuid7
from medvya_ai.services.analytics import medication_sql
from tests.integration.helpers import API

pytestmark = pytest.mark.integration


async def _seed_session(
    app: Any,
    cid: Any,
    uid: Any,
    *,
    day: date,
    state: str,
    meds: list[tuple[str, str]],
    symptoms: list[tuple[str, str]] = (),  # type: ignore[assignment]
) -> None:
    """meds / symptoms: (label or generic_name, review_state)."""
    sid, nid = uuid7(), uuid7()
    approved = (
        datetime(day.year, day.month, day.day, 10, tzinfo=UTC) if state == "approved" else None
    )
    async with app.state.sessionmaker() as db, db.begin():
        await set_tenant(db, cid)
        await db.execute(
            text(
                "INSERT INTO consultation_session (id, clinic_id, doctor_id, patient_ref, "
                "scheduled_date, state, dek_wrapped, kek_version, approved_at) VALUES "
                "(:id, :c, :u, 'P', :d, :st, :dek, 'v1', :ap)"
            ),
            {"id": sid, "c": cid, "u": uid, "d": day, "st": state, "dek": b"x", "ap": approved},
        )
        await db.execute(
            text("INSERT INTO clinical_note (id, clinic_id, session_id) VALUES (:n, :c, :s)"),
            {"n": nid, "c": cid, "s": sid},
        )
        facts = [("medication", g, rs) for g, rs in meds] + [
            ("symptom", lbl, rs) for lbl, rs in symptoms
        ]
        for cat, label, review in facts:
            fid = uuid7()
            await db.execute(
                text(
                    "INSERT INTO clinical_fact (id, clinic_id, session_id, note_id, category, "
                    "label, review_state) VALUES (:f, :c, :s, :n, :cat, :l, :r)"
                ),
                {"f": fid, "c": cid, "s": sid, "n": nid, "cat": cat, "l": label, "r": review},
            )
            if cat == "medication":
                await db.execute(
                    text(
                        "INSERT INTO medication (fact_id, clinic_id, name_as_spoken, generic_name) "
                        "VALUES (:f, :c, :l, :l)"
                    ),
                    {"f": fid, "c": cid, "l": label},
                )


@pytest.fixture
async def seeded(app_ctx, make_clinic):  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, uid, h = await make_clinic("A")
    ocid, ouid, _ = await make_clinic("B")
    fever = [("fever", "ai")]
    await _seed_session(
        app, cid, uid, day=date(2026, 11, 2), state="approved",
        meds=[("paracetamol", "ai"), ("amoxicillin", "confirmed")],
        symptoms=[*fever, ("cough", "rejected")],
    )  # fmt: skip
    await _seed_session(
        app, cid, uid, day=date(2026, 11, 4), state="approved",
        meds=[("paracetamol", "ai")], symptoms=fever,
    )  # fmt: skip
    await _seed_session(
        app, cid, uid, day=date(2026, 11, 10), state="approved",
        meds=[("paracetamol", "ai"), ("metformin", "rejected")], symptoms=fever,
    )  # fmt: skip
    await _seed_session(
        app, cid, uid, day=date(2026, 11, 5), state="review",
        meds=[("ibuprofen", "ai")], symptoms=fever,
    )  # fmt: skip
    await _seed_session(  # another clinic: must never show up
        app, ocid, ouid, day=date(2026, 11, 2), state="approved",
        meds=[("paracetamol", "ai")] * 5, symptoms=fever,
    )  # fmt: skip
    return app, c, h, cid


def _items(r: Any) -> list[tuple[Any, ...]]:
    assert r.status_code == 200, r.text
    return [
        (i.get("period"), i.get("generic_name"), i.get("prescriptions"), i.get("sessions"))
        for i in r.json()["items"]
    ]


async def test_medications_by_day_week_month(seeded) -> None:  # type: ignore[no-untyped-def]
    _, c, h, _ = seeded
    url = f"{API}/analytics/medications"
    day = _items(await c.get(url, headers=h))
    assert day == [
        ("2026-11-02", "amoxicillin", 1, 1),
        ("2026-11-02", "paracetamol", 1, 1),
        ("2026-11-04", "paracetamol", 1, 1),
        ("2026-11-10", "paracetamol", 1, 1),
    ]
    week = _items(await c.get(url, headers=h, params={"group": "week"}))
    assert week == [
        ("2026-11-02", "paracetamol", 2, 2),
        ("2026-11-02", "amoxicillin", 1, 1),
        ("2026-11-09", "paracetamol", 1, 1),
    ]
    month = _items(await c.get(url, headers=h, params={"group": "month"}))
    assert month == [("2026-11-01", "paracetamol", 3, 3), ("2026-11-01", "amoxicillin", 1, 1)]


async def test_medications_range_limit_and_validation(seeded) -> None:  # type: ignore[no-untyped-def]
    _, c, h, _ = seeded
    url = f"{API}/analytics/medications"
    rng = _items(await c.get(url, headers=h, params={"from": "2026-11-03", "to": "2026-11-09"}))
    assert rng == [("2026-11-04", "paracetamol", 1, 1)]
    top1 = _items(await c.get(url, headers=h, params={"limit": 1, "group": "month"}))
    assert [i[1] for i in top1] == ["paracetamol"]
    bad = await c.get(url, headers=h, params={"from": "2026-11-09", "to": "2026-11-03"})
    assert bad.status_code == 422 and bad.json()["error"]["code"] == "VALIDATION_ERROR"
    assert (await c.get(url, headers=h, params={"group": "year"})).status_code == 422


async def test_drafts_excluded_unless_include_draft(seeded) -> None:  # type: ignore[no-untyped-def]
    _, c, h, _ = seeded
    url = f"{API}/analytics/medications"
    default = {i[1] for i in _items(await c.get(url, headers=h, params={"group": "month"}))}
    with_draft = _items(
        await c.get(url, headers=h, params={"group": "month", "include_draft": True})
    )
    assert "ibuprofen" not in default and "metformin" not in default
    assert {i[1] for i in with_draft} == default | {"ibuprofen"}
    assert "metformin" not in {i[1] for i in with_draft}  # rejected stays out


async def test_facts_and_overview(seeded) -> None:  # type: ignore[no-untyped-def]
    _, c, h, _ = seeded
    r = await c.get(f"{API}/analytics/facts", headers=h, params={"category": "symptom"})
    assert r.json()["items"] == [{"category": "symptom", "label": "fever", "n": 3}]
    allf = (await c.get(f"{API}/analytics/facts", headers=h)).json()["items"]
    assert {"category": "medication", "label": "paracetamol", "n": 3} in allf
    assert (await c.get(f"{API}/analytics/facts?category=nope", headers=h)).status_code == 422

    o = (await c.get(f"{API}/analytics/overview", headers=h)).json()
    assert o == {
        "sessions": 4,
        "approved": 3,
        "by_category": [{"category": "medication", "n": 4}, {"category": "symptom", "n": 3}],
    }
    o = (
        await c.get(
            f"{API}/analytics/overview",
            headers=h,
            params={"from": "2026-11-01", "to": "2026-11-05"},
        )
    ).json()
    assert (o["sessions"], o["approved"]) == (3, 2)


async def _plan(app: Any, cid: Any, sql: str) -> str:
    sql = sql.replace(":cid", f"'{uuid.UUID(str(cid))}'::uuid").replace(":limit", "20")
    async with app.state.sessionmaker() as db, db.begin():
        await set_tenant(db, cid)
        await db.execute(text("SET LOCAL enable_seqscan = off"))  # tiny tables: force index paths
        rows = await db.execute(text("EXPLAIN " + sql))
        return "\n".join(r[0] for r in rows)


async def test_explain_uses_view_source_and_indexes(seeded) -> None:  # type: ignore[no-untyped-def]
    app, _, _, cid = seeded
    async with app.state.sessionmaker() as db, db.begin():
        views = (await db.execute(text("SELECT viewname FROM pg_views"))).scalars().all()
    assert {"v_medication_usage", "v_fact_counts"} <= set(views)

    sql, _ = medication_sql("day", None, None, include_draft=False)
    assert "v_medication_usage" in sql
    plan = await _plan(app, cid, sql)
    assert "state = 'approved'" in plan  # the view's approved-only predicate was applied
    assert "Index" in plan and ("ix_" in plan or "_pkey" in plan)

    draft_sql, _ = medication_sql("day", None, None, include_draft=True)
    draft_plan = await _plan(app, cid, draft_sql)
    assert "state = 'approved'" not in draft_plan and "Index" in draft_plan

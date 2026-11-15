import pytest
from sqlalchemy import text

from medvya_ai.extraction.schemas import (
    EvidenceIn,
    ExtractionOut,
    FactIn,
    SoapIn,
)
from medvya_ai.providers.llm.fake import FakeLLM
from tests.integration.helpers import API, finished_session
from tests.integration.pipeline import deps_for, drain

pytestmark = pytest.mark.integration


def _fact(label: str, quote: str, idx: int = 2, cat: str = "symptom") -> FactIn:
    return FactIn(
        category=cat,
        label=label,
        polarity="present",
        body_site=None,  # type: ignore[arg-type]
        value_numeric=None,
        value_unit=None,
        detail=None,
        confidence=0.9,
        evidence=[EvidenceIn(segment_idx=idx, quote=quote)],
        medication=None,
    )


def _llm_with_fabrication() -> FakeLLM:
    def extract(_: dict) -> ExtractionOut:  # type: ignore[type-arg]
        return ExtractionOut(
            note=SoapIn(
                chief_complaint="c",
                hpi="h",
                subjective="s",
                objective="o",
                assessment="a",
                plan="p",
                summary="sum",
            ),
            facts=[
                _fact("fever", "fever and a headache", idx=1),
                _fact("chest pain", "severe chest pain", idx=1),  # fabricated
                _fact("paracetamol", "paracetamol 500 mg", cat="medication"),  # no medication obj
            ],
            missing_or_uncertain=["dose unclear"],
        )

    return FakeLLM({"extract": extract})


async def test_extract_persists_only_verified_facts(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, _, h = await make_clinic()
    sid = await finished_session(c, h)
    assert await drain(app, deps_for(app, _llm_with_fabrication())) == 3
    async with app.state.sessionmaker() as db, db.begin():
        await db.execute(text("SELECT set_config('app.clinic_id', :c, true)"), {"c": str(cid)})
        facts = (
            await db.execute(
                text(
                    "SELECT f.id, f.label, f.category, f.review_state, "
                    "(SELECT count(*) FROM fact_evidence e WHERE e.fact_id = f.id) AS ev, "
                    "(SELECT count(*) FROM medication m WHERE m.fact_id = f.id) AS med "
                    "FROM clinical_fact f WHERE f.session_id = :s ORDER BY f.label"
                ),
                {"s": sid},
            )
        ).all()
        rej = (
            await db.execute(
                text("SELECT reason FROM extraction_rejection WHERE session_id = :s"), {"s": sid}
            )
        ).all()
        note = (
            await db.execute(
                text(
                    "SELECT n.status, r.rev_no, r.author_type, r.content->>'summary' AS summary "
                    "FROM clinical_note n JOIN note_revision r ON r.id = n.current_revision_id "
                    "WHERE n.session_id = :s"
                ),
                {"s": sid},
            )
        ).one()
        state = (
            await db.execute(
                text("SELECT state FROM consultation_session WHERE id = :s"), {"s": sid}
            )
        ).scalar_one()
    assert [f.label for f in facts] == ["fever", "paracetamol"]
    assert all(f.ev >= 1 and f.review_state == "ai" for f in facts)
    assert facts[1].med == 1 and facts[0].med == 0
    assert [r.reason for r in rej] == ["QUOTE_NOT_FOUND"]
    assert (note.status, note.rev_no, note.author_type, note.summary) == ("draft", 1, "ai", "sum")
    assert state == "review"
    r = await c.get(f"{API}/sessions/{sid}/result", headers=h)
    assert r.status_code == 200 and r.json()["label"] == "DRAFT"
    assert {f["label"] for f in r.json()["facts"]} == {"fever", "paracetamol"}


async def test_result_is_409_not_ready_while_processing(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    _, c = app_ctx
    _, _, h = await make_clinic()
    sid = await finished_session(c, h)
    r = await c.get(f"{API}/sessions/{sid}/result", headers=h)
    assert r.status_code == 409 and r.json()["error"]["code"] == "NOT_READY"

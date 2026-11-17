"""Content-based speaker roles: suggested by normalise, applied by the clinician as per-phrase
overrides, resolved in fact provenance; reprocess re-runs the AI steps on a draft."""

import pytest

from medvya_ai.providers.transcription.base import Phrase
from tests.integration.helpers import API, finished_session, mark_approved
from tests.integration.pipeline import deps_for, drain

pytestmark = pytest.mark.integration

# Two people merged in one diarization cluster, as the provider does for similar voices.
MERGED = (
    Phrase(0, 2000, "Tell me what is troubling you.", 1, "en-IN", 0.9),
    Phrase(2500, 3000, "I have had fever for two days.", 2, "en-IN", 0.9),
    Phrase(6000, 3000, "He takes paracetamol 500 mg twice a day.", 2, "en-IN", 0.9),
)


async def test_suggested_roles_applied_and_provenance_follows(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    _, _, h = await make_clinic()
    sid = await finished_session(c, h)
    deps = deps_for(app)
    deps.transcriber.__init__(MERGED)  # type: ignore[misc]
    assert await drain(app, deps) == 3
    res = (await c.get(f"{API}/sessions/{sid}/result", headers=h)).json()
    t = res["transcript"]
    assert [x["provider_speaker"] for x in t] == ["Speaker 1", "Speaker 2", "Speaker 2"]
    assert [x["suggested_role"] for x in t] == ["doctor", "patient", "companion"]
    med = next(f for f in res["facts"] if f["category"] == "medication")
    assert med["reported_by"] == "Speaker 2 (likely companion)"  # suggestion, not yet confirmed

    r = await c.post(f"{API}/sessions/{sid}/transcript/apply-suggested-roles", headers=h)
    assert r.status_code == 200 and r.json() == {"applied": 3, "skipped_low_confidence": 0}
    res = (await c.get(f"{API}/sessions/{sid}/result", headers=h)).json()
    t = res["transcript"]
    assert [x["speaker_label"] for x in t] == ["doctor", "patient", "companion"]
    assert all(x["edited"] for x in t) and t[2]["provider_speaker"] == "Speaker 2"
    med = next(f for f in res["facts"] if f["category"] == "medication")
    assert med["reported_by"] == "companion" and med["evidence"][0]["speaker_label"] == "companion"
    assert res["quality"]["edited_phrases"] == 3
    # idempotent; a single phrase can still be re-assigned by hand
    assert (
        await c.post(f"{API}/sessions/{sid}/transcript/apply-suggested-roles", headers=h)
    ).json()["applied"] == 0
    r = await c.patch(
        f"{API}/sessions/{sid}/transcript/turns/{t[2]['id']}",
        headers=h,
        json={"speaker_label": "patient"},
    )
    assert r.status_code == 200 and r.json()["turn"]["speaker_label"] == "patient"
    assert r.json()["turn"]["original_text"] == t[2]["original_text"]
    r = await c.patch(f"{API}/sessions/{sid}/transcript/turns/{t[2]['id']}", headers=h, json={})
    assert r.status_code == 422


async def test_reprocess_replaces_ai_facts_and_adds_revision(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    app, c = app_ctx
    cid, _, h = await make_clinic()
    sid = await finished_session(c, h)
    await drain(app, deps_for(app))
    before = (await c.get(f"{API}/sessions/{sid}/result", headers=h)).json()
    r = await c.post(f"{API}/sessions/{sid}/reprocess", headers=h)
    assert r.status_code == 202 and r.json() == {"state": "processing"}, r.text
    assert (await c.get(f"{API}/sessions/{sid}/result", headers=h)).status_code == 409
    assert await drain(app, deps_for(app)) == 2  # normalise + extract
    after = (await c.get(f"{API}/sessions/{sid}/result", headers=h)).json()
    assert after["state"] == "review" and after["note"]["rev_no"] == 2
    assert {f["label"] for f in after["facts"]} == {f["label"] for f in before["facts"]}
    assert {f["id"] for f in after["facts"]}.isdisjoint({f["id"] for f in before["facts"]})
    revs = (await c.get(f"{API}/sessions/{sid}/notes/revisions", headers=h)).json()["revisions"]
    assert [x["author_type"] for x in revs] == ["ai", "ai"]
    # a reviewed fact would be wiped by a re-run: refused until the review is undone
    # SAFETY: mutation check: removed the FACTS_REVIEWED guard in note.reprocess -> this failed
    await c.patch(
        f"{API}/facts/{after['facts'][0]['id']}", headers=h, json={"review_state": "confirmed"}
    )
    r = await c.post(f"{API}/sessions/{sid}/reprocess", headers=h)
    assert r.status_code == 409 and r.json()["error"]["code"] == "FACTS_REVIEWED"
    await mark_approved(app, cid, sid)
    assert (await c.post(f"{API}/sessions/{sid}/reprocess", headers=h)).status_code == 409


async def test_content_filter_refusal_isolates_one_phrase(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    """The provider filter refuses a batch; normalise splits it so only the refused phrase is kept
    as heard (role unknown) and every other phrase still gets English text and a role."""
    from medvya_ai.providers.errors import ProviderError
    from medvya_ai.providers.llm.fake import FakeLLM, _normalise

    def filtered(payload: dict) -> object:  # type: ignore[type-arg]
        if any("BLOCKED" in s["text"] for s in payload["segments"]):
            raise ProviderError("LLM_CONTENT_FILTER")
        return _normalise(payload)

    app, c = app_ctx
    _, _, h = await make_clinic()
    sid = await finished_session(c, h)
    deps = deps_for(app, FakeLLM({"normalise": filtered}))
    deps.transcriber.__init__(  # type: ignore[misc]
        (*MERGED, Phrase(10000, 1000, "BLOCKED phrase", 2, "en-IN", 0.9))
    )
    assert await drain(app, deps) == 3
    t = (await c.get(f"{API}/sessions/{sid}/result", headers=h)).json()["transcript"]
    assert [x["suggested_role"] for x in t] == ["doctor", "patient", "companion", "unknown"]
    assert t[3]["english_text"] == "BLOCKED phrase" and t[3]["role_confidence"] == 0
    assert all(x["english_text"] for x in t)


async def test_reprocess_reads_corrections_and_sends_roles_not_names(app_ctx, make_clinic) -> None:  # type: ignore[no-untyped-def]
    # SAFETY: mutation check: built normalise items from original_text/speaker_label again ->
    # this test failed (the model never saw the corrected text)
    import json

    from medvya_ai.providers.llm.fake import FakeLLM, _extract, _normalise

    seen: dict[str, list[dict]] = {"normalise": [], "extract": []}  # type: ignore[type-arg]

    def spy(task, fn):  # type: ignore[no-untyped-def]
        def run(payload):  # type: ignore[no-untyped-def]
            seen[task].append(json.loads(json.dumps(payload)))
            return fn(payload)

        return run

    app, c = app_ctx
    _, _, h = await make_clinic()
    sid = await finished_session(c, h)
    await drain(app, deps_for(app))
    t = (await c.get(f"{API}/sessions/{sid}/result", headers=h)).json()["transcript"]
    r = await c.patch(
        f"{API}/sessions/{sid}/transcript/turns/{t[1]['id']}",
        headers=h,
        json={"corrected_text": "I have had fever for three days.", "speaker_label": "patient"},
    )
    assert r.status_code == 200
    assert (await c.post(f"{API}/sessions/{sid}/reprocess", headers=h)).status_code == 202
    llm = FakeLLM({"normalise": spy("normalise", _normalise), "extract": spy("extract", _extract)})
    assert await drain(app, deps_for(app, llm)) == 2
    (norm,) = seen["normalise"]
    turn = next(s for s in norm["segments"] if s["idx"] == t[1]["idx"])
    assert turn == {
        "idx": t[1]["idx"],
        "speaker": "patient",
        "text": "I have had fever for three days.",
    }
    assert norm["participants"] == [{"role": "doctor"}, {"role": "patient"}]
    assert "doctor 0" not in json.dumps(norm) and "patient 1" not in json.dumps(norm)  # no names
    (ext,) = seen["extract"]
    turn = next(s for s in ext["segments"] if s["idx"] == t[1]["idx"])
    assert turn["speaker"] == "patient" and "three days" in turn["text"]

"""Clinical extraction gate evaluation over the synthetic case set (tests/evals/cases).

Replay mode (default) is deterministic and runs in CI. `pytest -m live tests/evals` replaces the
recorded model output with the configured LLM. The report lands in tests/evals/reports/."""

import json
from datetime import UTC, datetime
from pathlib import Path

import pytest

from medvya_ai.config import get_settings
from medvya_ai.providers.llm import build_llm
from tests.evals.harness import load_cases, run_case

REPORTS = Path(__file__).with_name("reports")


async def _run(live: bool) -> list[dict[str, object]]:
    llm = build_llm(get_settings()) if live else None
    rows = [await run_case(c, llm) for c in load_cases()]
    REPORTS.mkdir(exist_ok=True)
    report = {
        "run_at": datetime.now(UTC).isoformat(timespec="seconds"),
        "mode": "live" if live else "replay",
        "model": getattr(llm, "model_names", {}),
        "cases": rows,
    }
    (REPORTS / "latest.json").write_text(json.dumps(report, indent=1, ensure_ascii=False) + "\n")
    return rows


def _gate(rows: list[dict[str, object]]) -> None:
    """Release gate from the blueprint: zero unsupported critical facts, negations preserved."""
    assert len(rows) >= 7
    for r in rows:
        assert r["critical_unsupported"] == [], r
        assert r["negation_fidelity"] == 1.0, r
        assert r["precision"] == 1.0, r  # nothing the gate let through is outside the gold


async def test_replay_set_passes_release_gate() -> None:
    rows = await _run(live=False)
    _gate(rows)
    by_id = {r["id"]: r for r in rows}
    # the gate, not the model, produced these outcomes
    assert by_id["02_hi_negation"]["negation_flagged"] == 2
    # SAFETY: mutation check: removed the ASSERTION_CATEGORIES has_term check in persist.py ->
    # this test failed (penicillin and asthma kept as present)
    assert by_id["07_en_label_not_in_quote"]["grounding_flagged"] == 2
    assert by_id["04_hinglish_switch"]["rejected_by_gate"] == 1  # "dengue" never said
    assert by_id["05_three_speakers_overlap"]["rejected_by_gate"] == 1  # invented aspirin
    assert by_id["06_dose_correction_noisy"]["strength_exact"] == "0/0"  # wrong 50 mg dropped
    assert by_id["01_en_fever"]["wer"] < 0.1 and by_id["02_hi_negation"]["cer"] < 0.1


@pytest.mark.live
async def test_live_model_against_gold() -> None:
    _gate(await _run(live=True))

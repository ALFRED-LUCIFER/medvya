"""Runs one evaluation case through the real extraction gate (evidence verification, grounding,
negation guard) without a database, and scores it against the clinician gold.

Replay mode uses the recorded model output in the case file, so the gate itself is what is
measured and the run is reproducible. Live mode (pytest -m live) asks the configured LLM for the
output first, which measures the model as deployed. Cases are synthetic: no patient data."""

import json
import uuid
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from medvya_ai.extraction import prompts
from medvya_ai.extraction.persist import persist_extraction
from medvya_ai.extraction.schemas import ExtractionOut
from medvya_ai.models import ClinicalFact, Medication
from medvya_ai.providers.llm.base import LLMClient
from tests.evals.metrics import Fact, cer, score_facts, wer

CASES = Path(__file__).with_name("cases")


@dataclass
class Turn:
    idx: int
    id: uuid.UUID
    original_text: str
    english_text: str


class CollectDB:
    """Minimal AsyncSession stand-in: hands the transcript to persist_extraction, collects rows."""

    def __init__(self, turns: list[Turn]) -> None:
        self.turns, self.added = turns, []

    async def scalars(self, *_: object) -> Any:
        turns = self.turns
        return type("R", (), {"all": staticmethod(lambda: turns)})()

    def add(self, row: object) -> None:
        self.added.append(row)

    async def flush(self) -> None:
        return None

    async def execute(self, *_: object) -> None:
        return None

    async def scalar(self, *_: object) -> None:
        return None  # no existing note


def load_cases() -> list[dict[str, Any]]:
    return [json.loads(p.read_text("utf-8")) for p in sorted(CASES.glob("*.json"))]


async def run_case(case: dict[str, Any], llm: LLMClient | None = None) -> dict[str, Any]:
    turns = [
        Turn(t["idx"], uuid.uuid4(), t["original_text"], t.get("english_text", t["original_text"]))
        for t in case["turns"]
    ]
    if llm is None:
        out = ExtractionOut.model_validate(case["model_output"])
    else:
        out = await llm.parse(
            "extract",
            system=prompts.EXTRACT_SYSTEM,
            user=prompts.extract_user(
                [
                    (t.idx, c["speaker_role"], t.english_text)
                    for t, c in zip(turns, case["turns"], strict=True)
                ]
            ),
            schema=ExtractionOut,
        )
    db = CollectDB(turns)
    counts = await persist_extraction(db, clinic_id=uuid.uuid4(), session_id=uuid.uuid4(), out=out)  # type: ignore[arg-type]
    meds = {m.fact_id: m for m in db.added if isinstance(m, Medication)}
    predicted = [
        Fact(
            f.category,
            f.label,
            f.polarity,
            float(meds[f.id].strength_value)
            if f.id in meds and meds[f.id].strength_value is not None
            else None,
        )
        for f in db.added
        if isinstance(f, ClinicalFact)
    ]
    gold = [Fact(**g) for g in case["gold_facts"]]
    scores = score_facts(gold, predicted)
    asr: dict[str, float] = {}
    if "asr_hypothesis" in case:
        ref = " ".join(t.original_text for t in turns)
        hyp = " ".join(case["asr_hypothesis"])
        asr = {"wer": round(wer(ref, hyp), 3), "cer": round(cer(ref, hyp), 3)}
    return {
        "id": case["id"],
        "slice": case["slice"],
        "proposed": len(out.facts),
        "accepted": counts.facts,
        "rejected_by_gate": counts.rejected,
        "negation_flagged": counts.negation_flagged,
        "grounding_flagged": counts.grounding_flagged,
        **scores.as_dict(),
        **asr,
    }

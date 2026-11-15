"""Deterministic LLM for tests and local runs (LLM_PROVIDER=fake).

Default behaviour: translate via a tiny phrase table, and extract a symptom/medication fact from
transcript segments that mention fever or paracetamol, quoting the segment text verbatim.
Tests can pass `responders` to script any response (including fabricated facts).
"""

import json
from collections.abc import Callable
from typing import Any

from pydantic import BaseModel

from medvya_ai.extraction.schemas import (
    EvidenceIn,
    ExtractionOut,
    FactIn,
    MedicationIn,
    NormaliseOut,
    NormItem,
    SoapIn,
)
from medvya_ai.providers.llm.base import T, Task, Usage

TRANSLATIONS = {
    "Mujhe do din se bukhar hai aur sar mein dard hai.": (
        "I have had fever and a headache for two days."
    ),
}

Responder = Callable[[dict[str, Any]], BaseModel]


def _normalise(payload: dict[str, Any]) -> BaseModel:
    """Translate via the phrase table; role: Speaker 1 -> doctor, a third-person statement about
    the patient -> companion, else patient (mirrors the real prompt's rules on this fixture)."""
    items = []
    for s in payload["segments"]:
        low = s["text"].lower()
        if s["speaker"] == "Speaker 1":
            role = "doctor"
        elif low.startswith(("he ", "his ", "my father")) or "daughter" in low:
            role = "companion"
        else:
            role = "patient"
        items.append(
            NormItem(
                idx=s["idx"],
                english_text=TRANSLATIONS.get(s["text"], s["text"]),
                role=role,
                role_confidence=0.9,
            )
        )
    return NormaliseOut(items=items)


def _extract(payload: dict[str, Any]) -> BaseModel:
    facts: list[FactIn] = []
    for s in payload["segments"]:
        low = s["text"].lower()
        if "fever" in low:
            facts.append(_fact("symptom", "fever", s["idx"], "fever", None))
        if "paracetamol" in low:
            med = MedicationIn(
                name_as_spoken="paracetamol",
                generic_name="paracetamol",
                strength_value=500,
                strength_unit="mg",
                dose_text="1 tablet",
                frequency_code="BD",
                frequency_text="twice a day",
                duration_value=3,
                duration_unit="day",
                route="oral",
                instructions=None,
            )
            facts.append(_fact("medication", "paracetamol", s["idx"], "paracetamol 500 mg", med))
    return ExtractionOut(
        note=SoapIn(
            chief_complaint="Fever",
            hpi="Fever for two days.",
            subjective="Fever, headache.",
            objective="",
            assessment="",
            plan="Paracetamol 500 mg twice a day for three days.",
            summary="Patient reports fever and headache; paracetamol advised.",
        ),
        facts=facts,
        missing_or_uncertain=[],
    )


def _receptionist(payload: dict[str, Any]) -> BaseModel:
    """Scripted intake: asks for name, then reason, then date; complete on the third answer."""
    from medvya_ai.services.receptionist import Intake, ReceptionistTurn

    answers = [m["text"] for m in payload["messages"] if m["role"] == "patient"]
    name = answers[0] if len(answers) >= 1 else None
    reason = answers[1] if len(answers) >= 2 else None
    when = answers[2] if len(answers) >= 3 else None
    questions = [
        "Hello! May I have the patient's name?",
        "What is the reason for the visit?",
        "Which date would you prefer?",
    ]
    done = len(answers) >= 3
    reply = (
        f"Thank you {name}. Booking for {reason} on {when}. The doctor will see you then."
        if done
        else questions[len(answers)]
    )
    return ReceptionistTurn(
        reply=reply,
        language="en-IN",
        intake=Intake(
            patient_name=name,
            reason=reason,
            preferred_date=when,
            preferred_time=None,
            specialty=None,
            urgency="routine",
        ),
        complete=done,
    )


def _fact(cat: Any, label: str, idx: int, quote: str, med: MedicationIn | None) -> FactIn:
    return FactIn(
        category=cat,
        label=label,
        polarity="present",
        body_site=None,
        value_numeric=None,
        value_unit=None,
        detail=None,
        confidence=0.9,
        evidence=[EvidenceIn(segment_idx=idx, quote=quote)],
        medication=med,
    )


class FakeLLM:
    def __init__(self, responders: dict[Task, Responder] | None = None) -> None:
        self._responders: dict[Task, Responder] = {
            "normalise": _normalise,
            "extract": _extract,
            "receptionist": _receptionist,
        }
        self._responders.update(responders or {})
        self.calls: list[Task] = []
        self.usage = Usage()
        self.model_names: dict[Task, str] = {k: "fake" for k in self._responders}

    async def parse(self, task: Task, *, system: str, user: str, schema: type[T]) -> T:
        self.calls.append(task)
        out = self._responders[task](json.loads(user))
        self.usage.add(len(user) // 4, len(out.model_dump_json()) // 4)  # ~4 chars per token
        return schema.model_validate(out.model_dump())

"""Evaluation metrics (pure, dependency-free). WER/CER for transcripts, precision/recall and
negation fidelity for facts, unsupported-fact and critical-miss rates for safety."""

from collections.abc import Sequence
from dataclasses import dataclass, field

CRITICAL = frozenset({"medication", "allergy", "diagnosis"})


def edit_distance(a: Sequence[str], b: Sequence[str]) -> int:
    prev = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        cur = [i]
        for j, y in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (x != y)))
        prev = cur
    return prev[-1]


def _norm(s: str) -> str:
    return " ".join("".join(c for c in s.casefold() if c.isalnum() or c.isspace()).split())


def wer(reference: str, hypothesis: str) -> float:
    ref = _norm(reference).split()
    return edit_distance(ref, _norm(hypothesis).split()) / max(len(ref), 1)


def cer(reference: str, hypothesis: str) -> float:
    ref = _norm(reference).replace(" ", "")
    return edit_distance(ref, _norm(hypothesis).replace(" ", "")) / max(len(ref), 1)


@dataclass(frozen=True)
class Fact:
    category: str
    label: str
    polarity: str = "present"
    strength_value: float | None = None

    @property
    def key(self) -> tuple[str, str]:
        return self.category, _norm(self.label)


@dataclass
class FactScores:
    gold: int = 0
    predicted: int = 0
    matched: int = 0
    polarity_agree: int = 0
    strength_agree: int = 0
    strength_compared: int = 0
    unsupported: int = 0  # predicted facts without a gold counterpart
    critical_unsupported: list[str] = field(default_factory=list)
    critical_missed: list[str] = field(default_factory=list)

    @property
    def precision(self) -> float:
        return self.matched / self.predicted if self.predicted else 1.0

    @property
    def recall(self) -> float:
        return self.matched / self.gold if self.gold else 1.0

    @property
    def negation_fidelity(self) -> float:
        return self.polarity_agree / self.matched if self.matched else 1.0

    def as_dict(self) -> dict[str, object]:
        return {
            "precision": round(self.precision, 3),
            "recall": round(self.recall, 3),
            "negation_fidelity": round(self.negation_fidelity, 3),
            "strength_exact": f"{self.strength_agree}/{self.strength_compared}",
            "unsupported": self.unsupported,
            "critical_unsupported": self.critical_unsupported,
            "critical_missed": self.critical_missed,
        }


def score_facts(gold: Sequence[Fact], predicted: Sequence[Fact]) -> FactScores:
    s = FactScores(gold=len(gold), predicted=len(predicted))
    by_key = {g.key: g for g in gold}
    seen: set[tuple[str, str]] = set()
    for p in predicted:
        g = by_key.get(p.key)
        if g is None:
            s.unsupported += 1
            if p.category in CRITICAL:
                s.critical_unsupported.append(p.label)
            continue
        seen.add(p.key)
        s.matched += 1
        # a gold "absent"/"uncertain" reported as anything but "present" preserves the negation
        if (g.polarity == "present") == (p.polarity == "present"):
            s.polarity_agree += 1
        if g.strength_value is not None:
            s.strength_compared += 1
            s.strength_agree += g.strength_value == p.strength_value
    s.critical_missed = [g.label for g in gold if g.key not in seen and g.category in CRITICAL]
    return s

"""Pure note renderers: SOAP and concise views built from the approved content and the verified
facts. They never add a fact; rejected facts are left out, uncertain ones are marked."""

from collections.abc import Sequence

from medvya_ai.schemas import FactOut, NoteContent

SOAP_SECTIONS: tuple[tuple[str, str], ...] = (
    ("chief_complaint", "Chief complaint"),
    ("hpi", "History of present illness"),
    ("subjective", "Subjective"),
    ("objective", "Objective"),
    ("assessment", "Assessment"),
    ("plan", "Plan"),
)
CATEGORY_TITLES = {
    "symptom": "Symptoms",
    "diagnosis": "Diagnoses",
    "medication": "Medications",
    "allergy": "Allergies",
    "vital": "Vitals",
    "exam_finding": "Examination",
    "investigation": "Investigations",
    "follow_up": "Follow-up",
    "advice": "Advice",
    "history": "History",
}


def soap_sections(content: NoteContent) -> list[tuple[str, str]]:
    """(title, text) for every non-empty SOAP field, in clinical order."""
    out = []
    for key, title in SOAP_SECTIONS:
        text = getattr(content, key).strip()
        if text:
            out.append((title, text))
    return out


def fact_line(f: FactOut) -> str:
    parts = [f.label]
    if f.polarity == "absent":
        parts.insert(0, "No")
    elif f.polarity == "uncertain":
        parts.append("(uncertain)")
    if f.value_numeric is not None:
        parts.append(f"{f.value_numeric:g} {f.value_unit or ''}".strip())
    if f.body_site:
        parts.append(f"[{f.body_site}]")
    m = f.medication
    if m is not None:
        detail = [
            f"{m.strength_value:g} {m.strength_unit or ''}".strip() if m.strength_value else None,
            m.dose_text,
            m.frequency_code or m.frequency_text,
            f"{m.duration_value} {m.duration_unit}{'s' if m.duration_value != 1 else ''}"
            if m.duration_value
            else None,
            m.route,
            m.instructions,
        ]
        parts.append(", ".join(x for x in detail if x))
    return " ".join(p for p in parts if p).strip()


def fact_groups(facts: Sequence[FactOut]) -> list[tuple[str, list[str]]]:
    """Facts by category in CATEGORY_TITLES order, rejected ones excluded."""
    out: list[tuple[str, list[str]]] = []
    for cat, title in CATEGORY_TITLES.items():
        lines = [fact_line(f) for f in facts if f.category == cat and f.review_state != "rejected"]
        if lines:
            out.append((title, lines))
    return out


def concise_text(content: NoteContent, facts: Sequence[FactOut]) -> str:
    """Summary, then one bullet list per category, then what the clinician must still confirm."""
    blocks: list[str] = []
    if content.summary.strip():
        blocks.append(content.summary.strip())
    for title, lines in fact_groups(facts):
        blocks.append(title + "\n" + "\n".join(f"- {line}" for line in lines))
    if content.missing_or_uncertain:
        blocks.append(
            "To confirm\n" + "\n".join(f"- {x}" for x in content.missing_or_uncertain if x.strip())
        )
    return "\n\n".join(blocks)

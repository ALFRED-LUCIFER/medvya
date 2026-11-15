"""Schemas must be usable as Foundry strict response_format (provider-notes.md section 2)."""

from typing import Any

import pytest

from medvya_ai.extraction.schemas import ExtractionOut, NormaliseOut

UNSUPPORTED = {
    "minLength",
    "maxLength",
    "pattern",
    "format",
    "minimum",
    "maximum",
    "multipleOf",
    "minItems",
    "maxItems",
    "uniqueItems",
    "default",
}


def _objects(node: Any) -> list[dict[str, Any]]:
    out: list[dict[str, Any]] = []
    if isinstance(node, dict):
        if node.get("type") == "object" or "properties" in node:
            out.append(node)
        for v in node.values():
            out += _objects(v)
    elif isinstance(node, list):
        for v in node:
            out += _objects(v)
    return out


def _keys(node: Any) -> set[str]:
    if isinstance(node, dict):
        return set(node) | {k for v in node.values() for k in _keys(v)}
    if isinstance(node, list):
        return {k for v in node for k in _keys(v)}
    return set()


@pytest.mark.parametrize("model", [NormaliseOut, ExtractionOut])
def test_schema_is_strict_mode_compatible(model: type) -> None:
    schema = model.model_json_schema()  # type: ignore[attr-defined]
    objs = _objects(schema)
    assert objs
    for o in objs:
        props = o.get("properties", {})
        assert o.get("additionalProperties") is False
        assert set(o.get("required", [])) == set(props), "every field must be required"
    # "default"/"minimum" etc. as *property names* are fine; only schema keywords matter.
    keywords = {k for o in objs for p in o.get("properties", {}).values() for k in _keys(p)}
    assert not (keywords & (UNSUPPORTED - {"default", "format"})) and "default" not in keywords
    assert sum(len(o.get("properties", {})) for o in objs) <= 100


def test_category_enum_matches_ac4() -> None:
    cats = ExtractionOut.model_json_schema()["$defs"]["FactIn"]["properties"]["category"]["enum"]
    assert set(cats) == {
        "symptom",
        "diagnosis",
        "medication",
        "allergy",
        "vital",
        "exam_finding",
        "investigation",
        "follow_up",
        "advice",
        "history",
    }

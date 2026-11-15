import pytest

from medvya_ai.errors import AppError
from medvya_ai.services.note import NoteContent, ensure_editable


def test_draft_is_editable() -> None:
    ensure_editable("draft")


def test_approved_note_rejects_edits_with_409() -> None:
    with pytest.raises(AppError) as exc:
        ensure_editable("approved")
    assert exc.value.status == 409 and exc.value.code == "NOTE_APPROVED"


def test_note_content_defaults_and_forbids_unknown_keys() -> None:
    c = NoteContent.model_validate({"summary": "s"})
    assert c.model_dump()["missing_or_uncertain"] == [] and c.hpi == ""
    with pytest.raises(ValueError, match="extra"):
        NoteContent.model_validate({"summary": "s", "diagnosis_code": "x"})

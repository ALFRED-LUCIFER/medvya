"""F7: segment_no / seq are bounded and per-session caps exist."""

import pytest
from pydantic import ValidationError

from medvya_ai.config import Settings
from medvya_ai.main import create_app
from medvya_ai.schemas import MAX_INDEX, SegmentIn
from medvya_ai.storage import MemoryBlobStore


def test_segment_no_is_bounded() -> None:
    ok = {"mime_type": "audio/webm", "start_offset_ms": 0}
    assert SegmentIn(segment_no=MAX_INDEX, **ok).segment_no == MAX_INDEX  # type: ignore[arg-type]
    with pytest.raises(ValidationError):
        SegmentIn(segment_no=MAX_INDEX + 1, **ok)  # type: ignore[arg-type]


def test_path_indexes_are_bounded_in_openapi(settings: Settings) -> None:
    spec = create_app(settings, MemoryBlobStore()).openapi()
    path = next(p for p in spec["paths"] if p.endswith("/chunks/{seq}"))
    params = {p["name"]: p["schema"] for p in spec["paths"][path]["put"]["parameters"]}
    assert params["seq"]["maximum"] == MAX_INDEX and params["segment_no"]["maximum"] == MAX_INDEX


def test_session_caps_have_defaults(settings: Settings) -> None:
    assert settings.max_session_chunks > 0 and settings.max_session_bytes > settings.max_chunk_bytes

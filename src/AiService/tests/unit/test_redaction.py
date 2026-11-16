"""PHI never reaches logs or telemetry (AC5), plus HTTP hardening."""

import logging
from pathlib import Path

import httpx
import pytest
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import SimpleSpanProcessor
from opentelemetry.sdk.trace.export.in_memory_span_exporter import InMemorySpanExporter
from opentelemetry.trace import Status, StatusCode

from medvya_ai.config import Settings
from medvya_ai.main import create_app
from medvya_ai.storage import MemoryBlobStore
from medvya_ai.telemetry import (
    REDACTED,
    RedactingLogFilter,
    RedactingSpanProcessor,
    configure_telemetry,
    scrub_text,
)

SRC = Path(__file__).resolve().parents[2] / "src" / "medvya_ai"


def test_span_attributes_events_and_status_are_redacted() -> None:
    exporter = InMemorySpanExporter()
    provider = TracerProvider()
    provider.add_span_processor(RedactingSpanProcessor())  # before the exporting processor
    provider.add_span_processor(SimpleSpanProcessor(exporter))
    with provider.get_tracer("t").start_as_current_span("work") as span:
        span.set_attribute("http.method", "POST")
        span.set_attribute("patient_ref", "P-123")
        span.set_attribute("transcript.text", "I have diabetes")
        span.set_attribute("http.target", "/x?email=a@b.example")
        span.add_event(
            "exception", {"exception.message": "diabetes", "exception.type": "ValueError"}
        )
        span.set_status(Status(StatusCode.ERROR, "diabetes"))
    (done,) = exporter.get_finished_spans()
    attrs = dict(done.attributes or {})
    assert attrs["http.method"] == "POST"
    assert attrs["patient_ref"] == attrs["transcript.text"] == REDACTED
    assert "a@b.example" not in str(attrs["http.target"])
    assert done.events[0].attributes["exception.message"] == REDACTED  # type: ignore[index]
    assert done.events[0].attributes["exception.type"] == "ValueError"  # type: ignore[index]
    assert not done.status.description


def test_log_filter_scrubs_message_exception_and_extras(caplog: pytest.LogCaptureFixture) -> None:
    rec = logging.LogRecord(
        "medvya_ai.x",
        logging.ERROR,
        __file__,
        1,
        "login %s token Bearer abc.def.ghi",
        ("a@b.example",),
        None,
    )
    rec.patient_name = "Asha"
    rec.session_id = "keep-me"
    try:
        raise ValueError("patient has diabetes")
    except ValueError:
        import sys

        rec.exc_info = sys.exc_info()
    assert RedactingLogFilter().filter(rec)
    out = rec.getMessage()
    assert "a@b.example" not in out and "abc.def.ghi" not in out
    assert rec.patient_name == REDACTED and rec.session_id == "keep-me"
    assert rec.exc_info is None and rec.exc_text == "ValueError"
    assert "diabetes" not in str(rec.__dict__)


def test_scrub_text_masks_long_numbers() -> None:
    assert "9876543210" not in scrub_text("call 9876543210")


def test_telemetry_disabled_without_connection_string(settings: Settings) -> None:
    assert configure_telemetry(settings) is False


@pytest.fixture
def client(settings: Settings) -> httpx.AsyncClient:
    app = create_app(settings, MemoryBlobStore())
    return httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://t")


async def test_oversized_json_body_is_413_with_envelope(client: httpx.AsyncClient) -> None:
    r = await client.post("/api/v1/sessions", content=b"x" * (1024 * 1024 + 1))
    assert r.status_code == 413 and r.json()["error"]["code"] == "PAYLOAD_TOO_LARGE"

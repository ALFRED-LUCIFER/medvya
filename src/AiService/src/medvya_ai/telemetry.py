"""OpenTelemetry to Azure Monitor, enabled only when APPLICATIONINSIGHTS_CONNECTION_STRING is set.

PHI policy: request and response bodies are never recorded (no instrumentation here captures
them). As a second line of defence, span attributes/events and log records are scrubbed by key
name and by pattern before export, and exception messages are dropped (they can echo input).
"""

import logging
import re
from collections.abc import Mapping
from typing import Any

from opentelemetry.context import Context
from opentelemetry.sdk.trace import Event, ReadableSpan, Span, SpanProcessor
from opentelemetry.trace import Status

from medvya_ai.config import Settings

REDACTED = "[redacted]"
# Keys that can carry patient or credential content. Substring match, case-insensitive.
PHI_KEY_RE = re.compile(
    r"patient|transcript|quote|english_text|original_text|note|content|body|payload|"
    r"name|email|phone|password|secret|token|authorization|cookie|key|"
    r"exception\.message|exception\.stacktrace|db\.statement|db\.query",
    re.IGNORECASE,
)
_TEXT_PATTERNS = (
    (re.compile(r"[\w.+-]+@[\w-]+\.[\w.-]+"), "[email]"),
    (re.compile(r"(?i)bearer\s+[\w.~+/=-]+"), "Bearer [token]"),
    (re.compile(r"\b\d{10,}\b"), "[number]"),
)
# LogRecord attributes that exist on every record; anything else came from `extra=`.
_STD_RECORD = frozenset(vars(logging.LogRecord("", 0, "", 0, "", None, None))) | {
    "message",
    "asctime",
    "taskName",
}


def scrub_text(s: str) -> str:
    for pattern, repl in _TEXT_PATTERNS:
        s = pattern.sub(repl, s)
    return s


def redact_attrs(attrs: Mapping[str, Any] | None) -> dict[str, Any]:
    return {
        k: REDACTED if PHI_KEY_RE.search(k) else scrub_text(v) if isinstance(v, str) else v
        for k, v in (attrs or {}).items()
    }


class RedactingSpanProcessor(SpanProcessor):
    """Must be registered before the exporting processor: it rewrites the span in place."""

    def on_start(self, span: Span, parent_context: Context | None = None) -> None:
        return None

    def on_end(self, span: ReadableSpan) -> None:
        state = vars(span)
        state["_attributes"] = redact_attrs(span.attributes)
        state["_events"] = [
            Event(e.name, redact_attrs(e.attributes), e.timestamp) for e in span.events
        ]
        state["_status"] = Status(span.status.status_code)  # drops the free-text description


class RedactingLogFilter(logging.Filter):
    def filter(self, record: logging.LogRecord) -> bool:
        record.msg, record.args = scrub_text(record.getMessage()), None
        if record.exc_info:  # keep the exception class, drop message and traceback text
            record.exc_text = record.exc_info[0].__name__ if record.exc_info[0] else None
            record.exc_info = None
        record.stack_info = None
        for key in set(vars(record)) - _STD_RECORD:
            if PHI_KEY_RE.search(key):
                setattr(record, key, REDACTED)
        return True


def install_log_filter() -> None:
    """Attach the filter to every root handler and to the 'medvya_ai' logger's handlers."""
    f = RedactingLogFilter()
    for logger in (logging.getLogger(), logging.getLogger("medvya_ai")):
        for h in logger.handlers:
            if not any(isinstance(x, RedactingLogFilter) for x in h.filters):
                h.addFilter(f)


def configure_telemetry(settings: Settings) -> bool:
    """Returns True when Azure Monitor export was enabled."""
    conn = settings.applicationinsights_connection_string
    if conn is None or not conn.get_secret_value():
        install_log_filter()
        return False
    from azure.monitor.opentelemetry import configure_azure_monitor

    configure_azure_monitor(
        connection_string=conn.get_secret_value(),
        span_processors=[RedactingSpanProcessor()],
        logger_name="medvya_ai",  # export app logs only, not library chatter
    )
    install_log_filter()  # after configure: the OTel LoggingHandler now exists
    return True

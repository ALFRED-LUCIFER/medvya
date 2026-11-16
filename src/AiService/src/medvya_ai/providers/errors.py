"""Provider failures. `code` is safe for processing_job.last_error: never provider bodies."""

from collections.abc import Mapping

# Per provider call: 3 tries (2 retries). The job itself retries MODEL_JOB_MAX_ATTEMPTS times.
BACKOFF_SECONDS = (2, 8)
TIMEOUT_SECONDS = 90.0  # one fast-transcription or model call; a hung connection fails fast
MAX_RETRY_AFTER_SECONDS = 60.0


def retry_delay(headers: Mapping[str, str] | None, default: float) -> float:
    """Honour a numeric Retry-After (429/503) up to a cap; otherwise our backoff."""
    raw = (headers or {}).get("retry-after", "")
    try:
        return min(max(float(raw), 0.0), MAX_RETRY_AFTER_SECONDS)
    except ValueError:
        return default


class ProviderError(Exception):
    def __init__(self, code: str) -> None:
        super().__init__(code)
        self.code = code


class PipelineError(ProviderError):
    """A job-level failure (missing audio, ids not preserved ...). Same safe-code contract."""

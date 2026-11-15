"""Provider failures. `code` is safe for processing_job.last_error: never provider bodies."""


class ProviderError(Exception):
    def __init__(self, code: str) -> None:
        super().__init__(code)
        self.code = code


class PipelineError(ProviderError):
    """A job-level failure (missing audio, ids not preserved ...). Same safe-code contract."""

from medvya_ai.config import Settings
from medvya_ai.providers.errors import ProviderError
from medvya_ai.providers.transcription.azure_fast import AzureFastTranscriber
from medvya_ai.providers.transcription.base import Transcriber
from medvya_ai.providers.transcription.fake import FakeTranscriber


def build_transcriber(settings: Settings) -> Transcriber:
    if settings.transcription_provider == "fake":
        return FakeTranscriber()
    if settings.speech_key is None:
        raise RuntimeError("SPEECH_KEY is required when TRANSCRIPTION_PROVIDER=azure_fast")
    settings.allowed_host(settings.speech_endpoint_url)  # in-country only, checked at startup
    return AzureFastTranscriber(
        endpoint=settings.speech_endpoint_url,
        api_key=settings.speech_key.get_secret_value(),
        api_version=settings.speech_api_version,
    )


__all__ = ["ProviderError", "build_transcriber"]

from dataclasses import dataclass

from medvya_ai.config import Settings
from medvya_ai.crypto import KeyWrapper
from medvya_ai.providers.llm.base import LLMClient
from medvya_ai.providers.transcription.base import Transcriber
from medvya_ai.storage import BlobStore


@dataclass
class Deps:
    settings: Settings
    blobs: BlobStore
    wrapper: KeyWrapper
    transcriber: Transcriber
    llm: LLMClient

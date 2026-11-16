"""Environment-driven settings. Every key is documented in the repository .env.example."""

import base64
import binascii
from functools import lru_cache
from typing import Literal

from pydantic import SecretStr, field_validator, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    # Only the service's own .env (src/AiService/.env): never the Medvya root .env.
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    database_url: SecretStr
    database_url_migrate: SecretStr | None = None

    azure_storage_connection_string: SecretStr | None = None
    blob_container: str = "ai-audio"

    speech_region: str = "centralindia"
    speech_key: SecretStr | None = None
    speech_endpoint: str | None = None
    speech_api_version: str = "2025-10-15"
    speech_locales: str = "hi-IN,en-IN"
    speech_max_speakers: int = 3

    foundry_endpoint: str | None = None
    foundry_api_key: SecretStr | None = None
    foundry_deployment_extract: str | None = None
    foundry_deployment_normalise: str | None = None

    # No silent fake default: a deployment that forgets the variable must fail, not emit fake notes.
    transcription_provider: Literal["azure_fast", "fake"] = "azure_fast"
    llm_provider: Literal["foundry", "fake"] = "foundry"

    # development | test | production ... Only "development" may run AUTH_MODE=disabled.
    environment: str = "production"

    # service: Medvya's API calls with its managed-identity token (aud, issuer, allow-listed
    #   client id, app role) and asserts tenant, user and role in headers (ADR-021 pilot gate 1).
    # entra: a per-user bearer token mapped to app_user.external_subject.
    # disabled: no token, X-Clinic-Id picks the clinic (ENVIRONMENT=development only).
    auth_mode: Literal["service", "entra", "disabled"] = "service"
    auth_issuer: str | None = None
    auth_audience: str | None = None
    auth_jwks_url: str | None = None
    auth_allowed_client_ids: str = ""  # comma separated azp/appid of the callers (service mode)
    auth_required_role: str | None = None  # app role the caller's token must carry (service mode)

    retention_audio_after_approval_hours: int = 24
    retention_draft_audio_days: int = 7

    encryption_master_key: SecretStr

    max_chunk_bytes: int = 8 * 1024 * 1024
    max_session_chunks: int = 20_000
    max_session_bytes: int = 512 * 1024 * 1024
    segment_max_ms: int = 3_600_000
    worker_poll_seconds: float = 2.0
    applicationinsights_connection_string: SecretStr | None = None

    @field_validator("database_url", "database_url_migrate")
    @classmethod
    def _asyncpg_driver(cls, v: SecretStr | None) -> SecretStr | None:
        if v is None:
            return None
        url = v.get_secret_value()
        if url.startswith("postgresql://"):
            url = url.replace("postgresql://", "postgresql+asyncpg://", 1)
        return SecretStr(url)

    @field_validator("encryption_master_key")
    @classmethod
    def _master_key_is_32_bytes(cls, v: SecretStr) -> SecretStr:
        try:
            raw = base64.b64decode(v.get_secret_value(), validate=True)
        except (binascii.Error, ValueError) as exc:
            raise ValueError("ENCRYPTION_MASTER_KEY must be base64") from exc
        if len(raw) != 32:
            raise ValueError("ENCRYPTION_MASTER_KEY must decode to exactly 32 bytes")
        return v

    @model_validator(mode="after")
    def _disabled_only_in_development(self) -> "Settings":
        if self.auth_mode == "disabled" and self.environment != "development":
            raise ValueError("AUTH_MODE=disabled is allowed only with ENVIRONMENT=development")
        return self

    @staticmethod
    def csv(value: str) -> list[str]:
        return [v.strip() for v in value.split(",") if v.strip()]

    @property
    def master_key_bytes(self) -> bytes:
        return base64.b64decode(self.encryption_master_key.get_secret_value())


@lru_cache
def get_settings() -> Settings:
    return Settings()

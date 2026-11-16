"""App wiring checks that need no database."""

import httpx
import pytest

from medvya_ai.config import Settings
from medvya_ai.main import create_app
from medvya_ai.storage import MemoryBlobStore


@pytest.fixture
def client(settings: Settings) -> httpx.AsyncClient:
    app = create_app(settings, MemoryBlobStore())
    return httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://t")


async def test_healthz_and_validation_shape(client: httpx.AsyncClient) -> None:
    assert (await client.get("/healthz")).json() == {"status": "ok"}
    r = await client.get("/api/v1/sessions", headers={"X-Clinic-Id": "not-a-uuid"})
    assert r.status_code == 400 and r.json()["error"]["code"] == "TENANT_HEADER_INVALID"
    assert "input" not in str(r.json())


def test_settings_reject_weak_secrets() -> None:
    with pytest.raises(ValueError, match="32"):
        Settings(
            database_url="postgresql://a/b",
            encryption_master_key="AAAA",  # type: ignore[call-arg]
            _env_file=None,
        )


def test_entra_mode_refuses_to_start_without_issuer_and_audience(settings: Settings) -> None:
    from medvya_ai.auth import build_verifier

    with pytest.raises(RuntimeError, match="AUTH_ISSUER"):
        build_verifier(settings.model_copy(update={"auth_mode": "entra"}))

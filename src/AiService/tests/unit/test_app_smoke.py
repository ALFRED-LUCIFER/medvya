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


async def test_every_api_route_requires_authentication(settings: Settings) -> None:
    """Service mode, no token: every operation in the OpenAPI document is 401 before anything
    else runs, and outside development nothing but the health probes is served without one."""
    # SAFETY: mutation check: replaced `_: TenantDep` on GET /consent-notice with no parameter ->
    # this test failed
    import re
    import uuid

    from medvya_ai.auth import Claims

    class NeverCalled:
        async def verify(self, token: str) -> Claims:
            raise AssertionError("no token was sent")

    prod = settings.model_copy(update={"auth_mode": "service", "environment": "production"})
    app = create_app(prod, MemoryBlobStore(), verifier=NeverCalled())
    public = {"/healthz", "/readyz"}
    paths = app.openapi()["paths"]
    ops = [(m, path) for path, item in paths.items() if path not in public for m in item]
    assert len(ops) > 10
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://t") as c:
        for method, path in ops:
            url = re.sub(
                r"\{[^}]+\}",
                lambda m: "0" if m.group(0) in ("{segment_no}", "{seq}") else str(uuid.uuid4()),
                path,
            )
            r = await c.request(method, url)
            assert r.status_code == 401, (method, path, r.status_code)
        assert (await c.get("/openapi.json")).status_code == 404
        assert (await c.get("/docs")).status_code == 404
    served = {getattr(r, "path", None) for r in app.routes} - {None}
    assert served == public

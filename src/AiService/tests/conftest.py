"""Shared fixtures. Integration tests need PostgreSQL and are skipped (never failed) without it."""

import asyncio
import base64
import os
import secrets
from collections.abc import AsyncIterator, Callable
from pathlib import Path

import asyncpg
import httpx
import pytest
from alembic import command
from alembic.config import Config

from medvya_ai.config import Settings
from medvya_ai.main import create_app
from medvya_ai.seed import create_clinic_with_user
from medvya_ai.storage import MemoryBlobStore

API_DIR = Path(__file__).resolve().parents[1]
APP_URL = os.environ.get("MEDVYA_AI_TEST_DATABASE_URL", "")
OWNER_URL = os.environ.get("MEDVYA_AI_TEST_DATABASE_URL_MIGRATE", "")


def _dsn(url: str) -> str:
    return url.replace("postgresql+asyncpg://", "postgresql://", 1)


async def _probe(url: str) -> str | None:
    try:
        conn = await asyncpg.connect(_dsn(url), timeout=3)
        await conn.close()
    except Exception as exc:  # unreachable, bad credentials, no Docker: all mean skip
        return f"{type(exc).__name__}"
    return None


def _skip_reason() -> str | None:
    if not APP_URL or not OWNER_URL:
        return "MEDVYA_AI_TEST_DATABASE_URL / _MIGRATE not set"
    if not APP_URL.rsplit("/", 1)[-1].endswith("_test"):
        return "test database name must end in _test (suite drops its schema)"
    for url in (APP_URL, OWNER_URL):
        err = asyncio.run(_probe(url))
        if err:
            return f"PostgreSQL unreachable ({err}); start docker compose to run integration tests"
    return None


def pytest_collection_modifyitems(config: pytest.Config, items: list[pytest.Item]) -> None:
    integration = [i for i in items if "integration" in i.keywords]
    if not integration:
        return
    reason = _skip_reason()
    config.stash[_REASON] = reason
    if reason and os.environ.get("CI"):
        # In CI a quiet skip would be a green run that never touched the database.
        raise pytest.UsageError(f"integration tests cannot run in CI: {reason}")
    if reason:
        marker = pytest.mark.skip(reason=reason)
        for item in integration:
            item.add_marker(marker)


_REASON: pytest.StashKey[str | None] = pytest.StashKey()


@pytest.fixture(scope="session")
def schema() -> None:
    """Fresh schema via the real migration (also exercises downgrade). Sync on purpose:
    alembic's env.py runs its own event loop."""
    cfg = Config(str(API_DIR / "alembic.ini"))
    cfg.attributes["url"] = OWNER_URL
    command.downgrade(cfg, "base")
    command.upgrade(cfg, "head")


@pytest.fixture
def settings() -> Settings:
    return Settings(  # type: ignore[call-arg]
        database_url=APP_URL or "postgresql+asyncpg://unused/unused",
        encryption_master_key=base64.b64encode(os.urandom(32)).decode(),
        max_chunk_bytes=1024,
        auth_mode="disabled",
        environment="development",
        llm_provider="fake",
        _env_file=None,
    )


@pytest.fixture
def blobs() -> MemoryBlobStore:
    return MemoryBlobStore()


async def _clear_queue() -> None:
    """Tests share one database and one queue. A job left by an earlier test belongs to a session
    encrypted under that test's random master key, so the worker must never claim it here."""
    conn = await asyncpg.connect(_dsn(OWNER_URL))
    try:
        await conn.execute("DELETE FROM processing_job")
    finally:
        await conn.close()


@pytest.fixture
async def app_ctx(schema: None, settings: Settings, blobs: MemoryBlobStore):  # type: ignore[no-untyped-def]
    await _clear_queue()
    app = create_app(settings, blobs)
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        yield app, client
    await app.state.engine.dispose()


@pytest.fixture
def make_clinic(app_ctx):  # type: ignore[no-untyped-def]
    """Creates a clinic + doctor, returns (clinic_id, user_id, headers selecting that clinic)."""
    app, _ = app_ctx

    async def _make(name: str = "Clinic") -> tuple[object, object, dict[str, str]]:
        email = f"{secrets.token_hex(6)}@example.test"
        async with app.state.sessionmaker() as db, db.begin():
            cid, uid = await create_clinic_with_user(db, clinic_name=name, email=email)
        return cid, uid, {"X-Clinic-Id": str(cid)}

    return _make


Maker = Callable[..., object]


@pytest.fixture
async def client(app_ctx) -> AsyncIterator[httpx.AsyncClient]:  # type: ignore[no-untyped-def]
    yield app_ctx[1]

"""App factory: body limits, error handlers, telemetry. Only Medvya's API calls this service
(server to server, ADR-021 section 3), so there is no CORS."""

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI
from sqlalchemy import text

from medvya_ai.api import consent, result, review, segments, sessions
from medvya_ai.auth import TokenVerifier, build_verifier
from medvya_ai.config import Settings, get_settings
from medvya_ai.crypto import KeyWrapper, LocalKeyWrapper
from medvya_ai.db import make_engine, make_sessionmaker
from medvya_ai.errors import install_handlers
from medvya_ai.hardening import BodyLimitMiddleware
from medvya_ai.storage import AzureBlobStore, BlobStore
from medvya_ai.telemetry import configure_telemetry


def create_app(
    settings: Settings | None = None,
    blobs: BlobStore | None = None,
    key_wrapper: KeyWrapper | None = None,
    verifier: TokenVerifier | None = None,
) -> FastAPI:
    settings = settings or get_settings()
    configure_telemetry(settings)
    engine = make_engine(settings.database_url.get_secret_value())

    @asynccontextmanager
    async def lifespan(_: FastAPI) -> AsyncIterator[None]:
        yield
        await engine.dispose()

    dev = settings.environment == "development"  # schema and docs pages are unauthenticated
    app = FastAPI(
        title="Medvya AI API", lifespan=lifespan, openapi_url="/openapi.json" if dev else None
    )
    app.state.settings = settings
    app.state.engine = engine
    app.state.sessionmaker = make_sessionmaker(engine)
    app.state.key_wrapper = key_wrapper or LocalKeyWrapper(settings.master_key_bytes)
    app.state.verifier = verifier or build_verifier(settings)
    if blobs is None:
        conn = settings.azure_storage_connection_string
        if conn is None:
            raise RuntimeError("AZURE_STORAGE_CONNECTION_STRING is required")
        blobs = AzureBlobStore(conn.get_secret_value(), settings.blob_container)
    app.state.blobs = blobs
    install_handlers(app)
    app.add_middleware(BodyLimitMiddleware, max_chunk_bytes=settings.max_chunk_bytes)
    for r in (sessions.router, consent.router, segments.router, result.router, review.router):
        app.include_router(r, prefix="/api/v1")

    @app.get("/healthz")
    async def healthz() -> dict[str, str]:
        return {"status": "ok"}

    @app.get("/readyz")
    async def readyz() -> dict[str, str]:
        async with engine.connect() as c:
            await c.execute(text("SELECT 1"))
        await app.state.blobs.ping()
        return {"status": "ready"}

    return app

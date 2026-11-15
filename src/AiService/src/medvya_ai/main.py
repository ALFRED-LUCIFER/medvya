"""App factory: CORS, body limits, telemetry."""

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from sqlalchemy import text

from medvya_ai.api import (
    analytics,
    consent,
    export,
    privacy,
    receptionist,
    result,
    review,
    segments,
    sessions,
)
from medvya_ai.auth import TokenVerifier, build_verifier
from medvya_ai.config import Settings, get_settings
from medvya_ai.crypto import KeyWrapper, LocalKeyWrapper
from medvya_ai.db import make_engine, make_sessionmaker
from medvya_ai.errors import install_handlers
from medvya_ai.hardening import BodyLimitMiddleware
from medvya_ai.providers.llm import build_llm
from medvya_ai.providers.llm.base import LLMClient
from medvya_ai.storage import AzureBlobStore, BlobStore
from medvya_ai.telemetry import configure_telemetry


def create_app(
    settings: Settings | None = None,
    blobs: BlobStore | None = None,
    key_wrapper: KeyWrapper | None = None,
    verifier: TokenVerifier | None = None,
    llm: LLMClient | None = None,
) -> FastAPI:
    settings = settings or get_settings()
    configure_telemetry(settings)
    engine = make_engine(settings.database_url.get_secret_value())

    @asynccontextmanager
    async def lifespan(_: FastAPI) -> AsyncIterator[None]:
        yield
        await engine.dispose()

    app = FastAPI(title="Medvya AI API", lifespan=lifespan)
    app.state.settings = settings
    app.state.engine = engine
    app.state.sessionmaker = make_sessionmaker(engine)
    app.state.key_wrapper = key_wrapper or LocalKeyWrapper(settings.master_key_bytes)
    app.state.verifier = verifier or build_verifier(settings)
    app.state.llm = llm or build_llm(settings)  # receptionist replies run in-request
    if blobs is None:
        conn = settings.azure_storage_connection_string
        if conn is None:
            raise RuntimeError("AZURE_STORAGE_CONNECTION_STRING is required")
        blobs = AzureBlobStore(conn.get_secret_value(), settings.blob_container)
    app.state.blobs = blobs
    install_handlers(app)
    app.add_middleware(BodyLimitMiddleware, max_chunk_bytes=settings.max_chunk_bytes)
    # No cookies: no credentials mode, no wildcard origin.
    app.add_middleware(
        CORSMiddleware,
        allow_origins=[o for o in settings.cors_origin_list if o != "*"],
        allow_methods=["GET", "POST", "PUT", "PATCH"],
        allow_headers=["Authorization", "Content-Type", "X-Chunk-Sha256", "X-Clinic-Id"],
        allow_credentials=False,
        max_age=600,
    )
    for r in (
        analytics.router,
        sessions.router,
        consent.router,
        segments.router,
        result.router,
        review.router,
        export.router,
        privacy.router,
        receptionist.router,
    ):
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

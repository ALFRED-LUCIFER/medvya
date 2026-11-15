"""Alembic environment. Runs as the table owner (DATABASE_URL_MIGRATE), asyncpg driver."""

import asyncio
import os

from alembic import context
from sqlalchemy.engine import Connection
from sqlalchemy.ext.asyncio import create_async_engine

config = context.config
OFFLINE_FALLBACK_URL = "postgresql+asyncpg://offline/offline"


def _url() -> str:
    url = config.attributes.get("url") or os.environ.get("DATABASE_URL_MIGRATE")
    if not url:
        if context.is_offline_mode():
            return OFFLINE_FALLBACK_URL  # dialect only, never connected
        raise RuntimeError("DATABASE_URL_MIGRATE is not set")
    return str(url).replace("postgresql://", "postgresql+asyncpg://", 1)


def run_offline() -> None:
    context.configure(url=_url(), literal_binds=True, dialect_opts={"paramstyle": "named"})
    with context.begin_transaction():
        context.run_migrations()


def _run(connection: Connection) -> None:
    context.configure(connection=connection)
    with context.begin_transaction():
        context.run_migrations()


async def run_online() -> None:
    engine = create_async_engine(_url())
    async with engine.connect() as conn:
        await conn.run_sync(_run)
    await engine.dispose()


if context.is_offline_mode():
    run_offline()
else:
    asyncio.run(run_online())
